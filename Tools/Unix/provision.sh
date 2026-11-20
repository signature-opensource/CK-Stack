#!/usr/bin/env bash
# Provisions a Debian/Ubuntu or an Alpine machine (typically a dedicated "CK-UnixTests" WSL distribution
# created by Run-UnixTests.ps1) to run the tests of a repository of the stack. Must run as root. Idempotent.
#
#   provision.sh <repository-root> [user-name]
#
# - Installs the required packages (rsync to copy the sources, the flock command (for cross-process
#   lock tests), ICU for .NET on Debian/Ubuntu).
#   Alpine is kept minimal: musl, BusyBox and no ICU (the .NET globalization is invariant).
# - Creates a regular user (default "tester"): the tests must not run as root, root bypasses the
#   file permission checks.
# - Installs the .NET SDK required by the repository's global.json in /usr/share/dotnet (the SDKs of the
#   repositories tested in the machine add up).
# - In WSL, configures /etc/wsl.conf: the default user and no Windows PATH (a Windows dotnet.exe
#   must never be picked). The distribution must be restarted for this to apply (wsl --terminate).
set -euo pipefail

repo="${1:?Usage: provision.sh <repository-root> [user-name]}"
user="${2:-tester}"

if [ "$(id -u)" -ne 0 ]; then
    echo "provision.sh must run as root." >&2
    exit 1
fi

invariant_globalization=false
echo "== Packages"
if command -v apt-get > /dev/null; then
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq --no-install-recommends ca-certificates curl rsync util-linux libicu-dev git > /dev/null
elif command -v apk > /dev/null; then
    # util-linux-misc: the flock command. libgcc and libstdc++: required by .NET on musl. No ICU.
    apk add --no-cache -q bash ca-certificates curl rsync util-linux-misc libgcc libstdc++
    invariant_globalization=true
else
    echo "provision.sh supports Debian/Ubuntu (apt-get) and Alpine (apk). Install rsync, flock, ICU and the .NET SDK manually." >&2
    exit 1
fi
if [ "$invariant_globalization" = true ]; then
    # Without ICU, .NET doesn't start unless its globalization is invariant (this is also needed below).
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi

echo "== User '$user'"
if ! id "$user" > /dev/null 2>&1; then
    if command -v useradd > /dev/null; then
        useradd --create-home --shell /bin/bash "$user"
    else
        # BusyBox.
        adduser -D -s /bin/bash "$user"
    fi
fi

echo "== .NET SDK (from $repo/global.json)"
installer=/tmp/dotnet-install.sh
curl -sSL --retry 3 https://dot.net/v1/dotnet-install.sh -o "$installer"
# Remove the CRs: global.json is checked out with CRLF (see .gitattributes).
tr -d '\r' < "$repo/global.json" > /tmp/global.json
bash "$installer" --jsonfile /tmp/global.json --install-dir /usr/share/dotnet --no-path > /dev/null
ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
cat > /etc/profile.d/dotnet.sh << 'EOF'
export DOTNET_ROOT=/usr/share/dotnet
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
# The SDK is installed without workloads: the integrity check only emits a useless warning.
export DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK=true
EOF
if [ "$invariant_globalization" = true ]; then
    # Culture names are accepted (and behave like the invariant culture): otherwise the build warns
    # (NETSDK1188) for every satellite resource of the packages.
    cat >> /etc/profile.d/dotnet.sh << 'EOF'
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY=0
EOF
fi
dotnet --version

if grep -qi microsoft /proc/version 2> /dev/null; then
    echo "== WSL configuration"
    cat > /etc/wsl.conf << EOF
[user]
default=$user

[interop]
appendWindowsPath=false

[boot]
systemd=false
EOF
    # The Ubuntu WSL image may ask interactively to create a user on its first launch (OOBE):
    # the user is created above.
    if [ -f /etc/wsl-distribution.conf ]; then
        sed -i '/^\[oobe\]/,/^\[/{/^[[:space:]]*command[[:space:]]*=/d}' /etc/wsl-distribution.conf
    fi
fi

echo "== Provisioned."
