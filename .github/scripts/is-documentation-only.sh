#!/usr/bin/env bash
# Only Markdown changes can skip application CI. Include both sides of renames.
set -euo pipefail

changed_paths=$(mktemp)
trap 'rm -f "$changed_paths"' EXIT
git diff --name-only --no-renames -z "${1:?base revision required}" "${2:?head revision required}" > "$changed_paths"

docs_only=true
while IFS= read -r -d '' changed_path; do
  case "$changed_path" in
    *.md) ;;
    *) docs_only=false; break ;;
  esac
done < "$changed_paths"
printf '%s\n' "$docs_only"
