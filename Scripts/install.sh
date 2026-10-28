#!/usr/bin/env bash
# PKHeX-Avalonia — Linux installer.
#
# Downloads a PKHeX-Avalonia AppImage release and registers it in the application menu.
# Everything is installed into the user's own XDG directories (~/.local/...) without root.
# A supported x86_64 Linux distribution is required. The AppImage is installed under the
# stable name PKHeX-Avalonia.AppImage, which is also the path the in-app self-updater swaps in
# place — the menu entry keeps working across updates.
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/realgarit/PKHeX-Avalonia/main/Scripts/install.sh | bash
#   curl -fsSL https://raw.githubusercontent.com/realgarit/PKHeX-Avalonia/main/Scripts/install.sh | bash -s -- --uninstall
#
# Options:
#   --version <x.y.z>   install a specific release instead of the latest
#   --uninstall         remove the installed app, menu entry, and icon (keeps save files and app data)
#   -h, --help          show this help
#
# Colors are automatic when stdout is a terminal; set NO_COLOR=1 to force plain output.
set -euo pipefail

REPO="realgarit/PKHeX-Avalonia"
CANONICAL_NAME="PKHeX-Avalonia.AppImage"
DESKTOP_ID="io.pkhex.avalonia"

# --- output helpers --------------------------------------------------------------------
# Plain output when stdout is not a TTY (piped to a log), NO_COLOR is set, or TERM is dumb.
# Palette matches the app's own branding: the icon's Poké Ball red (~#CD1818 → 256-color 160),
# the white of the app text, and the CompactAccentBrush rose (#B05763 → 256-color 131).
# LIVE_UI additionally gates carriage-return redraws (progress bar) for the same conditions.
if [ -t 1 ] && [ -z "${NO_COLOR:-}" ] && [ "${TERM:-}" != "dumb" ]; then
    C_RESET=$'\033[0m'
    C_BOLD=$'\033[1m'
    C_DIM=$'\033[2m'
    C_RED=$'\033[1;38;5;160m'      # brand red (icon Poké Ball red)
    C_WHITE=$'\033[1;97m'          # bold bright white (app foreground)
    C_ACCENT=$'\033[38;5;131m'     # app accent rose (CompactAccentBrush)
    C_OK=$'\033[38;5;114m'
    C_WARN=$'\033[38;5;215m'
    C_ERR=$'\033[1;38;5;203m'
    LIVE_UI=1
else
    C_RESET=""; C_BOLD=""; C_DIM=""; C_RED=""; C_WHITE=""
    C_ACCENT=""; C_OK=""; C_WARN=""; C_ERR=""
    LIVE_UI=0
fi

info() { printf '%s\n' "${C_ACCENT}  ▶${C_RESET} $*"; }
ok()   { printf '%s\n' "${C_OK}  ✓${C_RESET} $*"; }
warn() { printf '%s\n' "${C_WARN}  !${C_RESET} $*" >&2; }
die()  { printf '%s\n' "${C_ERR}  ✗ $*${C_RESET}" >&2; exit 1; }

fmt_mb() { awk -v b="$1" 'BEGIN{printf "%.1f", b/1048576}'; }

BAR_WIDTH=26
# draw_bar <downloaded-bytes> <total-bytes>; total 0 = unknown size (counter only)
draw_bar() {
    local done_b="$1" total_b="$2" pct filled i bar=""
    if [ "$total_b" -gt 0 ]; then
        pct=$(( done_b * 100 / total_b )); [ "$pct" -gt 100 ] && pct=100
        filled=$(( BAR_WIDTH * done_b / total_b )); [ "$filled" -gt "$BAR_WIDTH" ] && filled=$BAR_WIDTH
        for ((i = 0; i < filled; i++)); do bar+="█"; done
        for ((i = filled; i < BAR_WIDTH; i++)); do bar+="░"; done
        printf '\r%s' "  ${C_RED}${bar}${C_RESET} ${C_WHITE}${pct}%${C_RESET} ${C_DIM}$(fmt_mb "$done_b") / $(fmt_mb "$total_b") MB${C_RESET}"
    else
        printf '\r%s' "  ${C_WHITE}$(fmt_mb "$done_b") MB${C_RESET} ${C_DIM}downloaded${C_RESET}"
    fi
}

