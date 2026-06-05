#!/usr/bin/env bash
# Install the Code-First Agent Starter skills + agent into Copilot and/or Claude.
#
# Usage:
#   ./install.sh                         # both tools, user scope
#   ./install.sh --tool copilot          # copilot only
#   ./install.sh --tool claude           # claude only
#   ./install.sh --scope project         # symlink into ./.github and ./.claude of cwd
#   ./install.sh --force                 # replace existing entries

set -euo pipefail

TOOL="both"
SCOPE="user"
FORCE="0"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --tool)  TOOL="$2"; shift 2 ;;
        --scope) SCOPE="$2"; shift 2 ;;
        --force) FORCE="1"; shift ;;
        -h|--help)
            grep '^#' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done

REPO_ROOT="$( cd "$( dirname "${BASH_SOURCE[0]}" )/.." && pwd )"
SKILLS_SRC="$REPO_ROOT/skills"
AGENTS_SRC="$REPO_ROOT/agents"

target_roots() {
    local tool="$1" scope="$2"
    local base
    if [[ "$scope" == "user" ]]; then base="$HOME"; else base="$PWD"; fi
    case "$tool" in
        copilot)
            if [[ "$scope" == "user" ]]; then
                echo "$base/.personalcopilot/skills|$base/.personalcopilot/agents|.agent.md"
            else
                echo "$base/.github/skills|$base/.github/agents|.agent.md"
            fi ;;
        claude)
            echo "$base/.claude/skills|$base/.claude/agents|.md" ;;
        *) echo "Unknown tool: $tool" >&2; exit 2 ;;
    esac
}

install_link() {
    local src="$1" tgt="$2"
    if [[ -e "$tgt" || -L "$tgt" ]]; then
        if [[ "$FORCE" != "1" ]]; then
            echo "  skip (exists): $tgt"
            return
        fi
        rm -rf "$tgt"
    fi
    mkdir -p "$(dirname "$tgt")"
    ln -s "$src" "$tgt"
    echo "  linked: $tgt"
}

install_for_tool() {
    local tool="$1" scope="$2"
    echo "==> Installing for $tool ($scope scope)"
    IFS='|' read -r skills_dir agents_dir agent_suffix <<<"$(target_roots "$tool" "$scope")"

    for d in "$SKILLS_SRC"/*/; do
        local name; name="$(basename "$d")"
        if [[ ! -f "$d/SKILL.md" ]]; then
            echo "  warn: $name has no SKILL.md, skipping" >&2
            continue
        fi
        install_link "${d%/}" "$skills_dir/$name"
    done

    if [[ -d "$AGENTS_SRC" ]]; then
        for f in "$AGENTS_SRC"/*.md; do
            [[ -e "$f" ]] || continue
            local base; base="$(basename "$f" .md)"
            base="${base%.agent}"
            install_link "$f" "$agents_dir/${base}${agent_suffix}"
        done
    fi
}

if [[ "$TOOL" == "both" ]]; then
    install_for_tool copilot "$SCOPE"
    install_for_tool claude  "$SCOPE"
else
    install_for_tool "$TOOL" "$SCOPE"
fi

echo
echo "Done. Open a fresh chat to pick up the new skills/agents."
