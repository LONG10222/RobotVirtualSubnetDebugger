#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."

runtime=${1:-}
case "$runtime" in osx-arm64|osx-x64) ;; *) echo "Usage: $0 osx-arm64|osx-x64" >&2; exit 2;; esac
project=RobotNet.Mac/RobotNet.Mac.csproj
version=$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$project")
out="$PWD/artifacts/macos"
bundle="$out/$runtime/RobotNet.app"
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
mkdir -p "$out/$runtime" "$stage/RobotNet.app/Contents/MacOS"
app="$stage/RobotNet.app"
"${DOTNET_BIN:-dotnet}" publish "$project" -c Release -r "$runtime" --self-contained true -o "$app/Contents/MacOS"
cp RobotNet.Mac/Info.plist "$app/Contents/Info.plist"
mkdir -p "$app/Contents/Resources"
cp RobotNet.Mac/Assets/RobotNet.icns "$app/Contents/Resources/RobotNet.icns"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" -c "Set :CFBundleVersion $version" "$app/Contents/Info.plist"
/usr/bin/codesign --force --sign "${ROBOTNET_MACOS_SIGN_IDENTITY:--}" --deep "$app"
/usr/bin/codesign --verify --deep --strict "$app"
archive="RobotNet-${version}-${runtime}.zip"
/usr/bin/ditto -c -k --keepParent "$app" "$stage/$archive"
mkdir -p "$bundle"
/usr/bin/ditto "$app" "$bundle"
mv -f "$stage/$archive" "$out/$archive"
(cd "$out" && shasum -a 256 "$archive" > "$archive.sha256")
printf '%s\n' "$out/$archive"
