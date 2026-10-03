#!/usr/bin/env bash
# Story workflow helper. See docs/workflow.md for the flow and its definitions.
# Run from anywhere inside the repository, with bash on Linux or Git Bash on Windows. The seats
# of the delivery rig (rigs/aiakos-delivery/) call it; it can also be run by hand.
#
# Analysis (files under docs/briefs/<slice>/ on a branch; nothing goes to GitHub):
#   tools/story.sh check <slice>          the brief is closed and the split is traceable
#   tools/story.sh split <slice>          prepares a directory for the split and says what to
#                                         do there
#   tools/story.sh split-done <slice>     checks the stories.md written there and copies it in
#   tools/story.sh show <slice> <n>       prints story <n> as an implementer will get it
#   tools/story.sh analysis <slice> [--remove]
#                                         a worktree and branch for the analysis of one slice
#
# Development (one GitHub sub-issue per story, created when the story is ready):
#   tools/story.sh status                 briefs, their stories and the state of each
#   tools/story.sh ready <slice> <n> [--dry-run]
#                                         checks the definition of ready, then creates the
#                                         sub-issue with the label "ready"
#   tools/story.sh baseline <slice> <n>   runs the story's gate against main in a temporary
#                                         worktree; it must fail there before "ready"
#   tools/story.sh next [<route>]         the ready story to take next
#   tools/story.sh start <issue>          worktree + branch from origin/main, story copied in,
#                                         label ready -> in-progress
#   tools/story.sh start <issue> --retry  sends a story back to its implementer: same worktree,
#                                         main merged in, story text and issue body written
#                                         again from the brief, label -> in-progress
#   tools/story.sh done <issue>           the gate: clean tree, allowed paths, build, acceptance
#                                         tests; label -> needs-review, or one retry, or blocked.
#                                         A run that stops at a process check before the build
#                                         is not an attempt
#   tools/story.sh pr <issue> [--maintainer-reviewed] [--partial]
#                                         pushes the branch and opens the pull request.
#                                         --partial: the gate did not pass because the brief
#                                         depends on something that does not exist; needs
#                                         artifacts/trials/<story>/partial.md, labels "partial"
#   tools/story.sh cleanup <issue>        removes the worktree and the local branch after merge
#
# A slice is one brief: docs/briefs/<slice>/ with brief.md, items.tsv, stories.md and, while it
# is reviewed, findings.md. A story is "<slice>-<n>" (for example 14-3-2); its issue title is
# "<story>: <title>". Acceptance tests are written before the run and stay local, in
# artifacts/trials/<story>/ of the main checkout (git-ignored): gate.sh runs them.
#
# "check", "split", "split-done" and "show" read the working tree, so they work on an analysis
# branch. "ready" and "status" read origin/main, because merging the analysis is its approval;
# set AIAKOS_BRIEF_REF=WORKTREE to read the working tree instead.
set -euo pipefail

REPO="${AIAKOS_REPO:-aiakos-hq/aiakos}"
BRIEF_REF="${AIAKOS_BRIEF_REF:-origin/main}"
# "impl" is the default implementer; "impl/senior" is the exception, for a story that cannot be
# split further. Which model is behind each is set in rigs/aiakos-delivery/rig.yaml.
ROUTES="impl impl/senior"
# Routes of briefs and issues from before the rig; both mean "impl".
LEGACY_ROUTES="impl/opencode impl/sonnet"

norm_route() { case " $LEGACY_ROUTES " in *" $1 "*) printf 'impl' ;; *) printf '%s' "$1" ;; esac; }

die() { echo "story: $*" >&2; exit 1; }

here="$(git rev-parse --show-toplevel 2>/dev/null)" || die "not inside a git repository"
main_root="$(git worktree list --porcelain | head -n 1 | sed 's/^worktree //')"
tools="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
slug_of() { printf '%s' "$1" | tr '[:upper:]' '[:lower:]' | sed -E 's/[^a-z0-9]+/-/g; s/^-+//; s/-+$//' | cut -d- -f1-4; }
is_slice() { [[ "$1" =~ ^[0-9]+-[0-9]+$ ]]; }

# A value from the brief's front matter.
front() { awk -v key="$2" 'NR == 1 && $0 !~ /^---/ { exit } NR > 1 && /^---/ { exit } index($0, key ":") == 1 { v = substr($0, length(key) + 2); sub(/[ \t]+#[^"]*$/, "", v); gsub(/^[ \t"]+|[ \t"\r]+$/, "", v); print v; exit }' "$1"; }

# Sets $dir to a directory that holds the slice's files, from the working tree or from $BRIEF_REF.
slice_dir() {
  local slice="$1" mode="${2:-tree}" file
  is_slice "$slice" || die "give a slice id such as 14-3, found '$slice'"
  if [ "$mode" = "tree" ] || [ "$BRIEF_REF" = "WORKTREE" ]; then
    dir="$here/docs/briefs/$slice"
    [ -f "$dir/brief.md" ] || die "no brief: docs/briefs/$slice/brief.md"
  else
    git fetch --quiet origin main 2>/dev/null || true
    dir="$(mktemp -d)"
    for file in brief.md items.tsv stories.md findings.md; do
      git show "$BRIEF_REF:docs/briefs/$slice/$file" > "$dir/$file" 2>/dev/null || rm -f "$dir/$file"
    done
    [ -f "$dir/brief.md" ] || die "docs/briefs/$slice/brief.md is not on $BRIEF_REF: the analysis is approved by merging it"
  fi
}

