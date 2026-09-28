#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET="${DOTNET:-dotnet}"
"$DOTNET" build src/Jellyfin.Plugin.AutoLut.csproj --configuration Release
"$DOTNET" run --project tests/AutoLut.Tests.csproj --configuration Release
"${NODE:-node}" --test tests/web-player.test.mjs
