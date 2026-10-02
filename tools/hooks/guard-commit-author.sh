#!/usr/bin/env bash
# Hook PreToolUse de Claude Code: bloquea `git commit` si el commit saldría firmado por Claude o con sus trailers.
# Los commits los firman las personas del equipo; vale también en las sesiones de Claude Code en la web, que leen este
# .claude/settings.json. Lee el JSON del evento por stdin (tool_input.command). Código 2 = bloquear y explicar el motivo.
set -u
input="$(cat)"
command="$(printf '%s' "$input" | sed -n 's/.*"command"[[:space:]]*:[[:space:]]*"\(.*\)".*/\1/p' | head -1)"
case "$command" in
  *"git commit"*|*"git -c "*" commit"*) ;;
  *) exit 0 ;;
esac
# Identidad efectiva (tiene en cuenta GIT_AUTHOR_* / GIT_COMMITTER_* y git config).
author="$(git var GIT_AUTHOR_IDENT 2>/dev/null || true)"
committer="$(git var GIT_COMMITTER_IDENT 2>/dev/null || true)"
bad_identity="$(printf '%s\n%s' "$author" "$committer" | grep -iE 'claude|noreply@anthropic\.com' || true)"
bad_trailer="$(printf '%s' "$command" | grep -iE 'co-authored-by:[^"]*(claude|anthropic)|claude-session:|--author[= ][^ ]*claude' || true)"
if [ -n "$bad_identity$bad_trailer" ]; then
  {
    echo "Commit bloqueado: saldría firmado por Claude o con sus trailers."
    echo "Autor: ${author:-(sin configurar)}"
    echo "Configura tu identidad (git config user.name / user.email) y quita Co-Authored-By/Claude-Session del mensaje."
    echo "No uses --author ni -c user.* para firmar como Claude."
  } >&2
  exit 2
fi
exit 0