run_check() { bash "$tools/story-check.sh" "$1/items.tsv" "$1/stories.md" "$1/brief.md"; }

# A field of story S<n> in stories.md ("title" is the heading text).
story_field() {
  awk -v want="S$2" -v key="$3" '
    { sub(/\r$/, "") }
    /^## / { current = ""; if (match($0, /^## S[0-9]+: */)) { current = substr($0, 4, RLENGTH - 4); sub(/:.*/, "", current); if (current == want && key == "title") { print substr($0, RLENGTH + 1); exit } } next }
    current == want && index($0, key ":") == 1 { v = substr($0, length(key) + 2); gsub(/^[ \t]+|[ \t]+$/, "", v); print v; exit }
  ' "$1/stories.md"
}

# The rules and changes of the stories that story S<n> depends on, directly or not.
context_of() {
  awk -v want="S$2" '
    function trim(s) { gsub(/^[ \t\r]+|[ \t\r]+$/, "", s); return s }
    { sub(/\r$/, "") }
    /^## / { current = ""; if (match($0, /^## S[0-9]+: */)) { current = substr($0, 4, RLENGTH - 4); sub(/:.*/, "", current); order[++count] = current } next }
    current != "" && /^depends:/ { deps[current] = substr($0, 9) }
    current != "" && /^owns:/ { owns[current] = substr($0, 6) }
    END {
      need[want] = 1
      for (s = count; s >= 1; s--) {
        if (!(order[s] in need)) continue
        n = split(deps[order[s]], parts, ",")
        for (i = 1; i <= n; i++) if (trim(parts[i]) != "-" && trim(parts[i]) != "") need[trim(parts[i])] = 1
      }
      out = ""
      for (s = 1; s <= count; s++) {
        if (order[s] == want || !(order[s] in need)) continue
        n = split(owns[order[s]], parts, ",")
        for (i = 1; i <= n; i++) if (trim(parts[i]) != "-" && trim(parts[i]) != "") out = out (out == "" ? "" : ", ") trim(parts[i])
      }
      print out
    }
  ' "$1/stories.md"
}

