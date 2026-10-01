#!/usr/bin/env bash
# Hook PostToolUse de Claude Code (HU-003): si el archivo editado está en content/, ejecuta el validador.
# Lee el JSON del evento por stdin (tool_input.file_path) y no bloquea si el validador no compila.
set -u
input="$(cat)"
file="$(printf '%s' "$input" | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1)"
case "$file" in
  *content/*.json|*content\\*.json)
    root="$(cd "$(dirname "$0")/../.." && pwd)"
    dotnet run --project "$root/server/tools/ContentValidator" -- "$root/content" 2>&1 | tail -20
    ;;
esac
exit 0
