#Requires -Version 5.1
<#
.SYNOPSIS
Runs the tests of a repository of the stack on Linux, in a dedicated WSL distribution.

.DESCRIPTION
Everything is automated and idempotent:
  1. Installs WSL if needed (this requires administrator rights - a UAC prompt - and a reboot:
     the script stops and must be run again after the reboot).
  2. Creates the dedicated distribution from the latest image (its SHA256 is checked): by default
     "CK-UnixTests" from the Ubuntu WSL image of the release, or "CK-UnixTests-alpine-minimal" from the
     Alpine mini root file system (-Linux alpine-minimal: musl, BusyBox and no ICU, the .NET globalization
     is invariant). A distribution is independent of any other one: it can be reset at any time with -Reset.
     It lives in %LOCALAPPDATA%\CK-UnixTests.
  3. Provisions it (see provision.sh): packages, a regular "tester" user and the .NET SDK required
     by the global.json of the repository. This is done again only when provision.sh or the global.json
     changes (or with -Provision).
  4. Runs run-tests.sh as "tester": the working tree is copied into the Linux file system (the Windows
     drives don't support the Unix permissions and locks) and the tests run in the passes that the
     repository declares in its Tests/Unix/unix-tests.conf (one default pass otherwise).

The .trx results and the test logs are in %LOCALAPPDATA%\CK-UnixTests\results\<repository>\<distribution>\<run time>.
The exit code is 0 when all the passes succeeded.

.PARAMETER Repository
The repository to test: its folder. Defaults to the git working folder of the current directory.

.PARAMETER Linux
The Linux of the distribution: "ubuntu" (glibc, the default) or "alpine-minimal" (musl, no ICU).

.PARAMETER Distribution
The name of the dedicated WSL distribution. Defaults to "CK-UnixTests" for ubuntu and
"CK-UnixTests-alpine-minimal" for alpine-minimal.

.PARAMETER UbuntuRelease
The Ubuntu release code name (see https://releases.ubuntu.com/).

.PARAMETER Reset
Unregisters (deletes) the distribution and creates it again.

.PARAMETER Provision
Provisions the distribution even if provision.sh and global.json didn't change.

.PARAMETER Filter
A "dotnet test --filter" expression (like "FullyQualifiedName~FileStore").

.PARAMETER TestArguments
Additional arguments for "dotnet test". This is an array: it can't be used with "powershell -File"
(that passes '--x','y' as a single string), only from a PowerShell session.

.EXAMPLE
.PublicStack\Tools\Unix\Run-UnixTests.ps1 -Repository Core\CKomposable\CK-AppIdentity

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .PublicStack\Tools\Unix\Run-UnixTests.ps1 -Repository Core\CKomposable\CK-AppIdentity -Filter FullyQualifiedName~AtomicWriteAndLock

.EXAMPLE
.PublicStack\Tools\Unix\Run-UnixTests.ps1 -Repository Core\CKomposable\CK-AppIdentity -Linux alpine-minimal
#>
[CmdletBinding()]
param(
    [string] $Repository,
    [ValidateSet( 'ubuntu', 'alpine-minimal' )]
    [string] $Linux = 'ubuntu',
    [string] $Distribution,
    [string] $UbuntuRelease = 'noble',
    [switch] $Reset,
    [switch] $Provision,
    [string] $Filter,
    [string[]] $TestArguments = @()
)
if( $Filter ) { $TestArguments = @( '--filter', $Filter ) + $TestArguments }
if( -not $Distribution ) { $Distribution = if( $Linux -eq 'ubuntu' ) { 'CK-UnixTests' } else { "CK-UnixTests-$Linux" } }

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
# wsl.exe writes UTF-16 by default.
$env:WSL_UTF8 = '1'

# The repository: the git working folder (a ".git" folder or file) of the given folder or of the current directory.
$start = if( $Repository ) { (Resolve-Path $Repository).Path } else { (Get-Location).Path }
$repository = $start
while( $repository -and -not (Test-Path (Join-Path $repository '.git')) ) { $repository = Split-Path $repository -Parent }
if( -not $repository ) { throw "'$start' is not in a git working folder: -Repository <the repository folder> is required." }
if( -not (Test-Path (Join-Path $repository 'global.json')) ) { throw "'$repository' has no global.json: the .NET SDK to install is unknown." }
$name = Split-Path $repository -Leaf
$home_ = Join-Path $env:LOCALAPPDATA 'CK-UnixTests'
$results = Join-Path $home_ "results\$name\$Distribution"
New-Item -ItemType Directory -Force -Path $results | Out-Null
$user = 'tester'

function Write-Step( [string] $message ) { Write-Host "== $message" -ForegroundColor Cyan }

# Runs wsl.exe, its output goes to the console (not to the pipeline): returns the exit code.
function Invoke-Wsl( [string[]] $arguments ) {
    & wsl.exe @arguments | Out-Host
    return $LASTEXITCODE
}

# Runs wsl.exe and returns its standard output lines (null on error). The standard error is ignored:
# in Windows PowerShell, redirecting a native command's stderr raises errors.
function Get-WslOutput( [string[]] $arguments ) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & wsl.exe @arguments 2> $null
        if( $LASTEXITCODE -ne 0 ) { return $null }
        return @( $lines | ForEach-Object { ($_ -replace "`0", '').Trim() } | Where-Object { $_ } )
    }
    catch { return $null }
    finally { $ErrorActionPreference = $previous }
}

