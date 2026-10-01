#!/usr/bin/env bash
# Slice workflow helper (stage A). One GitHub issue per slice; its body is the approved brief.
# Run from anywhere inside the repository, with Git Bash on Windows or bash on Linux.
#
#   tools/slice.sh status                 slice issues by state, and slice worktrees
#   tools/slice.sh start <issue> [--dry-run]
#                                         worktree + branch from origin/main, brief copied in,
#                                         label ready -> in-progress, prints the run command
#   tools/slice.sh done <issue>           label in-progress -> needs-review, prints review inputs
#   tools/slice.sh rework <issue>         copies the follow-up brief in, label -> in-progress,
#                                         prints the run command
#   tools/slice.sh pr <issue>             pushes the branch and opens the pull request from
#                                         artifacts/trials/<slice>/pr-body.md
#   tools/slice.sh cleanup <issue>        removes the worktree and the local branch after merge
#
# Conventions: issue title "<slice id>: <title>" (for example "14-2: semantic validation"),
# body contains "Part of #<parent>", one routing label impl/opencode or impl/sonnet.
# Local, git-ignored inputs live in the main checkout: artifacts/trials/<slice id>/.
set -euo pipefail

REPO="${AIAKOS_REPO:-aiakos-hq/aiakos}"
OPENCODE_MODEL="${AIAKOS_OPENCODE_MODEL:-opencode-go/gpt-6-luna}"
SONNET_MODEL="${AIAKOS_SONNET_MODEL:-claude-sonnet-5-5}"
ROUTES="impl/opencode impl/sonnet"
STATES="ready in-progress needs-review blocked"

die() { echo "slice: $*" >&2; exit 1; }

main_root="$(git worktree list --porcelain | head -n 1 | sed 's/^worktree //')"
[ -n "$main_root" ] || die "not inside a git repository"

native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

# First four words of the title, lower-case, joined with '-'.
slug_of() { printf '%s' "$1" | tr '[:upper:]' '[:lower:]' | sed -E 's/[^a-z0-9]+/-/g; s/^-+//; s/-+$//' | cut -d- -f1-4; }

# Sets: number title body labels slice slug branch worktree trial parent route
load_issue() {
  number="${1:-}"
  [[ "$number" =~ ^[0-9]+$ ]] || die "give the slice issue number"
  local state
  state="$(gh issue view "$number" --repo "$REPO" --json state --jq .state)"
  [ "$state" = "OPEN" ] || die "issue #$number is $state"
  title="$(gh issue view "$number" --repo "$REPO" --json title --jq .title)"
  body="$(gh issue view "$number" --repo "$REPO" --json body --jq .body | tr -d '\r')"
  labels="$(gh issue view "$number" --repo "$REPO" --json labels --jq '[.labels[].name] | join(" ")')"

  [[ "$title" =~ ^([0-9]+-[0-9]+[a-z]?):[[:space:]]*(.+)$ ]] \
    || die "title of #$number must be '<slice id>: <title>', found '$title'"
  slice="${BASH_REMATCH[1]}"
  slug="$(slug_of "${BASH_REMATCH[2]}")"
  branch="feat/$slice-$slug"
  worktree="$main_root/.claude/worktrees/slice-$slice"
  trial="$main_root/artifacts/trials/$slice"

  parent="$(printf '%s' "$body" | grep -oE 'Part of #[0-9]+' | head -n 1 | grep -oE '[0-9]+' || true)"
  [ -n "$parent" ] || die "body of #$number must contain 'Part of #<parent issue>'"

  route=""
  local candidate
  for candidate in $ROUTES; do
    if has_label "$candidate"; then
      [ -z "$route" ] || die "#$number has more than one routing label"
      route="$candidate"
    fi
  done
  [ -n "$route" ] || die "#$number has no routing label (impl/opencode or impl/sonnet)"
}

has_label() { case " $labels " in *" $1 "*) return 0 ;; *) return 1 ;; esac; }

print_run_command() {
  local brief="$1" prompt
  prompt="Implement the brief at $brief. Follow it exactly. Run build and tests until green."
  echo
  echo "Run this yourself (Windows terminal), then report 'done':"
  echo
  if [ "$route" = "impl/opencode" ]; then
    echo "  opencode run -m $OPENCODE_MODEL --auto --dir \"$(native "$worktree")\" \"$prompt\""
  else
    echo "  cd \"$(native "$worktree")\""
    echo "  claude --model $SONNET_MODEL \"$prompt\""
  fi
  echo
  echo "Do not build or test in this worktree while the run is in progress."
}

cmd_status() {
  local route_label
  for route_label in $ROUTES; do
    gh issue list --repo "$REPO" --state open --label "$route_label" --limit 100 \
      --json number,title,labels \
      --jq ".[] | \"#\(.number)\t\([.labels[].name] | map(select(. == \"ready\" or . == \"in-progress\" or . == \"needs-review\" or . == \"blocked\")) | join(\",\") | if . == \"\" then \"waiting\" else . end)\t$route_label\t\(.title)\""
  done | sort -t "$(printf '\t')" -k2,2 -k1,1 | column -t -s "$(printf '\t')" || true
  echo
  echo "Slice worktrees:"
  git worktree list | grep -E '/slice-[0-9]+-[0-9]+[a-z]? ' || echo "  none"
}

