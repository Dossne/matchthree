#!/usr/bin/env bash
set -euo pipefail

# Scans tracked text files for unresolved merge conflict markers.
# Excludes generated/build folders and common binary file extensions.

binary_ext_regex='\.(png|jpg|jpeg|gif|bmp|tif|tiff|webp|ico|psd|ai|mp3|wav|ogg|flac|aac|mp4|mov|avi|mkv|webm|unitypackage|dll|so|dylib|exe|bin|zip|gz|7z|rar|pdf|ttf|otf|woff|woff2|eot|assetbundle)$'
marker_regex='^(<<<<<<<|=======|>>>>>>>)'

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

  if [[ ! -f "$file" ]]; then
    continue
  fi

  if grep -nE -I "$marker_regex" "$file" > /tmp/conflict_markers_match.txt; then
    while IFS= read -r line; do
      echo "$file:$line"
    done < /tmp/conflict_markers_match.txt
    found=1
  fi
done < <(git ls-files -z)

if [[ $found -ne 0 ]]; then
  echo
  echo "ERROR: Merge conflict markers were found. Resolve all markers above." >&2
  exit 1
fi

echo "No merge conflict markers found."
