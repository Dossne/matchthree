#!/usr/bin/env bash
set -euo pipefail

# Scan tracked text files for unresolved merge conflict markers.
# Excludes generated/build folders and common binary extensions.

readonly binary_ext_regex='\.(png|jpg|jpeg|webp|gif|wav|mp3|mp4|dll|exe|unitypackage)$'
readonly marker_regex='^(<{7}|={7}|>{7})'

found=0

while IFS= read -r -d '' file; do
  case "$file" in
    .git/*|Library/*|Temp/*|Logs/*|obj/*|Build/*)
      continue
      ;;
  esac

  if [[ "$file" =~ $binary_ext_regex ]]; then
    continue
  fi

  [[ -f "$file" ]] || continue

  if grep -nHE -I "$marker_regex" "$file"; then
    found=1
  fi
done < <(git ls-files -z)

if [[ $found -ne 0 ]]; then
  echo
  echo "ERROR: Merge conflict markers were found. Resolve all markers listed above." >&2
  exit 1
fi

echo "No merge conflict markers found."
