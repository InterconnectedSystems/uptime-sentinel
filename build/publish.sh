#!/usr/bin/env bash
# Builds the Windows release into dist/SiteMonitor (cross-compiles from Linux or runs on Windows via Git Bash/WSL).
set -euo pipefail
cd "$(dirname "$0")/.."
OUT=dist/SiteMonitor
rm -rf "$OUT" && mkdir -p "$OUT"

COMMON=(-c Release -r win-x64 --self-contained true
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
        -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none)

dotnet test tests/SiteMonitor.Core.Tests --nologo -v quiet
dotnet publish src/SiteMonitor.Service "${COMMON[@]}" -o "$OUT"
dotnet publish src/SiteMonitor.Gui     "${COMMON[@]}" -o "$OUT"

cp build/install.ps1 build/uninstall.ps1 LICENSE "$OUT"/
cp docs/USER-GUIDE.md "$OUT"/README.md
rm -f "$OUT"/*.pdb
(cd dist && rm -f SiteMonitor-win-x64.zip && python3 -c "import shutil; shutil.make_archive('SiteMonitor-win-x64', 'zip', '.', 'SiteMonitor')")
ls -la "$OUT" dist/SiteMonitor-win-x64.zip
