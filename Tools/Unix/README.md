# Running the tests of a repository on Unix

Some code behaves differently on Unix and must be tested there: the file permissions, the file locks (`flock(2)`), a
musl or ICU-less Linux. These scripts run the tests of any repository of the stack on Linux. From Windows, they use a
dedicated WSL distribution. They are the stack's tools: a stack decides on which systems its repositories are
tested, and the scripts are copied with the Stack when an LTS is created.

| File | |
|---|---|
| [Run-UnixTests.ps1](Run-UnixTests.ps1) | From Windows: the WSL distribution (installed, created, provisioned when needed), then `run-tests.sh` in it. |
| [provision.sh](provision.sh) | Provisions a Debian/Ubuntu or Alpine machine (as root): the packages, a regular `tester` user, the .NET SDK of the repository's `global.json`. |
| [run-tests.sh](run-tests.sh) | Runs the tests of a repository as a regular user, from a native file system, in the passes that the repository declares. |

## From Windows (WSL)

From the root of the stack, or with `-Repository` omitted from inside a repository:

```powershell
.PublicStack\Tools\Unix\Run-UnixTests.ps1 -Repository Core\CKomposable\CK-AppIdentity
```

If running scripts is disabled on the machine (the default execution policy), use:

```powershell
powershell -ExecutionPolicy Bypass -File .PublicStack\Tools\Unix\Run-UnixTests.ps1 -Repository Core\CKomposable\CK-AppIdentity
```

Everything is automated and idempotent:

1. **WSL is installed if needed.** This requires administrator rights (a UAC prompt) and a reboot: the script stops
   and must be run again after the reboot.
2. **A dedicated `CK-UnixTests` distribution is created** from the latest Ubuntu 24.04 WSL image (downloaded from
   https://releases.ubuntu.com/noble/, SHA256 checked, cached in `%LOCALAPPDATA%\CK-UnixTests\images`). It is
   independent of any other distribution and lives in `%LOCALAPPDATA%\CK-UnixTests\CK-UnixTests`.
3. **It is provisioned** by `provision.sh`: packages, a regular `tester` user and the .NET SDK required by the
   repository's `global.json` (the SDKs of the repositories tested in it add up). This is done again only when
   `provision.sh` or the `global.json` changes.
4. **The tests run** with `run-tests.sh` as `tester`, see below.

| Option | |
|---|---|
| `-Repository <folder>` | The repository to test. Defaults to the git working folder of the current directory. |
| `-Linux alpine-minimal` | Tests on Alpine (musl, BusyBox, no ICU: the .NET globalization is invariant) in the `CK-UnixTests-alpine-minimal` distribution, created from the Alpine mini root file system (about 4 MB). The default is `ubuntu` (glibc). |
| `-Reset` | Deletes the distribution and creates it again. |
| `-Provision` | Provisions again even if nothing changed. |
| `-Filter FullyQualifiedName~FileStore` | A `dotnet test --filter` expression. |
| `-TestArguments '--x','y'` | Additional `dotnet test` arguments (from a PowerShell session only: `powershell -File` can't pass an array). |
| `-Distribution <name>` / `-UbuntuRelease <codename>` | Another distribution name or Ubuntu release. |

The results are in `%LOCALAPPDATA%\CK-UnixTests\results\<repository>\<distribution>\<run time>`: nothing is written in
the repository.

To remove everything: `wsl --unregister CK-UnixTests` (and `CK-UnixTests-alpine-minimal`) and delete
`%LOCALAPPDATA%\CK-UnixTests`.

## On Linux or macOS

```bash
.PublicStack/Tools/Unix/run-tests.sh --source Core/CKomposable/CK-AppIdentity
```

The .NET SDK required by the repository's `global.json` and `rsync` must be installed: on Debian/Ubuntu or Alpine,
`sudo .PublicStack/Tools/Unix/provision.sh <repository> "$USER"` does it (with the `flock` command of util-linux).
The results are in `~/unix-tests/<repository>` (`--results` to change it).

## What run-tests.sh does

- It refuses to run as root: root bypasses the file permission checks.
- When the sources are on a file system that doesn't support the Unix permissions and locks (a WSL `/mnt/c` drive, a
  network or FUSE mount), they are copied with `rsync` into `--work` (by default `~/src/<repository>`) and the tests run
  there.
- It builds the test projects, then runs them in each pass. The `Logs` folder of a test project is moved to the
  results after each pass.

## What a repository declares

A repository with Unix specifics declares them in its `Tests/Unix/unix-tests.conf`, a bash file that `run-tests.sh`
sources. Without it, every `Tests/*/*.Tests.csproj` runs in one `default` pass (umask `022`).

```bash
# The test projects (folders relative to the repository). Default: every Tests/*/*.Tests.csproj.
projects=( "Tests/CK.AppIdentity.Tests" )
# The passes: "<name> <umask> [VAR=value...]". Default: one "default 022" pass.
passes=( "default 022" "no-dotnet-locking 022 DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1" "umask-027 027" )
# The folders of a test project deleted before each pass (and not copied). Default: none.
fresh=( "TestStore" )
```
