#!/usr/bin/env bash
set -euo pipefail
export DOTNET_ROOT=/workspace/.dotnet
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$DOTNET_ROOT:$PATH"
cd /workspace/Instrument-Repair_Calibration-Dashboard
# Live processes must be restarted in each task. Development defaults to a temporary demo.
exec dotnet run --project src/InstrumentHub.Api --no-build --urls http://0.0.0.0:5080
