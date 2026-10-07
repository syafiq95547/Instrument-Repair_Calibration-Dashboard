#!/usr/bin/env bash
set -euo pipefail
repo_dir="/workspace/Instrument-Repair_Calibration-Dashboard"
export DOTNET_ROOT=/workspace/.dotnet
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export PATH="$DOTNET_ROOT:$PATH"
if ! "$DOTNET_ROOT/dotnet" --list-sdks 2>/dev/null | grep -q '^8\.0\.425 '; then
  curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh --output /tmp/instrumenthub-dotnet-install.sh
  bash /tmp/instrumenthub-dotnet-install.sh --version 8.0.425 --install-dir "$DOTNET_ROOT"
fi
cd "$repo_dir"
dotnet restore InstrumentHub.sln --locked-mode
dotnet tool restore
dotnet build InstrumentHub.sln --no-restore
dotnet run --project tests/InstrumentHub.Checks --no-build