# The story as an implementer gets it: the brief without the items of the other stories.
assemble() {
  local d="$1" slice="$2" n="$3" issue title goal depends notes context
  issue="$(front "$d/brief.md" issue)"
  title="$(story_field "$d" "$n" title)"
  [ -n "$title" ] || die "no story S$n in docs/briefs/$slice/stories.md"
  goal="$(story_field "$d" "$n" goal)"
  depends="$(story_field "$d" "$n" depends)"
  notes="$(story_field "$d" "$n" notes)"
  context="$(context_of "$d" "$n")"
  echo "Part of #$issue."
  echo
  echo "# Story $slice-$n: $title"
  echo
  echo "$goal"
  echo
  echo "This is one story of slice $slice. The text below the line is the slice's brief with the rules,"
  echo "changes, expected outputs and tests of the other stories removed. Implement only the items that"
  echo "are here; where IDs are missing from a list or a table, they belong to other stories. The other"
  echo "sections (files, public surface, definition of done) describe the whole slice: build only the"
  echo "parts of them that this story's items need."
  if [ "$depends" != "-" ]; then
    echo "Already merged: $(printf '%s' "$depends" | sed -E "s/S([0-9]+)/$slice-\1/g")."
    echo "The rules they implemented are kept below for context, not to be implemented again: $context."
  fi
  if [ -n "$notes" ] && [ "$notes" != "none" ]; then
    echo
    echo "Notes for this story: $notes"
  fi
  echo
  echo "---"
  echo
  awk -v want="S$n" -v context="$context" '
    function trim(s) { gsub(/^[ \t\r]+|[ \t\r]+$/, "", s); return s }
    function mark_of(line,    m) {
      if (match(line, /^(- )?[A-Za-z][A-Za-z0-9_.-]*\. /)) { m = substr(line, 1, RLENGTH - 2); sub(/^- /, "", m); return m }
      if (match(line, /^\| `[^`]+` \|/)) return substr(line, 4, RLENGTH - 6)
      return ""
    }
    BEGIN { n = split(context, parts, ","); for (i = 1; i <= n; i++) mine[trim(parts[i])] = 1 }
    FNR == 1 { file_number++ }
    { sub(/\r$/, "") }
    file_number == 1 { if ($0 !~ /^#/ && split($0, f, "\t") >= 3) scope[f[1]] = f[3]; next }
    file_number == 2 {
      if (/^## /) { current = ""; if (match($0, /^## S[0-9]+: */)) { current = substr($0, 4, RLENGTH - 4); sub(/:.*/, "", current) } next }
      if (current == want && match($0, /^(owns|outputs|tests):/)) {
        n = split(substr($0, RLENGTH + 1), parts, ",")
        for (i = 1; i <= n; i++) mine[trim(parts[i])] = 1
      }
      next
    }
    # the brief: skip the front matter, then drop the items of other stories
    FNR == 1 && /^---/ { in_front = 1; next }
    in_front { if (/^---/) in_front = 0; next }
    {
      m = mark_of($0)
      other = (m in scope) && scope[m] != "all" && !(m in mine)
      if (other && $0 ~ /^\|/) next
      if (other) { dropping = 1; swallowed_blank = 0; next }
      if (dropping) {
        if ($0 == "") { swallowed_blank = 1; next }
        if ($0 ~ /^[ \t]/) next
        dropping = 0
        if (swallowed_blank) print ""
      }
      print
    }
  ' "$d/items.tsv" "$d/stories.md" "$d/brief.md"
}

route_of() {
  local d="$1" n="$2" route
  route="$(story_field "$d" "$n" route)"
  [ -n "$route" ] || route="$(front "$d/brief.md" route)"
  norm_route "$route"
}

# The issue of a story, by its title prefix: prints "<number> <state>" or nothing.
issue_of() {
  gh issue list --repo "$REPO" --state all --search "\"$1:\" in:title" --limit 20 \
    --json number,title,state --jq ".[] | select(.title | startswith(\"$1: \")) | \"\(.number) \(.state)\"" | head -n 1
}

# Sets: number title body labels story slice n slug branch worktree trial parent route
load_issue() {
  number="${1:-}"
  [[ "$number" =~ ^[0-9]+$ ]] || die "give the story issue number"
  title="$(gh issue view "$number" --repo "$REPO" --json title --jq .title)"
  body="$(gh issue view "$number" --repo "$REPO" --json body --jq .body | tr -d '\r')"
  labels="$(gh issue view "$number" --repo "$REPO" --json labels --jq '[.labels[].name] | join(" ")')"
  # "<slice>-<n>: <title>", or "<slice>: <title>" for a slice that was started before stories.
  [[ "$title" =~ ^([0-9]+-[0-9]+(-[0-9]+)?):[[:space:]]*(.+)$ ]] \
    || die "title of #$number must be '<story id>: <title>', found '$title'"
  story="${BASH_REMATCH[1]}"
  slug="$(slug_of "${BASH_REMATCH[3]}")"
  slice="$(printf '%s' "$story" | cut -d- -f1-2)"
  n="$(printf '%s' "$story" | cut -d- -f3)"
  branch="feat/$story-$slug"
  worktree="$main_root/.claude/worktrees/story-$story"
  if [ -z "$n" ] && [ -d "$main_root/.claude/worktrees/slice-$story" ]; then worktree="$main_root/.claude/worktrees/slice-$story"; fi
  trial="$main_root/artifacts/trials/$story"
  parent="$(printf '%s' "$body" | grep -oE 'Part of #[0-9]+' | head -n 1 | grep -oE '[0-9]+' || true)"
  [ -n "$parent" ] || die "body of #$number must contain 'Part of #<parent issue>'"
  route=""
  local candidate
  for candidate in $LEGACY_ROUTES $ROUTES; do
    if has_label "$candidate"; then route="$(norm_route "$candidate")"; fi
  done
  [ -n "$route" ] || die "#$number has no routing label (impl or impl/senior)"
}

# Label changes on the story issue. AIAKOS_NO_WRITE=1 prints them instead (for trying a command out).
relabel() {
  if [ "${AIAKOS_NO_WRITE:-}" = "1" ]; then echo "would relabel #$number: -$1 +$2"; return; fi
  gh issue edit "$number" --repo "$REPO" --remove-label "$1" --add-label "$2" >/dev/null
}

has_label() { case " $labels " in *" $1 "*) return 0 ;; *) return 1 ;; esac; }

# What the implementer does next. The seat that ran the command acts on it (route "impl": the
# impl seat; "impl/senior": the senior seat).
print_task() {
  echo
  echo "Next, for the $route implementer, in the worktree:"
  echo "  $1"
  echo "Nobody else builds or tests in this worktree while that run is in progress."
}

cmd_check() {
  slice_dir "${1:-}"
  [ -f "$dir/items.tsv" ] || die "no item list: docs/briefs/$1/items.tsv"
  [ -f "$dir/stories.md" ] || die "no stories yet: docs/briefs/$1/stories.md (run 'split $1')"
  run_check "$dir"
}

cmd_split() {
  local slice="${1:-}" run
  slice_dir "$slice"
  [ -f "$dir/items.tsv" ] || die "no item list: docs/briefs/$slice/items.tsv"
  run="$main_root/artifacts/split/$slice"
  rm -rf "$run"; mkdir -p "$run"
  cp "$dir/brief.md" "$dir/items.tsv" "$run/"
  cp "$tools/story-check.sh" "$run/"
  cp "$tools/prompts/split.md" "$run/PROMPT.md"
  echo "slice    $slice"
  echo "run dir  $(native "$run") (git-ignored)"
  echo
  echo "Next: in that directory, follow PROMPT.md (it asks for one file, stories.md), then run"
  echo "'tools/story.sh split-done $slice' here."
}

cmd_split_done() {
  local slice="${1:-}" run file
  slice_dir "$slice"
  run="$main_root/artifacts/split/$slice"
  [ -f "$run/stories.md" ] || die "the splitter wrote no stories.md in $(native "$run")"
  for file in brief.md items.tsv; do
    cmp -s "$dir/$file" "$run/$file" || die "the splitter changed $file, or it changed here since 'split'; run 'split $slice' again"
  done
  bash "$tools/story-check.sh" "$run/items.tsv" "$run/stories.md" "$run/brief.md"
  tr -d '\r' < "$run/stories.md" > "$dir/stories.md"
  echo "copied to docs/briefs/$slice/stories.md; next: the story check by the reviewer, then commit"
}

cmd_show() {
  slice_dir "${1:-}"
  [[ "${2:-}" =~ ^[0-9]+$ ]] || die "give the story number, for example: show $1 2"
  assemble "$dir" "$1" "$2"
}

cmd_status() {
  local d slice status n title found state
  git fetch --quiet origin main 2>/dev/null || true
  printf '%-9s %-12s %-14s %s\n' "story" "state" "issue" "title"
  for slice in $( { if [ "$BRIEF_REF" = "WORKTREE" ]; then ls "$here/docs/briefs"; else git ls-tree --name-only "$BRIEF_REF" docs/briefs/ | sed 's|docs/briefs/||'; fi; } | grep -E '^[0-9]+-[0-9]+$' | sort -t- -k1,1n -k2,2n); do
    slice_dir "$slice" ref
    d="$dir"
    status="$(front "$d/brief.md" status)"
    if [ ! -f "$d/stories.md" ]; then
      printf '%-9s %-12s %-14s %s\n' "$slice" "$status" "-" "$(front "$d/brief.md" title) (not split)"
      continue
    fi
    for n in $(grep -oE '^## S[0-9]+:' "$d/stories.md" | grep -oE '[0-9]+'); do
      title="$(story_field "$d" "$n" title)"
      found="$(issue_of "$slice-$n")"
      if [ -z "$found" ]; then
        state="$status"; [ "$status" = "approved" ] && state="analysed"
        printf '%-9s %-12s %-14s %s\n' "$slice-$n" "$state" "-" "$title"
      else
        state="$(gh issue view "${found%% *}" --repo "$REPO" --json labels,state --jq 'if .state == "CLOSED" then "done" else ([.labels[].name] | map(select(. == "ready" or . == "in-progress" or . == "needs-review" or . == "partial" or . == "blocked")) | join(",")) end')"
        printf '%-9s %-12s %-14s %s\n' "$slice-$n" "${state:-open}" "#${found%% *}" "$title"
      fi
    done
  done
  echo
  echo "Story worktrees:"
  git worktree list | grep -E '/(story|slice|analysis|baseline)-[0-9]+-[0-9]+' || echo "  none"
}

cmd_ready() {
  local slice="${1:-}" n="${2:-}" dry="" story title status depends dep found findings problems=0 body_file parent_labels new parent_id child_id
  [ "${3:-}" = "--dry-run" ] && dry=1
  [[ "$n" =~ ^[0-9]+$ ]] || die "usage: ready <slice> <n> [--dry-run]"
  slice_dir "$slice" ref
  story="$slice-$n"
  [ -f "$dir/stories.md" ] && [ -f "$dir/items.tsv" ] || die "docs/briefs/$slice has no items.tsv or stories.md on $BRIEF_REF"
  title="$(story_field "$dir" "$n" title)"
  [ -n "$title" ] || die "no story S$n in docs/briefs/$slice/stories.md"
  parent="$(front "$dir/brief.md" issue)"
  route="$(route_of "$dir" "$n")"
  echo "story    $story: $title (part of #$parent)"

  problem() { echo "not ready: $*"; problems=$((problems + 1)); }
  status="$(front "$dir/brief.md" status)"
  [ "$status" = "approved" ] || problem "1. the brief's status is '$status', not 'approved'"
  run_check "$dir" > /dev/null 2>&1 || problem "2. the story check fails (tools/story.sh check $slice)"
  [ -f "$main_root/artifacts/trials/$story/gate.sh" ] || problem "3. no acceptance gate: artifacts/trials/$story/gate.sh (it must fail on main before the run)"
  if [ -f "$main_root/artifacts/trials/$story/gate.sh" ]; then
    tail -n 1 "$main_root/artifacts/trials/$story/main-before.txt" 2>/dev/null | grep -q '^BASELINE: fail' \
      || problem "3. the gate was not seen to fail on main: run 'tools/story.sh baseline $slice $n'"
  fi
  depends="$(story_field "$dir" "$n" depends)"
  if [ "$depends" != "-" ]; then
    for dep in $(printf '%s' "$depends" | tr ',' ' '); do
      found="$(issue_of "$slice-${dep#S}")"
      [ "${found##* }" = "CLOSED" ] || problem "4. it depends on $slice-${dep#S}, which is not done"
    done
  fi
  if [ -f "$dir/findings.md" ] && findings="$(grep -c '^- \[ \]' "$dir/findings.md")" && [ "$findings" -gt 0 ]; then
    problem "5. docs/briefs/$slice/findings.md has $findings open finding(s)"
  fi
  case " $ROUTES " in *" $route "*) ;; *) problem "6. no route (impl or impl/senior) in the brief or the story" ;; esac
  if [ "$route" = "impl/senior" ] && [ -z "$(story_field "$dir" "$n" escalation)" ]; then
    problem "6. the route is impl/senior but the story has no 'escalation:' line with the reason it cannot be split"
  fi
  [ "$problems" -eq 0 ] || die "$story does not meet the definition of ready ($problems problem(s))"

  found="$(issue_of "$story")"
  [ -z "$found" ] || die "$story already has issue #${found%% *}"
  parent_labels="$(gh issue view "$parent" --repo "$REPO" --json labels --jq '[.labels[].name | select(startswith("type/") or startswith("area/"))] | join(",")')"
  body_file="$(mktemp)"
  assemble "$dir" "$slice" "$n" > "$body_file"
  echo "route    $route"
  echo "labels   ready,$route${parent_labels:+,$parent_labels}"
  echo "body     $(wc -l < "$body_file") lines (tools/story.sh show $slice $n)"
  if [ -n "$dry" ]; then echo "dry run: ready, nothing created"; rm -f "$body_file"; return; fi

  new="$(gh issue create --repo "$REPO" --title "$story: $title" --body-file "$body_file" --label "ready,$route${parent_labels:+,$parent_labels}")"
  rm -f "$body_file"
  echo "issue    $new"
  child_id="$(gh api "repos/$REPO/issues/${new##*/}" --jq .id)"
  if gh api --method POST "repos/$REPO/issues/$parent/sub_issues" -F "sub_issue_id=$child_id" > /dev/null 2>&1; then
    echo "linked   sub-issue of #$parent"
  else
    echo "warning  could not link it as a sub-issue of #$parent; the body says 'Part of #$parent'"
  fi
}

cmd_next() {
  local want="${1:-}" route_label
  for route_label in $ROUTES $LEGACY_ROUTES; do
    [ -z "$want" ] || [ "$want" = "$(norm_route "$route_label")" ] || continue
    gh issue list --repo "$REPO" --state open --label ready --label "$route_label" --limit 100 --json number,title \
      --jq ".[] | select(.title | test(\"^[0-9]+-[0-9]+-[0-9]+: \")) | \"\(.title | split(\":\")[0])\t#\(.number)\t$route_label\t\(.title)\""
  done | sort -t- -k1,1n -k2,2n -k3,3n > "${TMPDIR:-/tmp}/story-next.$$" || true
  if [ ! -s "${TMPDIR:-/tmp}/story-next.$$" ]; then echo "no story is ready"; rm -f "${TMPDIR:-/tmp}/story-next.$$"; return; fi
  echo "next:"
  head -n 1 "${TMPDIR:-/tmp}/story-next.$$" | cut -f2-
  if [ "$(wc -l < "${TMPDIR:-/tmp}/story-next.$$")" -gt 1 ]; then echo; echo "also ready:"; tail -n +2 "${TMPDIR:-/tmp}/story-next.$$" | cut -f2-; fi
  rm -f "${TMPDIR:-/tmp}/story-next.$$"
}

# Removes every state label the issue has and sets one.
set_state() {
  local old args=()
  for old in ready in-progress needs-review blocked partial; do
    if [ "$old" != "$1" ] && has_label "$old"; then args+=(--remove-label "$old"); fi
  done
  if [ "${AIAKOS_NO_WRITE:-}" = "1" ]; then echo "would set #$number to $1"; return; fi
  gh issue edit "$number" --repo "$REPO" "${args[@]}" --add-label "$1" >/dev/null
}

# Failed attempts so far: gate runs that reached the build and failed, and reviews that blocked.
# A run that stopped at a process check (uncommitted changes, a file outside the paths) is not one.
failed_attempts() {
  local count=0 file
  for file in "$trial"/gate-*.txt; do
    [ -f "$file" ] || continue
    if grep -q '^== dotnet build' "$file" && tail -n 1 "$file" | grep -q 'GATE: fail'; then count=$((count + 1)); fi
  done
  for file in "$trial"/review-block-*.md; do [ -f "$file" ] && count=$((count + 1)); done
  printf '%s' "$count"
}

cmd_start() {
  load_issue "${1:-}"
  case "${2:-}" in
    "") ;;
    --retry) start_retry; return ;;
    *) die "unknown option for start: $2" ;;
  esac
  has_label ready || die "#$number is not labelled 'ready' (labels: $labels)"
  [ ! -e "$worktree" ] || die "worktree already exists: $worktree (to send the story back to its implementer: start $number --retry)"
  if git show-ref --verify --quiet "refs/heads/$branch"; then die "branch already exists: $branch"; fi
  [ -f "$trial/gate.sh" ] || die "no acceptance gate: $trial/gate.sh"
  git fetch --quiet origin main
  git worktree add --quiet -b "$branch" "$worktree" origin/main
  mkdir -p "$worktree/artifacts/briefs"
  printf '%s\n' "$body" > "$worktree/artifacts/briefs/$story.md"
  relabel ready in-progress
  echo "story    $story (#$number, part of #$parent, $route)"
  echo "branch   $branch"
  echo "worktree $(native "$worktree")"
  echo "label    in-progress"
  print_task "Implement the story at artifacts/briefs/$story.md. Follow it exactly. Run build and tests until green. One commit."
}

# Sends a story back to its implementer after a blocking review, a changed brief or a failed
# gate: same worktree and branch, main merged in, the story text written again from the brief.
start_retry() {
  local last count reason="" text
  [ -d "$worktree" ] || die "no worktree for $story: nothing to retry (use 'start $number')"
  git show-ref --verify --quiet "refs/heads/$branch" || die "no branch $branch: nothing to retry"
  [ -z "$(git -C "$worktree" status --porcelain)" ] || die "the worktree has uncommitted changes: $worktree"
  last="$(ls -t "$trial"/gate-*.txt 2>/dev/null | head -n 1 || true)"
  if [ -n "$last" ]; then
    [ "$(git -C "$worktree" rev-parse --short HEAD)" = "$(head -n 1 "$last" | grep -oE 'commit [0-9a-f]+' | cut -d' ' -f2)" ] \
      || die "the branch has commits the gate has not seen; run 'done $number' first"
  fi
  git fetch --quiet origin main
  if ! git -C "$worktree" merge --quiet --no-edit origin/main >/dev/null 2>&1; then
    git -C "$worktree" merge --abort 2>/dev/null || true
    die "origin/main does not merge cleanly into $branch; merge it by hand in $worktree, then run this again"
  fi
  mkdir -p "$worktree/artifacts/briefs"
  text="$worktree/artifacts/briefs/$story.md"
  if [ -n "$n" ] && git cat-file -e "$BRIEF_REF:docs/briefs/$slice/stories.md" 2>/dev/null; then
    slice_dir "$slice" ref
    assemble "$dir" "$slice" "$n" > "$text"
    if [ "${AIAKOS_NO_WRITE:-}" = "1" ]; then echo "would update the body of #$number"
    else gh issue edit "$number" --repo "$REPO" --body-file "$text" >/dev/null; fi
    echo "story    text written again from the brief on $BRIEF_REF; issue body updated"
  else
    printf '%s\n' "$body" > "$text"
  fi
  if [ -f "$trial/review.md" ]; then
    if head -n 1 "$trial/review.md" | grep -q '^VERDICT: block'; then
      count=$(( $(find "$trial" -maxdepth 1 -name 'review-block-*.md' | wc -l) + 1 ))
      cp "$trial/review.md" "$worktree/artifacts/briefs/$story-review.md"
      mv "$trial/review.md" "$trial/review-block-$count.md"
      reason="The review that sent it back is in artifacts/briefs/$story-review.md. "
    else
      count=$(( $(find "$trial" -maxdepth 1 -name 'review-superseded-*.md' | wc -l) + 1 ))
      mv "$trial/review.md" "$trial/review-superseded-$count.md"
    fi
  fi
  if [ -n "$last" ] && tail -n 1 "$last" | grep -q 'GATE: fail'; then
    cp "$last" "$worktree/artifacts/briefs/$story-$(basename "$last")"
    reason="${reason}The last gate output is in artifacts/briefs/$story-$(basename "$last"). "
  fi
  set_state in-progress
  echo "story    $story (#$number, part of #$parent, $route), retry"
  echo "branch   $branch (origin/main merged in)"
  echo "worktree $(native "$worktree")"
  echo "label    in-progress"
  echo "attempts $(failed_attempts) failed so far"
  print_task "The story at artifacts/briefs/$story.md was sent back. Read it again: its text may have changed. ${reason}Fix what is named there and nothing else. Add one new commit."
}

# The gate. Everything it checks is decided before the run; nothing is judged here.
cmd_done() {
  load_issue "${1:-}"
  [ -d "$worktree" ] || die "no worktree for $story: $worktree"
  [ -f "$trial/gate.sh" ] || die "no acceptance gate: $trial/gate.sh"
  local run failed attempt log paths path file bad="" ok=1
  # The file number counts every run; the attempt counts only runs that can use up the retry.
  run=$(( $(find "$trial" -maxdepth 1 -name 'gate-*.txt' | wc -l) + 1 ))
  failed="$(failed_attempts)"
  attempt=$((failed + 1))
  log="$trial/gate-$run.txt"
  {
    echo "Gate for story $story, run $run, attempt $attempt, commit $(git -C "$worktree" rev-parse --short HEAD)."
    echo
    if [ -n "$(git -C "$worktree" status --porcelain)" ]; then echo "FAIL: the worktree has uncommitted changes"; ok=0; fi
    if [ -z "$(git -C "$worktree" log --oneline origin/main..HEAD)" ]; then echo "FAIL: no commit on the branch"; ok=0; fi

    paths="$(git show "origin/main:docs/briefs/$slice/brief.md" 2>/dev/null | awk '/^---/ { c++; next } c == 1 && index($0, "paths:") == 1 { v = substr($0, 7); gsub(/[][",]/, " ", v); print v }')"
    if [ -n "$paths" ]; then
      for file in $(git -C "$worktree" diff --name-only origin/main...HEAD); do
        bad="$file"
        for path in $paths; do case "$file" in "$path"*) bad="" ;; esac; done
        if [ -n "$bad" ]; then echo "FAIL: $file is outside the brief's paths ($paths)"; ok=0; fi
      done
    fi
    if git -C "$worktree" diff origin/main...HEAD | grep -E '^\+' | grep -E 'NoWarn|#pragma warning disable|SuppressMessage|<PackageReference[^>]*Version=' > /dev/null; then
      echo "FAIL: the change adds NoWarn, #pragma warning disable, SuppressMessage or a Version on a PackageReference"; ok=0
    fi
    if git -C "$worktree" diff --name-only origin/main...HEAD | xargs -r git -C "$worktree" ls-files --eol -- | grep -v 'i/lf' | grep -v 'i/-text' | grep -v 'i/none' | grep . ; then
      echo "FAIL: the files above are not LF in the index"; ok=0
    fi

    if [ "$ok" -eq 1 ]; then
      echo "== dotnet build -c Release"
      if ! (cd "$worktree" && dotnet build -c Release 2>&1 | tail -n 15); then echo "FAIL: build"; ok=0; fi
    fi
    if [ "$ok" -eq 1 ]; then
      echo "== acceptance (gate.sh)"
      if ! (cd "$worktree" && STORY="$story" TRIAL="$trial" bash "$trial/gate.sh" 2>&1); then echo "FAIL: acceptance"; ok=0; fi
      if [ -n "$(git -C "$worktree" status --porcelain)" ]; then
        echo "FAIL: gate.sh left files behind in the worktree:"; git -C "$worktree" status --short; ok=0
      fi
    fi
    echo
    if [ "$ok" -eq 1 ]; then echo "GATE: pass"; else echo "GATE: fail"; fi
  } > "$log" 2>&1 || true
  cat "$log"
  echo
  if tail -n 1 "$log" | grep -q 'GATE: pass'; then
    set_state needs-review
    echo "label    needs-review"
    echo "Inputs for the reviewer (one read of the diff):"
    echo "  story     $story"
    echo "  worktree  $(native "$worktree")"
    echo "  brief     $(native "$worktree/artifacts/briefs/$story.md")"
    echo "  gate      $(native "$log")"
  elif ! grep -q '^== dotnet build' "$log"; then
    echo "A process check failed before the build. This run is not an attempt: fix what the"
    echo "FAIL lines name and run 'done $number' again."
  elif [ "$failed" -eq 0 ]; then
    mkdir -p "$worktree/artifacts/briefs"
    cp "$log" "$worktree/artifacts/briefs/$story-gate-$run.txt"
    echo "One retry is allowed (stop rule). The gate output is in the worktree."
    print_task "The acceptance gate failed for the story at artifacts/briefs/$story.md. Its output is in artifacts/briefs/$story-gate-$run.txt. Fix the code so that the gate passes. Do not change the scope of the story. Add one new commit."
  else
    set_state blocked
    echo "label    blocked"
    echo "Stop rule: this is failed attempt $attempt (failed gates and blocking reviews count). Do not"
    echo "run it again as it is. Tag each failure:"
    echo "  context-gap   the brief lacked it: fix the brief or split the story (slice $slice)"
    echo "  judgment-gap  the brief had it: set the route to impl/senior, once"
    echo "If the brief depends on something that does not exist, the story may end partial:"
    echo "write artifacts/trials/$story/partial.md and use 'pr $number --partial'."
  fi
}

cmd_pr() {
  load_issue "${1:-}"
  [ -d "$worktree" ] || die "no worktree for $story: $worktree"
  local last pr_title body_file arg reviewed="" partial=""
  for arg in "${@:2}"; do
    case "$arg" in
      --maintainer-reviewed) reviewed=1 ;;
      --partial) partial=1 ;;
      "") ;;
      *) die "unknown option for pr: $arg" ;;
    esac
  done
  last="$(ls -t "$trial"/gate-*.txt 2>/dev/null | head -n 1 || true)"
  [ -n "$last" ] || die "no gate run for $story (tools/story.sh done $number)"
  if [ -n "$partial" ]; then
    # Partial: the gate ran and did not pass, and partial.md says which tests cannot pass and why.
    [ -s "$trial/partial.md" ] || die "--partial needs artifacts/trials/$story/partial.md: the tests that do not pass, the missing capability with file and line, and the new item for it"
    tail -n 1 "$last" | grep -q 'GATE: fail' || die "the last gate run of $story passed; open the pull request without --partial"
  else
    tail -n 1 "$last" | grep -q 'GATE: pass' || die "the last gate run of $story did not pass (tools/story.sh done $number)"
  fi
  [ "$(git -C "$worktree" rev-parse --short HEAD)" = "$(head -n 1 "$last" | grep -oE 'commit [0-9a-f]+' | cut -d' ' -f2)" ] \
    || die "the branch changed after the last gate run; run 'done $number' again"
  # A partial story is read by the maintainer at the pull request.
  if [ -z "$reviewed" ] && [ -z "$partial" ]; then
    [ -f "$trial/review.md" ] && head -n 1 "$trial/review.md" | grep -q '^VERDICT: pass' \
      || die "no passing review in artifacts/trials/$story/review.md (or pass --maintainer-reviewed when you read the diff yourself)"
  fi
  [ -z "$(git -C "$worktree" status --porcelain)" ] || die "worktree has uncommitted changes"
  pr_title="$(git -C "$worktree" log --reverse --format=%s origin/main..HEAD | head -n 1)"
  body_file="$(mktemp)"
  {
    if [ -f "$trial/pr-body.md" ]; then cat "$trial/pr-body.md"; else echo "Story $story: ${title#*: }"; fi
    echo
    if [ -n "$partial" ]; then
      echo "## Partial"
      echo
      echo "The acceptance gate does not pass. The maintainer decides whether this story ends partial."
      echo
      cat "$trial/partial.md"
      echo
    fi
    echo "## Verification"
    echo
    echo '```text'
    grep -E '^(Gate for|== |GATE:|Test run summary|  (total|failed|succeeded|skipped):|    [0-9]+ (Warning|Error))' "$last" || true
    echo '```'
    if [ -f "$trial/review.md" ]; then echo; echo "Review: $(head -n 1 "$trial/review.md")"; else echo; echo "Review: read by the maintainer."; fi
    printf '\nCloses #%s. Part of #%s.\n' "$number" "$parent"
  } > "$body_file"
  git -C "$worktree" push --quiet -u origin "$branch"
  if [ -n "$partial" ]; then
    set_state partial
    echo "label    partial"
  fi
  gh pr create --repo "$REPO" --base main --head "$branch" --title "$pr_title" --body-file "$body_file"
  rm -f "$body_file"
}

# A worktree for the analysis of one slice (brief, items, stories, findings), so that two
# slices can be analysed at the same time and nobody works in the main checkout.
cmd_analysis() {
  local slice="${1:-}" wt branch_name
  is_slice "$slice" || die "give a slice id such as 14-3, found '$slice'"
  wt="$main_root/.claude/worktrees/analysis-$slice"
  branch_name="docs/brief-$slice"
  if [ "${2:-}" = "--remove" ]; then
    if [ -d "$wt" ]; then
      git worktree remove "$wt" || die "worktree not removed (uncommitted changes?): $wt"
      echo "removed worktree $(native "$wt")"
    fi
    if git show-ref --verify --quiet "refs/heads/$branch_name"; then
      git branch -d "$branch_name" >/dev/null 2>&1 && echo "deleted branch $branch_name" \
        || echo "branch $branch_name kept: git does not see it as merged (squash merge?). Delete it yourself with: git branch -D $branch_name"
    fi
    return
  fi
  [ -z "${2:-}" ] || die "unknown option for analysis: $2"
  if [ ! -d "$wt" ]; then
    git fetch --quiet origin
    if git show-ref --verify --quiet "refs/heads/$branch_name"; then
      git worktree add --quiet "$wt" "$branch_name"
    elif git show-ref --verify --quiet "refs/remotes/origin/$branch_name"; then
      git worktree add --quiet -b "$branch_name" "$wt" "origin/$branch_name"
    else
      git worktree add --quiet -b "$branch_name" "$wt" origin/main
    fi
  fi
  echo "slice    $slice"
  echo "branch   $branch_name"
  echo "worktree $(native "$wt")"
  echo "Work on docs/briefs/$slice/ there. After the merge: tools/story.sh analysis $slice --remove"
}

# Runs a story's acceptance gate against main, in a worktree that is removed again. The gate
# must fail there; "ready" asks for that. Whether it fails for the right reason is for QA to read.
cmd_baseline() {
  local slice="${1:-}" n="${2:-}" story trial wt out commit rc=0
  is_slice "$slice" && [[ "$n" =~ ^[0-9]+$ ]] || die "usage: baseline <slice> <n>"
  story="$slice-$n"
  trial="$main_root/artifacts/trials/$story"
  [ -f "$trial/gate.sh" ] || die "no acceptance gate: $trial/gate.sh"
  wt="$main_root/.claude/worktrees/baseline-$story"
  git fetch --quiet origin main
  [ ! -d "$wt" ] || git worktree remove --force "$wt"
  git worktree add --quiet --detach "$wt" origin/main
  commit="$(git -C "$wt" rev-parse --short HEAD)"
  out="$trial/main-before.txt"
  {
    echo "Baseline for story $story on main, commit $commit."
    echo
    (cd "$wt" && STORY="$story" TRIAL="$trial" bash "$trial/gate.sh" 2>&1) || rc=$?
    echo
    if [ "$rc" -ne 0 ]; then echo "BASELINE: fail (as it must; QA reads why)"
    else echo "BASELINE: pass (the acceptance tests do not fail on main: the story is not ready)"; fi
  } > "$out" 2>&1
  git worktree remove --force "$wt"
  cat "$out"
  [ "$rc" -ne 0 ] || return 1
}

# Runs after the merge, when the issue is closed.
cmd_cleanup() {
  load_issue "${1:-}"
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
  check)      cmd_check "${2:-}" ;;
  split)      cmd_split "${2:-}" ;;
  split-done) cmd_split_done "${2:-}" ;;
  show)       cmd_show "${2:-}" "${3:-}" ;;
  status)     cmd_status ;;
  ready)      cmd_ready "${2:-}" "${3:-}" "${4:-}" ;;
  next)       cmd_next "${2:-}" ;;
  start)      cmd_start "${2:-}" "${3:-}" ;;
  analysis)   cmd_analysis "${2:-}" "${3:-}" ;;
  baseline)   cmd_baseline "${2:-}" "${3:-}" ;;
  done)       cmd_done "${2:-}" ;;
  pr)         cmd_pr "${2:-}" "${3:-}" "${4:-}" ;;
  cleanup)    cmd_cleanup "${2:-}" ;;
  *)          sed -n '2,45p' "$0" | sed 's/^# \{0,1\}//'; exit 2 ;;
esac
