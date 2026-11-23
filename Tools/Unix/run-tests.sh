#!/usr/bin/env bash
# Runs the tests of a repository of the stack on Linux or macOS, as a regular user, from a native file system.
#
#   run-tests.sh --source <dir> [--work <dir>] [--results <dir>] [-- <extra dotnet test arguments>]
#
# --source   The repository to test.
# --work     Copies (rsync) the source into this directory and runs the tests there. Defaults to
#            ~/src/<repository name> when the source is on a file system that doesn't support the Unix
#            permissions or locking semantics (a WSL /mnt/c drive, a network or FUSE mount).
# --results  Where the .trx results and the test logs are copied. Defaults to ~/unix-tests/<repository name>.
#
# The repository may declare what is specific to it in "Tests/Unix/unix-tests.conf" (a bash file, sourced):
#
#   # The test projects (folders relative to the repository). Default: every Tests/*/*.Tests.csproj.
#   projects=( "Tests/CK.AppIdentity.Tests" )
#   # The passes: "<name> <umask> [VAR=value...]". Default: one "default 022" pass.
#   passes=( "default 022" "no-dotnet-locking 022 DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1" "umask-027 027" )
#   # The folders of a test project deleted before each pass (and not copied). Default: none.
#   fresh=( "TestStore" )
#
# The "Logs" folder of a test project is moved to the results after each pass.
set -euo pipefail

source_dir=""
work_dir=""
results_dir=""
extra_args=()

while [ $# -gt 0 ]; do
    case "$1" in
        --source) source_dir="$(cd "$2" && pwd)"; shift 2 ;;
        --work) work_dir="$2"; shift 2 ;;
        --results) results_dir="$2"; shift 2 ;;
        --) shift; extra_args=("$@"); break ;;
        *) echo "Unknown argument '$1'." >&2; exit 2 ;;
    esac
done
[ -n "$source_dir" ] || { echo "--source <repository> is required." >&2; exit 2; }
name="$(basename "$source_dir")"
results_dir="${results_dir:-$HOME/unix-tests/$name}"

if [ "$(id -u)" -eq 0 ]; then
    echo "The tests must not run as root: root bypasses the file permission checks." >&2
    exit 1
fi
command -v dotnet > /dev/null || { echo "dotnet is not installed (see provision.sh)." >&2; exit 1; }

# The specifics of the repository.
projects=()
passes=( "default 022" )
fresh=()
conf="$source_dir/Tests/Unix/unix-tests.conf"
if [ -f "$conf" ]; then
    # shellcheck disable=SC1090
    source <(tr -d '\r' < "$conf")
fi

fs_type() {
    if [ "$(uname -s)" = "Linux" ]; then stat -f -c %T "$1"; else echo "native"; fi
}

source_fs="$(fs_type "$source_dir")"
case "$source_fs" in
    # BusyBox's stat (Alpine) reports a WSL Windows drive as UNKNOWN: copying is always safe.
    v9fs|9p|drvfs|fuseblk|nfs|cifs|smb2|UNKNOWN)
        work_dir="${work_dir:-$HOME/src/$name}" ;;
esac

if [ -n "$work_dir" ]; then
    echo "== Copying '$source_dir' ($source_fs) to '$work_dir'"
    mkdir -p "$work_dir"
    excludes=( --exclude 'bin/' --exclude 'obj/' --exclude '.vs/' --exclude 'node_modules/' --exclude 'Tests/**/Logs/' )
    for f in ${fresh[@]+"${fresh[@]}"}; do excludes+=( --exclude "Tests/**/$f/" ); done
    # The modes of a Windows drive are meaningless: they are normalized.
    rsync -a --delete --no-perms --no-owner --no-group --chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r \
          "${excludes[@]}" "$source_dir/" "$work_dir/"
    test_dir="$work_dir"
else
    test_dir="$source_dir"
fi

work_fs="$(fs_type "$test_dir")"
case "$work_fs" in
    v9fs|9p|drvfs|fuseblk|nfs|cifs|smb2)
        echo "'$test_dir' is on a '$work_fs' file system: the Unix permissions and locks can't be tested there. Use --work." >&2
        exit 1 ;;
esac

if [ ${#projects[@]} -eq 0 ]; then
    while IFS= read -r p; do projects+=( "$(dirname "${p#"$test_dir"/}")" ); done \
        < <(find "$test_dir/Tests" -mindepth 2 -maxdepth 2 -name '*.Tests.csproj' 2> /dev/null | sort)
fi
[ ${#projects[@]} -gt 0 ] || { echo "No test project (Tests/*/*.Tests.csproj, or \"projects\" in Tests/Unix/unix-tests.conf)." >&2; exit 1; }

run_id="$(date -u +%Y%m%dT%H%M%SZ)"
run_results="$results_dir/$run_id"
mkdir -p "$run_results"

echo "== $(uname -sm), $(dotnet --version), user $(id -un), tests of '$name' in '$test_dir' ($work_fs)"
echo "== Build"
for p in "${projects[@]}"; do dotnet build "$test_dir/$p" -c Debug -nologo -v quiet; done

failed=()
for pass in "${passes[@]}"; do
    read -r pass_name pass_umask pass_env <<< "$pass"
    echo "== Pass '$pass_name' (umask $pass_umask${pass_env:+, $pass_env})"
    for p in "${projects[@]}"; do
        project="$test_dir/$p"
        project_name="$(basename "$p")"
        # Each pass starts with fresh folders.
        for f in ${fresh[@]+"${fresh[@]}"}; do rm -rf "${project:?}/$f"; done
        # shellcheck disable=SC2086
        if ( umask "$pass_umask"; env $pass_env dotnet test "$project" -c Debug --no-build -nologo \
                    --logger "trx;LogFileName=$pass_name-$project_name.trx" --results-directory "$run_results" \
                    ${extra_args[@]+"${extra_args[@]}"} ); then
            echo "== '$project_name' succeeded in pass '$pass_name'."
        else
            echo "== '$project_name' FAILED in pass '$pass_name'."
            failed+=( "$pass_name/$project_name" )
        fi
        if [ -d "$project/Logs" ]; then
            mkdir -p "$run_results/$pass_name-$project_name-Logs"
            cp -r "$project/Logs/." "$run_results/$pass_name-$project_name-Logs/"
            rm -rf "$project/Logs"
        fi
    done
done

echo "== Results in '$run_results'"
if [ ${#failed[@]} -gt 0 ]; then
    echo "== FAILED: ${failed[*]}"
    exit 1
fi
echo "== All passes succeeded."