# Windows PowerShell returns the content as a byte array when the server sends no text content type.
function Get-WebText( [string] $uri ) {
    $content = (Invoke-WebRequest -Uri $uri -UseBasicParsing).Content
    if( $content -is [byte[]] ) { return [Text.Encoding]::UTF8.GetString( $content ) }
    return [string]$content
}

# Downloads (once: the images are cached) and checks an image. Returns its local path.
function Get-Image( [string] $url, [string] $fileName, [string] $expectedSha256 ) {
    $images = Join-Path $home_ 'images'
    New-Item -ItemType Directory -Force -Path $images | Out-Null
    $image = Join-Path $images $fileName
    if( -not (Test-Path $image) -or (Get-FileHash $image -Algorithm SHA256).Hash -ne $expectedSha256 ) {
        Write-Step "Downloading $url."
        Invoke-WebRequest -Uri $url -OutFile $image -UseBasicParsing
        $actual = (Get-FileHash $image -Algorithm SHA256).Hash
        if( $actual -ne $expectedSha256 ) {
            Remove-Item $image
            throw "SHA256 mismatch for '$url': expected $expectedSha256, got $actual."
        }
    }
    return $image
}

# The latest Ubuntu WSL image of the release.
function Get-UbuntuImage {
    $arch = if( $env:PROCESSOR_ARCHITECTURE -eq 'ARM64' ) { 'arm64' } else { 'amd64' }
    $index = "https://releases.ubuntu.com/$UbuntuRelease/"
    Write-Step "Looking for the latest Ubuntu '$UbuntuRelease' WSL image ($arch) in $index"
    $page = Get-WebText $index
    $pattern = "ubuntu-(\d+\.\d+(?:\.\d+)?)-wsl-$arch\.wsl"
    $latest = [regex]::Matches( $page, $pattern ) |
                ForEach-Object { [pscustomobject]@{ Name = $_.Value; Version = [version]$_.Groups[1].Value } } |
                Sort-Object Version -Unique | Select-Object -Last 1
    if( -not $latest ) { throw "No '$pattern' image found in $index." }
    $sumsUrl = "$($index)SHA256SUMS"
    $sums = Get-WebText $sumsUrl
    $expected = ([regex]::Match( $sums, "(?m)^([0-9a-f]{64}) \*?$([regex]::Escape( $latest.Name ))\s*$" )).Groups[1].Value
    if( -not $expected ) { throw "No SHA256 for '$($latest.Name)' in $sumsUrl." }
    # wsl --import expects a tar file: the .wsl image is a .tar.gz.
    return Get-Image "$index$($latest.Name)" ($latest.Name -replace '\.wsl$', '.tar.gz') $expected
}

# The latest stable Alpine mini root file system.
function Get-AlpineImage {
    $arch = if( $env:PROCESSOR_ARCHITECTURE -eq 'ARM64' ) { 'aarch64' } else { 'x86_64' }
    $index = "https://dl-cdn.alpinelinux.org/alpine/latest-stable/releases/$arch/"
    Write-Step "Looking for the latest Alpine mini root file system ($arch) in $index"
    $releases = Get-WebText "$($index)latest-releases.yaml"
    # The yaml is a list of "-" separated entries: the one of the "alpine-minirootfs" flavor.
    $entry = ($releases -split "(?m)^-\s*$") | Where-Object { $_ -match '(?m)^\s*flavor:\s*alpine-minirootfs\s*$' } | Select-Object -First 1
    if( -not $entry ) { throw "No alpine-minirootfs entry in $($index)latest-releases.yaml." }
    $file = ([regex]::Match( $entry, '(?m)^\s*file:\s*(\S+)\s*$' )).Groups[1].Value
    $sha256 = ([regex]::Match( $entry, '(?m)^\s*sha256:\s*([0-9a-f]{64})\s*$' )).Groups[1].Value
    if( -not $file -or -not $sha256 ) { throw "Invalid alpine-minirootfs entry in $($index)latest-releases.yaml." }
    return Get-Image "$index$file" $file $sha256
}