print_banner() {
    printf '%s' "${C_RED}"
    cat <<'BANNER_PKH'
           ██████  ██   ██ ██   ██ ███████ ██   ██
           ██   ██ ██  ██  ██   ██ ██       ██ ██
           ██████  █████   ███████ █████     ███
           ██      ██  ██  ██   ██ ██       ██ ██
           ██      ██   ██ ██   ██ ███████ ██   ██
BANNER_PKH
    printf '%s' "${C_RESET}${C_WHITE}"
    cat <<'BANNER_AVALONIA'

 █████  ██    ██  █████  ██       ██████  ███    ██ ██  █████
██   ██ ██    ██ ██   ██ ██      ██    ██ ████   ██ ██ ██   ██
███████ ██    ██ ███████ ██      ██    ██ ██ ██  ██ ██ ███████
██   ██  ██  ██  ██   ██ ██      ██    ██ ██  ██ ██ ██ ██   ██
██   ██   ████   ██   ██ ███████  ██████  ██   ████ ██ ██   ██
BANNER_AVALONIA
    printf '%s\n' "${C_RESET}"
    printf '%s\n' "${C_DIM}    Pokémon save file editor for Linux. User-local install, no root required.${C_RESET}"
    printf '\n'
}

print_summary() {
    local version="$1"
    printf '\n'
    printf '%s\n' "${C_WHITE}  PKHeX-Avalonia ${version} installed${C_RESET}"
    printf '%s\n' "${C_DIM}  ─────────────────────────────────────────────${C_RESET}"
    printf '%s\n' "    ${C_BOLD}App        ${C_RESET}${INSTALL_PATH}"
    printf '%s\n' "    ${C_BOLD}Menu entry ${C_RESET}${DESKTOP_FILE}"
    printf '%s\n' ""
    printf '%s\n' "  Launch it from your application menu, or run the AppImage directly."
    printf '%s\n' "  The in-app updater keeps this copy updated in place."
    printf '%s\n' "  ${C_DIM}Uninstall any time: re-run this script with${C_RESET} ${C_ACCENT}--uninstall${C_RESET}"
}

VERSION=""
UNINSTALL=false
while [ $# -gt 0 ]; do
    case "$1" in
        --version)
            [ $# -ge 2 ] || { echo "--version needs a value (e.g. --version 1.87.4)" >&2; exit 2; }
            VERSION="${2#v}"
            shift 2
            ;;
        --uninstall) UNINSTALL=true; shift ;;
        -h|--help)
            cat <<'HELP'
PKHeX-Avalonia Linux installer — installs the AppImage release into your
user directories (~/.local/...) and adds it to the application menu.

Usage:
  curl -fsSL https://raw.githubusercontent.com/realgarit/PKHeX-Avalonia/main/Scripts/install.sh | bash
  curl -fsSL https://raw.githubusercontent.com/realgarit/PKHeX-Avalonia/main/Scripts/install.sh | bash -s -- --uninstall

Options:
  --version <x.y.z>   install a specific release instead of the latest
  --uninstall         remove the installed app, menu entry, and icon
                      (save files and app data under ~/.local/share/PKHeX-Avalonia are kept)
  -h, --help          show this help

Set NO_COLOR=1 to disable the colored output.
HELP
            exit 0
            ;;
        *) echo "Unknown option: $1 (try --help)" >&2; exit 2 ;;
    esac