cmd_start() {
  local dry_run=""
  load_issue "${1:-}"
  [ "${2:-}" = "--dry-run" ] && dry_run=1
  has_label ready || die "#$number is not labelled 'ready' (labels: $labels)"
  [ ! -e "$worktree" ] || die "worktree already exists: $worktree"
  if git show-ref --verify --quiet "refs/heads/$branch"; then die "branch already exists: $branch"; fi
  [ -f "$trial/review.md" ] \
    || die "review inputs missing: $trial/review.md (the scoring test and probes are written before the run)"

  echo "slice    $slice (#$number, part of #$parent, $route)"
  echo "branch   $branch"
  echo "worktree $(native "$worktree")"
  if [ -n "$dry_run" ]; then echo "dry run: nothing changed"; return; fi

  git fetch --quiet origin main
  git worktree add --quiet -b "$branch" "$worktree" origin/main
  mkdir -p "$worktree/artifacts/briefs"
  printf '%s\n' "$body" > "$worktree/artifacts/briefs/$slice.md"
  gh issue edit "$number" --repo "$REPO" --remove-label ready --add-label in-progress >/dev/null
  echo "brief    artifacts/briefs/$slice.md (git-ignored)"
  echo "label    in-progress"
  print_run_command "artifacts/briefs/$slice.md"
}

cmd_done() {
  load_issue "${1:-}"
  [ -d "$worktree" ] || die "no worktree for $slice: $worktree"
  gh issue edit "$number" --repo "$REPO" --remove-label in-progress --add-label needs-review >/dev/null
  echo "slice    $slice (#$number)"
  echo "label    needs-review"
  echo "Review inputs for the slice-reviewer agent:"
  echo "  slice     $slice"
  echo "  worktree  $(native "$worktree")"
  echo "  brief     $(native "$worktree/artifacts/briefs/$slice.md")"
  echo "  trial     $(native "$trial")"
}

cmd_rework() {
  load_issue "${1:-}"
  [ -d "$worktree" ] || die "no worktree for $slice: $worktree"
  local follow_up
  follow_up="$(ls -t "$main_root/artifacts/briefs/$slice"?-*.md 2>/dev/null | head -n 1 || true)"
  [ -n "$follow_up" ] || die "no follow-up brief found: artifacts/briefs/$slice<letter>-*.md"
  cp "$follow_up" "$worktree/artifacts/briefs/"
  gh issue edit "$number" --repo "$REPO" --remove-label needs-review --add-label in-progress >/dev/null
  echo "slice    $slice (#$number)"
  echo "label    in-progress"
  print_run_command "artifacts/briefs/$(basename "$follow_up")"
}

cmd_pr() {
  load_issue "${1:-}"
  [ -d "$worktree" ] || die "no worktree for $slice: $worktree"
  [ -f "$trial/pr-body.md" ] || die "missing $trial/pr-body.md (first line 'Title: ...', then the body)"
  [ -z "$(git -C "$worktree" status --porcelain)" ] || die "worktree has uncommitted changes"
  local pr_title body_file
  pr_title="$(head -n 1 "$trial/pr-body.md" | sed -E 's/^Title:[[:space:]]*//')"
  [ -n "$pr_title" ] || die "first line of pr-body.md must be 'Title: ...'"
  body_file="$(mktemp)"
  { tail -n +2 "$trial/pr-body.md"; printf '\nCloses #%s. Refs #%s.\n' "$number" "$parent"; } > "$body_file"
  git -C "$worktree" push --quiet -u origin "$branch"
  gh pr create --repo "$REPO" --base main --head "$branch" --title "$pr_title" --body-file "$body_file"
  rm -f "$body_file"
}

# Runs after the merge, when the issue is closed: derives the names without the open check.
cmd_cleanup() {
  local number="${1:-}" title slice slug branch worktree
  [[ "$number" =~ ^[0-9]+$ ]] || die "give the slice issue number"
  title="$(gh issue view "$number" --repo "$REPO" --json title --jq .title)"
  [[ "$title" =~ ^([0-9]+-[0-9]+[a-z]?):[[:space:]]*(.+)$ ]] || die "title of #$number is not a slice title"
  slice="${BASH_REMATCH[1]}"
  slug="$(slug_of "${BASH_REMATCH[2]}")"
  branch="feat/$slice-$slug"
  worktree="$main_root/.claude/worktrees/slice-$slice"
  if [ -d "$worktree" ]; then
    git worktree remove "$worktree" || die "worktree not removed (uncommitted changes?): $worktree"
    echo "removed worktree $(native "$worktree")"
  fi
  if git show-ref --verify --quiet "refs/heads/$branch"; then
    git branch -d "$branch" 2>/dev/null && echo "deleted branch $branch" \
      || echo "branch $branch kept: git does not see it as merged (squash merge?). Delete it yourself with: git branch -D $branch"
  fi
}

case "${1:-}" in
  status)  cmd_status ;;
  start)   cmd_start "${2:-}" "${3:-}" ;;
  done)    cmd_done "${2:-}" ;;
  rework)  cmd_rework "${2:-}" ;;
  pr)      cmd_pr "${2:-}" ;;
  cleanup) cmd_cleanup "${2:-}" ;;
  *)       sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'; exit 2 ;;
esac
