#!/usr/bin/env bash
set -euo pipefail

if [[ $# -gt 1 ]]; then
    printf 'Usage: bash Tools/ValidateUnity.sh [project-directory]\n' >&2
    exit 2
fi

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
project_dir="${1:-$repo_root}"
for folder in Assets Packages ProjectSettings; do
    if [[ ! -d "$project_dir/$folder" ]]; then
        printf 'Missing Unity project directory: %s/%s\n' "$project_dir" "$folder" >&2
        exit 2
    fi
done

editor_version="$(awk '$1 == "m_EditorVersion:" {print $2; exit}' "$project_dir/ProjectSettings/ProjectVersion.txt")"
unity_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$editor_version/Unity.app/Contents/MacOS/Unity}"
if [[ ! -x "$unity_editor" ]]; then
    printf 'Set UNITY_EDITOR to the executable for Unity %s. Checked %s\n' "$editor_version" "$unity_editor" >&2
    exit 2
fi

temp_dir="$(mktemp -d "${TMPDIR:-/tmp}/eindwerk-unity.XXXXXX")"
trap 'rm -rf -- "$temp_dir"' EXIT
log_file="$(mktemp "${TMPDIR:-/tmp}/eindwerk-unity-log.XXXXXX")"
cp -R "$project_dir/Assets" "$project_dir/Packages" "$project_dir/ProjectSettings" "$temp_dir"
printf 'Unity validation log: %s\n' "$log_file"

if "$unity_editor" -batchmode -nographics -quit -projectPath "$temp_dir" \
    -executeMethod Eindwerk.PlayerMechanics.Editor.SurfMovementValidation.ValidateAllMovement \
    -logFile "$log_file" && grep -q '^ALL_MOVEMENT_VALIDATION_PASSED$' "$log_file"; then
    printf 'Unity compilation and movement validation passed.\n'
else
    printf 'Unity validation failed. See %s\n' "$log_file" >&2
    grep -m 10 -E 'error CS[0-9]+:|Exception:|[Ff]ailed to' "$log_file" >&2 || true
    exit 1
fi