done

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
[[ "$DATA_HOME" = /* ]] || DATA_HOME="$HOME/.local/share"
OPT_DIR="$HOME/.local/opt/PKHeX-Avalonia"
APPS_DIR="$DATA_HOME/applications"
ICON_DIR="$DATA_HOME/icons/hicolor/64x64/apps"
INSTALL_PATH="$OPT_DIR/$CANONICAL_NAME"
DESKTOP_FILE="$APPS_DIR/$DESKTOP_ID.desktop"
ICON_FILE="$ICON_DIR/$DESKTOP_ID.png"

refresh_databases() {
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -f -t "$DATA_HOME/icons/hicolor" >/dev/null 2>&1 || true
    fi
}

if $UNINSTALL; then
    print_banner
    info "Uninstalling PKHeX-Avalonia (user-local files only)…"
    REMOVED=0
    for target in "$DESKTOP_FILE" "$INSTALL_PATH" "$ICON_FILE"; do
        if [ -e "$target" ] || [ -L "$target" ]; then
            rm -f -- "$target"
            ok "Removed $(basename "$target")"
            REMOVED=$((REMOVED + 1))
        fi
    done
    rmdir "$OPT_DIR" 2>/dev/null || true
    if [ "$REMOVED" -eq 0 ]; then
        ok "Nothing to uninstall. PKHeX-Avalonia is not installed for this user."
        exit 0
    fi
    refresh_databases
    printf '%s\n' ""
    ok "PKHeX-Avalonia removed from the application menu."
    printf '%s\n' "  Your save files and app data (${DATA_HOME}/PKHeX-Avalonia) are untouched."
    exit 0
fi

[ "$(uname -s)" = Linux ] || die "This installer requires Linux."
[ "$(uname -m)" = x86_64 ] || die "Both Linux release packages require x86_64; ARM is not supported."
for dependency in curl python3 sha256sum; do
    command -v "$dependency" >/dev/null 2>&1 || die "$dependency is required. Install it with your distribution's package manager."
done
[[ "$VERSION" = "" || "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "Use a release version such as 1.88.0."
[[ "$INSTALL_PATH" != *'='* ]] || die "Desktop entry executable paths cannot contain '='."
[ ! -d "$INSTALL_PATH" ] && [ ! -L "$INSTALL_PATH" ] || die "The install path is a directory or symlink; leaving it untouched."

print_banner
info "Resolving release…"

# --- resolve the release ---------------------------------------------------------------
RELEASE_JSON="$(mktemp)"
trap 'rm -f "$RELEASE_JSON"' EXIT

if [ -n "$VERSION" ]; then
    RELEASE_TAG="v$VERSION"
    if ! curl -fsSL --retry 3 "https://api.github.com/repos/$REPO/releases/tags/$RELEASE_TAG" -o "$RELEASE_JSON"; then
        die "Could not find release $RELEASE_TAG on $REPO."
    fi
    ok "Pinned release: ${C_BOLD}${RELEASE_TAG}${C_RESET}"
else
    if ! curl -fsSL --retry 3 "https://api.github.com/repos/$REPO/releases/latest" -o "$RELEASE_JSON"; then
        echo "Could not reach the GitHub API (offline, or rate-limited — retry in a bit," >&2
        echo "or pin a version with: install.sh --version 1.87.4)" >&2
        exit 1
    fi
fi

# Parse the selected asset as JSON; neither field order nor response formatting is an API contract.
if ! RELEASE_META=$(python3 - "$RELEASE_JSON" <<'PY'
import json, re, sys
with open(sys.argv[1], encoding="utf-8") as stream:
    release = json.load(stream)
tag = release["tag_name"]
if not re.fullmatch(r"v[0-9]+\.[0-9]+\.[0-9]+", tag):
    raise SystemExit("Unsupported release tag")
name = f"PKHeX-Avalonia-{tag[1:]}-x86_64.AppImage"
assets = [asset for asset in release["assets"] if asset["name"] == name]
if len(assets) != 1:
    raise SystemExit("Release does not contain exactly one matching AppImage")
asset = assets[0]
digest = asset.get("digest") or ""
if not re.fullmatch(r"sha256:[0-9a-f]{64}", digest):
    raise SystemExit("Release has no valid SHA-256 digest; refusing an unverified install")
print(tag)
print(digest[7:])
print(int(asset["size"]))
PY
); then die "Could not resolve a verified AppImage from the release metadata."; fi
mapfile -t RELEASE_FIELDS <<< "$RELEASE_META"
RELEASE_TAG="${RELEASE_FIELDS[0]}"
VERSION="${RELEASE_TAG#v}"
DIGEST="${RELEASE_FIELDS[1]}"
TOTAL_BYTES="${RELEASE_FIELDS[2]}"
ok "Release: ${C_BOLD}${RELEASE_TAG}${C_RESET}"

ASSET="PKHeX-Avalonia-${VERSION}-x86_64.AppImage"
DOWNLOAD_URL="https://github.com/$REPO/releases/download/v${VERSION}/${ASSET}"

# --- download + verify -----------------------------------------------------------------
STAGING="$(mktemp --suffix=.AppImage 2>/dev/null || mktemp)"
trap 'rm -f "$RELEASE_JSON" "$STAGING"' EXIT

if [ "$TOTAL_BYTES" -gt 0 ]; then
    info "Downloading $ASSET ($(fmt_mb "$TOTAL_BYTES") MB)…"
else
    info "Downloading $ASSET …"
fi

# curl runs silently in the background while the script draws its own themed
# progress bar; curl's default meter table is noisy and off-brand.
curl -sS -fL --retry 3 -o "$STAGING" "$DOWNLOAD_URL" & CURL_PID=$!
if [ "$LIVE_UI" -eq 1 ]; then
    while kill -0 "$CURL_PID" 2>/dev/null; do
        draw_bar "$(wc -c < "$STAGING" 2>/dev/null || echo 0)" "$TOTAL_BYTES"
        sleep 0.2
    done
    draw_bar "$(wc -c < "$STAGING" 2>/dev/null || echo 0)" "$TOTAL_BYTES"
    printf '\n'
fi
if ! wait "$CURL_PID"; then
    die "Download failed. Check your connection and try again (or pin a release with --version)."
fi

info "Verifying SHA-256 checksum…"
if [ "$(sha256sum < "$STAGING" | cut -d ' ' -f 1)" = "$DIGEST" ]; then
    ok "Checksum verified (SHA-256 matches the published digest)"
else
    die "Checksum mismatch — the download is corrupt or was tampered with. Aborting."
fi

# --- install ---------------------------------------------------------------------------
info "Installing to $OPT_DIR …"
mkdir -p "$OPT_DIR" "$APPS_DIR" "$ICON_DIR"
# Prepare on the destination filesystem before the atomic replacement. A failed copy/chmod
# leaves an existing installation intact, including when /tmp is a different filesystem.
INSTALL_STAGE="$(mktemp "$OPT_DIR/.install.XXXXXX")"
trap 'rm -f -- "$RELEASE_JSON" "$STAGING" "$INSTALL_STAGE"' EXIT
cp -- "$STAGING" "$INSTALL_STAGE"
chmod 755 "$INSTALL_STAGE"
mv -fT -- "$INSTALL_STAGE" "$INSTALL_PATH"

# The icon ships in the repository next to the app; prefer the tagged copy, fall back to main.
ICON_URL_BASE="https://raw.githubusercontent.com/$REPO"
ICON_STAGE="$(mktemp "$ICON_DIR/.pkhex-icon.XXXXXX")"
trap 'rm -f -- "$RELEASE_JSON" "$STAGING" "$INSTALL_STAGE" "$ICON_STAGE"' EXIT
if curl -fsSL --retry 2 -o "$ICON_STAGE" "$ICON_URL_BASE/v${VERSION}/PKHeX.Avalonia/Assets/Icons/icon.png" ||
   curl -fsSL --retry 2 -o "$ICON_STAGE" "$ICON_URL_BASE/main/PKHeX.Avalonia/Assets/Icons/icon.png"; then
    chmod 644 "$ICON_STAGE"
    mv -fT -- "$ICON_STAGE" "$ICON_FILE"
else
    warn "Could not download the icon; any existing icon is unchanged."
fi

# Apply Exec quoting first, then the desktop-entry string escaping layer.
desktop_value() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//$'\n'/\\n}"
    value="${value//$'\r'/\\r}"
    value="${value//$'\t'/\\t}"
    printf '%s' "$value"
}
EXEC_ARGUMENT="$INSTALL_PATH"
EXEC_ARGUMENT="${EXEC_ARGUMENT//\\/\\\\}"
EXEC_ARGUMENT="${EXEC_ARGUMENT//\"/\\\"}"
EXEC_ARGUMENT="${EXEC_ARGUMENT//\$/\\\$}"
EXEC_ARGUMENT="${EXEC_ARGUMENT//\`/\\\`}"
EXEC_ARGUMENT="${EXEC_ARGUMENT//%/%%}"
ESCAPED_EXEC="$(desktop_value "\"$EXEC_ARGUMENT\"")"
ESCAPED_TRYEXEC="$(desktop_value "$INSTALL_PATH")"
DESKTOP_STAGE="$(mktemp "$APPS_DIR/.pkhex-desktop.XXXXXX")"
trap 'rm -f -- "$RELEASE_JSON" "$STAGING" "$INSTALL_STAGE" "$ICON_STAGE" "$DESKTOP_STAGE"' EXIT
cat > "$DESKTOP_STAGE" <<EOF
[Desktop Entry]
Type=Application
Name=PKHeX-Avalonia
Comment=Pokémon save file editor
Exec=/usr/bin/env -- $ESCAPED_EXEC
TryExec=$ESCAPED_TRYEXEC
Icon=$DESKTOP_ID
Categories=Utility;
Terminal=false
StartupWMClass=PKHeX.Avalonia
EOF
chmod 644 "$DESKTOP_STAGE"
mv -fT -- "$DESKTOP_STAGE" "$DESKTOP_FILE"

refresh_databases
ok "Menu entry registered (${DESKTOP_ID})"

print_summary "$VERSION"