function Get-Distributions {
    $names = Get-WslOutput '--list', '--quiet'
    if( $null -eq $names ) { return @() }
    return $names
}

# 1. WSL itself (the Windows inbox wsl.exe stub only supports --install).
if( $null -eq (Get-WslOutput '--version') ) {
    Write-Step 'WSL is not installed: installing it (administrator rights are required).'
    $p = Start-Process -FilePath 'wsl.exe' -ArgumentList '--install', '--no-distribution' -Verb RunAs -Wait -PassThru
    if( $p.ExitCode -ne 0 ) { throw "WSL installation failed (exit code $($p.ExitCode))." }
    Write-Host 'WSL has been installed. Restart Windows, then run this script again.' -ForegroundColor Yellow
    exit 3
}

# 2. The dedicated distribution.
$exists = (Get-Distributions) -contains $Distribution
if( $exists -and $Reset ) {
    Write-Step "Unregistering '$Distribution'."
    if( (Invoke-Wsl '--unregister', $Distribution) -ne 0 ) { throw "Unable to unregister '$Distribution'." }
    $exists = $false
}
if( -not $exists ) {
    $image = if( $Linux -eq 'ubuntu' ) { Get-UbuntuImage } else { Get-AlpineImage }
    $location = Join-Path $home_ $Distribution
    Write-Step "Creating the '$Distribution' distribution in '$location'."
    New-Item -ItemType Directory -Force -Path $location | Out-Null
    if( (Invoke-Wsl '--import', $Distribution, $location, $image, '--version', '2') -ne 0 ) { throw "Unable to import '$Distribution'." }
}

# A Windows path seen from the distribution.
# (a function that returns a single line returns a string, not an array: @() ensures an array.)
function Get-WslPath( [string] $path ) {
    $p = @( Get-WslOutput '-d', $Distribution, '-u', 'root', '--exec', 'wslpath', '-a', ($path -replace '\\', '/') )
    if( $p.Count -eq 0 -or -not $p[0] ) { throw "Unable to compute the WSL path of '$path'." }
    return $p[0]
}
$wslRepository = Get-WslPath $repository
$wslTools = Get-WslPath $PSScriptRoot
$wslResults = Get-WslPath $results

# 3. Provisioning (only when needed).
$hash = (Get-FileHash -Algorithm SHA256 -InputStream ([IO.MemoryStream]::new( [Text.Encoding]::UTF8.GetBytes(
            $Linux + (Get-Content -Raw (Join-Path $PSScriptRoot 'provision.sh')) + (Get-Content -Raw (Join-Path $repository 'global.json')) )))).Hash
# "sh" (not bash): the Alpine mini root file system has only the BusyBox shell before its provisioning.
$provisioned = Get-WslOutput '-d', $Distribution, '-u', 'root', '--exec', 'sh', '-c', 'cat /etc/ck-unixtests.provisioned 2> /dev/null || true'
if( $Provision -or "$provisioned".Trim() -ne $hash ) {
    Write-Step "Provisioning '$Distribution'."
    $command = "(command -v bash > /dev/null || apk add --no-cache -q bash) && tr -d '\r' < '$wslTools/provision.sh' > /tmp/provision.sh && bash /tmp/provision.sh '$wslRepository' $user && echo $hash > /etc/ck-unixtests.provisioned"
    if( (Invoke-Wsl '-d', $Distribution, '-u', 'root', '--exec', 'sh', '-c', $command) -ne 0 ) { throw "Provisioning of '$Distribution' failed." }
    # /etc/wsl.conf is read when the distribution starts.
    Invoke-Wsl '--terminate', $Distribution | Out-Null
}

# 4. The tests.
Write-Step "Running the tests of '$name' in '$Distribution' as '$user'."
$extra = ''
if( $TestArguments.Count -gt 0 ) {
    $extra = ' -- ' + (($TestArguments | ForEach-Object { "'" + ($_ -replace "'", "'\''") + "'" }) -join ' ')
}
$command = "tr -d '\r' < '$wslTools/run-tests.sh' > /tmp/run-tests.sh && bash /tmp/run-tests.sh --source '$wslRepository' --work ~/src/$name --results '$wslResults'$extra"
$exitCode = Invoke-Wsl '-d', $Distribution, '-u', $user, '--exec', 'bash', '-lc', $command
if( $exitCode -eq 0 ) { Write-Host 'All the Unix test passes succeeded.' -ForegroundColor Green }
else { Write-Host "Unix tests FAILED (exit code $exitCode). See $results." -ForegroundColor Red }
exit $exitCode
