#!/usr/bin/env bash
# Builds the plugin, zips it the way Jellyfin's plugin catalogue expects, and refreshes manifest.json.
# Usage: ./build-release.sh [version] [download-base-url]
set -euo pipefail
cd "$(dirname "$0")"
PROJECT=Jellyfin.Plugin.Laurels; GUID=9c1f6c3a-2b7e-4d5f-9a1e-6f0b2c4d8e71; NAME=Laurels; DESC="Ratings from many services (incl. Filmweb) and film awards."; CATEGORY="Metadata"; ABI=12.0.0.0; TFM=net10.0; SLUG=laurels
SDK=/opt/homebrew/opt/dotnet
if [ -d "$SDK/libexec" ]; then export DOTNET_ROOT="$SDK/libexec"; export PATH="$SDK/bin:$PATH"; fi
VERSION="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' $PROJECT/$PROJECT.csproj | head -1)}"
BASE_URL="${2:-https://github.com/theqkash/jellyfin-plugin-laurels/releases/download}"
OUT=artifacts; ZIP="$OUT/${SLUG}_$VERSION.zip"
dotnet build "$PROJECT/$PROJECT.csproj" -c Release -p:Version="$VERSION" -p:AssemblyVersion="$VERSION" -p:FileVersion="$VERSION" >/dev/null
rm -rf "$OUT/stage" && mkdir -p "$OUT/stage"
cp "$PROJECT/bin/Release/$TFM/$PROJECT.dll" "$OUT/stage/"
cat > "$OUT/stage/meta.json" <<JSON
{
  "category": "$CATEGORY",
  "changelog": "",
  "description": "$DESC",
  "guid": "$GUID",
  "name": "$NAME",
  "overview": "$DESC",
  "owner": "theqkash",
  "targetAbi": "$ABI",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "version": "$VERSION",
  "status": "Active",
  "autoUpdate": true,
  "imagePath": ""
}
JSON
rm -f "$ZIP" && (cd "$OUT/stage" && zip -q -r "../${SLUG}_$VERSION.zip" .)
MD5=$(md5 -q "$ZIP" 2>/dev/null || md5sum "$ZIP" | cut -d' ' -f1)
python3 - "$VERSION" "$MD5" "$BASE_URL/v$VERSION/${SLUG}_$VERSION.zip" "$GUID" "$ABI" "$NAME" "$DESC" "$CATEGORY" <<'PY'
import json, sys, datetime, os
version, md5, url, guid, abi, name, desc, category = sys.argv[1:9]
path = 'manifest.json'
manifest = json.load(open(path)) if os.path.exists(path) else []
entry = next((m for m in manifest if m['guid'] == guid), None)
if entry is None:
    entry = {"guid": guid, "name": name, "description": desc, "overview": desc, "owner": "theqkash", "category": category, "imageUrl": "", "versions": []}
    manifest.append(entry)
ver = {"version": version, "changelog": "", "targetAbi": abi, "sourceUrl": url, "checksum": md5, "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')}
entry["versions"] = [v for v in entry["versions"] if v["version"] != version]
entry["versions"].insert(0, ver)
json.dump(manifest, open(path, 'w'), indent=2); open(path, 'a').write('\n')
PY
echo "built $ZIP (md5 $MD5); manifest.json updated"
