#!/usr/bin/env bash
# Run from the repository root. The same staging path is used by CI and local validation.
set -euo pipefail

PUBLISH_DIR=${1:?Usage: prepare-appdir.sh PUBLISH_DIR APPDIR VERSION SOURCE_REVISION [RELEASE_DATE]}
APPDIR=${2:?Missing AppDir destination}
VERSION=${3:?Missing release version}
SOURCE_REVISION=${4:?Missing source revision}
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Invalid release version" >&2; exit 1; }
[[ "$SOURCE_REVISION" =~ ^[0-9a-f]{40}$ ]] || { echo "Expected a full source commit SHA" >&2; exit 1; }
RELEASE_DATE=${5:-$(git show -s --format=%cs "$SOURCE_REVISION")}
[[ "$RELEASE_DATE" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || { echo "Invalid release date" >&2; exit 1; }
test -x "$PUBLISH_DIR/PKHeX.Avalonia"
# Refuse to overlay an old AppDir: stale payload files must never enter a release.
test ! -e "$APPDIR"

mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" \
  "$APPDIR/usr/share/metainfo" "$APPDIR/usr/share/icons/hicolor/64x64/apps"
cp -a "$PUBLISH_DIR/." "$APPDIR/usr/bin/"
cp packaging/linux/io.pkhexdeck.app.desktop "$APPDIR/"
cp packaging/linux/io.pkhexdeck.app.desktop "$APPDIR/usr/share/applications/"
cp PKHeX.Avalonia/Assets/Icons/icon.png "$APPDIR/io.pkhexdeck.app.png"
cp "$APPDIR/io.pkhexdeck.app.png" "$APPDIR/usr/share/icons/hicolor/64x64/apps/"
ln -s io.pkhexdeck.app.png "$APPDIR/.DirIcon"
sed -e "s/@SOURCE_REVISION@/$SOURCE_REVISION/g" \
    -e "s/@VERSION@/$VERSION/g" -e "s/@RELEASE_DATE@/$RELEASE_DATE/g" \
  packaging/linux/io.pkhexdeck.app.metainfo.xml \
  > "$APPDIR/usr/share/metainfo/io.pkhexdeck.app.appdata.xml"

cat > "$APPDIR/AppRun" << 'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/PKHeX.Avalonia" "$@"
EOF
chmod +x "$APPDIR/AppRun"

desktop-file-validate "$APPDIR/io.pkhexdeck.app.desktop"
appstreamcli validate --no-net "$APPDIR/usr/share/metainfo/io.pkhexdeck.app.appdata.xml"
