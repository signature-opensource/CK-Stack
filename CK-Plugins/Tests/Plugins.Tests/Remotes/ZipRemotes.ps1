$ErrorActionPreference = "Stop"

$errors = @()

function Add-Error($message) {
    $script:errors += $message
}

# ------------------------------------------------------------------
# Validate current directory name
# ------------------------------------------------------------------
$currentDir = Get-Item -Path "."
if ($currentDir.Name -ne "Remotes") {
    Write-Error "Current directory must be named 'Remotes'. Found '$($currentDir.Name)'."
    exit 1
}

# ------------------------------------------------------------------
# Discover container directories (exclude root 'bare')
# ------------------------------------------------------------------
$containers = Get-ChildItem -Directory |
    Where-Object { $_.Name -ne "bare" }

if ($containers.Count -eq 0) {
    Write-Error "No container directories found in 'Remotes' (excluding 'bare')."
    exit 1
}

# ------------------------------------------------------------------
# Discover repositories (one level below containers)
# ------------------------------------------------------------------
$repos = foreach ($container in $containers) {
    Get-ChildItem -Path $container.FullName -Directory
}

if (-not $repos) {
    Write-Error "No repositories found under container directories."
    exit 1
}

# ------------------------------------------------------------------
# Validate each repository (collect all errors)
# ------------------------------------------------------------------
foreach ($repo in $repos) {
    Write-Host "Validating repository '$($repo.FullName)'..."

    $repoErrors = @()

    $gitDir = Join-Path $repo.FullName ".git"
    if (-not (Test-Path $gitDir)) {
        $repoErrors += "missing .git directory"
    }
    else {
        Push-Location $repo.FullName
        try {
            $isBare = git rev-parse --is-bare-repository 2>$null
            if ($isBare -ne "false") {
                $repoErrors += "is a bare repository"
            }

            $remotes = git remote
            if ($remotes) {
                $repoErrors += "has git remotes configured: $remotes"
            }

            $status = git status --porcelain
            if ($status) {
                $repoErrors += "working tree is dirty"
            }
        }
        catch {
            $repoErrors += "git command failed: $($_.Exception.Message)"
        }
        finally {
            Pop-Location
        }
    }

    foreach ($err in $repoErrors) {
        Add-Error "[$($repo.FullName)] $err"
    }
}

# ------------------------------------------------------------------
# Abort if any validation failed
# ------------------------------------------------------------------
if ($errors.Count -gt 0) {
    Write-Error "Validation failed with the following issues:"
    foreach ($err in $errors) {
        Write-Error "  $err"
    }
    exit 1
}

# ------------------------------------------------------------------
# Cleanup repositories (runs only if all valid)
# ------------------------------------------------------------------
foreach ($repo in $repos) {
    Write-Host "Cleaning repository '$($repo.FullName)'..."

    Push-Location $repo.FullName

    # Git cleanup
    git reflog expire --expire=now --expire-unreachable=now --all
    git prune
    git repack -ad

    # --------------------------------------------------------------
    # IDE / editor cleanup
    # --------------------------------------------------------------
    Get-ChildItem -Recurse -Directory -Force |
        Where-Object { $_.Name -in @(".vs", ".vscode", ".idea") } |
        ForEach-Object {
            Write-Host "  Removing IDE folder $($_.FullName)"
            Remove-Item -Recurse -Force $_.FullName
        }

    # --------------------------------------------------------------
    # SAFE .NET cleanup:
    # Delete bin/obj only when co-located with a project file
    # --------------------------------------------------------------
    Get-ChildItem -Recurse -File -Include *.csproj,*.fsproj,*.vbproj |
        ForEach-Object {
            $projectDir = $_.Directory.FullName

            foreach ($folderName in @("bin", "obj")) {
                $candidate = Join-Path $projectDir $folderName
                if (Test-Path $candidate) {
                    Write-Host "  Removing $candidate"
                    Remove-Item -Recurse -Force $candidate
                }
            }
        }

    Pop-Location
}

# ------------------------------------------------------------------
# Create zip archive (exclude root 'bare')
# ------------------------------------------------------------------
$zipPath = Join-Path $currentDir.FullName "Remotes.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Write-Host "Creating Remotes.zip..."
Compress-Archive -Path ($containers | ForEach-Object { $_.Name }) -DestinationPath $zipPath

Write-Host "Remotes.zip created successfully."
