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
#   tools/story.sh analysis-pr <slice> [--look "<reason>"]
#                                         the one pull request of a slice's analysis: checks the
#                                         split and the review, sets status approved and the
#                                         index row; the maintainer's merge is the approval
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
#                                         For an issue labelled type/chore or type/bug that is
#                                         not a story: no brief, no acceptance tests; the gate
#                                         of "done" is the build and every existing test
#   tools/story.sh start <issue> --retry  sends a story back to its implementer: same worktree,
#                                         main merged in, story text and issue body written
#                                         again from the brief, label -> in-progress
#   tools/story.sh done <issue>           the gate: clean tree, allowed paths, build, acceptance
#                                         tests; label -> needs-review, or one retry, or blocked.
#                                         A run that stops at a process check before the build
#                                         is not an attempt
#                                         A failure counts only for the version of the story
#                                         text and acceptance tests it was judged by
#   tools/story.sh waive <issue> <run> infrastructure|test-defect "<evidence>"
#                                         takes one failed run out of the count, on record
#   tools/story.sh pr <issue> [--maintainer-reviewed] [--partial]
#                                         pushes the branch and opens the pull request.
#                                         --partial: the gate did not pass because the brief
#                                         depends on something that does not exist; needs
#                                         artifacts/trials/<story>/partial.md, labels "partial"
#   tools/story.sh cleanup <issue>        removes the worktree and the local branch after merge
#
# Passing work on (the seats use this in place of "rig queue handoff"):
#   tools/story.sh hand <target> [--role <role>] [--item <qitem>] --summary "<one line>"
#                       (--body "<text>" | --body-file <path>) [--evidence <path or link>]
#                                         <target> is a pool (low, high) or a seat (architect,
#                                         reviewer, gate, lead). A pool needs
#                                         --role: a file of rigs/aiakos-delivery/roles/. The
#                                         destination gets a clean conversation when it is idle
#                                         and has nothing in progress, then the item. --item
#                                         closes the caller's own item as handed off.
#                                         artifacts/hand.log records every delivery
#   tools/story.sh events                 prints what changed on GitHub since the last look:
#                                         merged pull requests, chores and bugs labelled
#                                         ready. Each change once; the lead runs it
#   tools/story.sh allowance [--every <minutes>]
#                                         one line with the Claude and Codex allowances as
#                                         last reported; --every prints at most that often
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
    # One fetch per run is enough; "status" reads every slice.
    if [ -z "${fetched_main:-}" ]; then git fetch --quiet origin main 2>/dev/null || true; fetched_main=1; fi
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

# Removes what the cut of a story leaves empty: a table with a header and no rows, and a
# section that then holds nothing else. A note takes its place, so that an implementer does not
# read the gap as a truncated brief.
drop_empty_tables() {
  awk '
    function is_table(s) { return s ~ /^\|/ }
    function is_rule(s) { return s ~ /^\|[ \t:|-]+$/ }
    function is_heading(s) { return s ~ /^#+ / }
    { line[++count] = $0 }
    END {
      # An empty table: a header line, a rule line, and no row after them.
      for (i = 1; i <= count; i++) {
        if (is_table(line[i]) && !(i > 1 && is_table(line[i - 1])) && i < count && is_rule(line[i + 1]) && !(i + 2 <= count && is_table(line[i + 2]))) {
          gone[i] = gone[i + 1] = 1
          note[i] = "(This table of the brief has no rows for this story.)"
        }
      }
      # A section whose body is only such tables goes as a whole.
      for (i = 1; i <= count; i++) {
        if (!is_heading(line[i])) continue
        last = count
        for (j = i + 1; j <= count; j++) if (is_heading(line[j])) { last = j - 1; break }
        tables = 0; other = 0
        for (j = i + 1; j <= last; j++) {
          if (j in note) tables++
          else if (!(j in gone) && line[j] !~ /^[ \t]*$/) other++
        }
        if (tables > 0 && other == 0) {
          title = line[i]; sub(/^#+ +/, "", title)
          for (j = i; j <= last; j++) { gone[j] = 1; delete note[j] }
          note[i] = "(The section \"" title "\" of the brief has no rows for this story and is left out.)"
          blank_after[i] = 1
        }
      }
      for (i = 1; i <= count; i++) {
        if (i in note) { print note[i]; if (i in blank_after) print ""; continue }
        if (i in gone) continue
        print line[i]
      }
    }'
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
  ' "$d/items.tsv" "$d/stories.md" "$d/brief.md" | drop_empty_tables
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

# Sets: number title body labels kind story slice n slug branch worktree trial parent route origin
# kind is "story", or "chore" for an issue labelled type/chore or type/bug that has no brief.
load_issue() {
  number="${1:-}"
  [[ "$number" =~ ^[0-9]+$ ]] || die "give the story issue number"
  title="$(gh issue view "$number" --repo "$REPO" --json title --jq .title)"
  body="$(gh issue view "$number" --repo "$REPO" --json body --jq .body | tr -d '\r')"
  labels="$(gh issue view "$number" --repo "$REPO" --json labels --jq '[.labels[].name] | join(" ")')"
  if ! [[ "$title" =~ ^[0-9]+-[0-9]+(-[0-9]+)?:[[:space:]] ]] && { has_label type/chore || has_label type/bug; }; then
    kind=chore; story="chore-$number"; slice=""; n=""; parent=""; route="impl/senior"; origin="chore"
    slug="$(slug_of "$title")"
    if has_label type/bug; then branch="fix/$number-$slug"; else branch="chore/$number-$slug"; fi
    worktree="$main_root/.claude/worktrees/chore-$number"
    trial="$main_root/artifacts/trials/chore-$number"
    return
  fi
  kind=story
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
  origin="part of #$parent"
}

# Label changes on the story issue. AIAKOS_NO_WRITE=1 prints them instead (for trying a command out).
relabel() {
  if [ "${AIAKOS_NO_WRITE:-}" = "1" ]; then echo "would relabel #$number: -$1 +$2"; return; fi
  gh issue edit "$number" --repo "$REPO" --remove-label "$1" --add-label "$2" >/dev/null
}

has_label() { case " $labels " in *" $1 "*) return 0 ;; *) return 1 ;; esac; }

# What the implementer does next. The seat that ran the command acts on it (route "impl": the
# Low pool; "impl/senior": the High pool).
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
  local d slice status n title found state cache
  git fetch --quiet origin main 2>/dev/null || true
  # One call for every issue, not one or two per story: "<story id>\t<number>\t<state>".
  cache="$(mktemp)"
  gh issue list --repo "$REPO" --state all --limit 1000 --json number,title,state,labels --jq '
    .[] | select(.title | test("^[0-9]+-[0-9]+(-[0-9]+)?: ")) |
    [(.title | split(":")[0]), (.number | tostring),
     (if .state == "CLOSED" then "done" else ([.labels[].name] | map(select(. == "ready" or . == "in-progress" or . == "needs-review" or . == "partial" or . == "blocked")) | join(",")) end)] | @tsv' > "$cache"
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
      # The newest issue with this story id (the list is newest first).
      found="$(awk -F'\t' -v id="$slice-$n" '$1 == id { print $2 "\t" $3; exit }' "$cache")"
      if [ -z "$found" ]; then
        state="$status"; [ "$status" = "approved" ] && state="analysed"
        printf '%-9s %-12s %-14s %s\n' "$slice-$n" "$state" "-" "$title"
      else
        state="${found#*$'\t'}"
        printf '%-9s %-12s %-14s %s\n' "$slice-$n" "${state:-open}" "#${found%%$'\t'*}" "$title"
      fi
    done
  done
  rm -f "$cache"
  echo
  echo "Story worktrees:"
  git worktree list | grep -E '/(story|slice|analysis|baseline|chore)-[0-9]+' || echo "  none"
  echo
  echo "Waived gate runs:"
  local record found_waiver=""
  for record in "$main_root"/artifacts/trials/*/gate-*.waived; do
    [ -f "$record" ] || continue
    found_waiver=1
    printf '  %s run %s: %s\n' "$(basename "$(dirname "$record")")" "$(basename "$record" .waived | cut -d- -f2)" "$(sed -n 's/^kind: //p' "$record")"
  done
  [ -n "$found_waiver" ] || echo "  none"
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

# Chores and bugs that are ready: issues labelled ready with type/chore or type/bug and no story title.
ready_chores() {
  gh issue list --repo "$REPO" --state open --label ready --limit 100 --json number,title,labels \
    --jq '.[] | select((.title | test("^[0-9]+-[0-9]+(-[0-9]+)?: ") | not) and ([.labels[].name] | any(. == "type/chore" or . == "type/bug"))) | "#\(.number)\timpl/senior\t\(.title)"'
}

cmd_next() {
  local want="${1:-}" route_label chores
  for route_label in $ROUTES $LEGACY_ROUTES; do
    [ -z "$want" ] || [ "$want" = "$(norm_route "$route_label")" ] || continue
    gh issue list --repo "$REPO" --state open --label ready --label "$route_label" --limit 100 --json number,title \
      --jq ".[] | select(.title | test(\"^[0-9]+-[0-9]+-[0-9]+: \")) | \"\(.title | split(\":\")[0])\t#\(.number)\t$route_label\t\(.title)\""
  done | sort -t- -k1,1n -k2,2n -k3,3n > "${TMPDIR:-/tmp}/story-next.$$" || true
  if [ ! -s "${TMPDIR:-/tmp}/story-next.$$" ]; then echo "no story is ready"
  else
    echo "next:"
    head -n 1 "${TMPDIR:-/tmp}/story-next.$$" | cut -f2-
    if [ "$(wc -l < "${TMPDIR:-/tmp}/story-next.$$")" -gt 1 ]; then echo; echo "also ready:"; tail -n +2 "${TMPDIR:-/tmp}/story-next.$$" | cut -f2-; fi
  fi
  rm -f "${TMPDIR:-/tmp}/story-next.$$"
  if [ -z "$want" ] || [ "$want" = "impl/senior" ]; then
    chores="$(ready_chores || true)"
    if [ -n "$chores" ]; then echo; echo "chores and bugs ready (for the High pool):"; printf '%s\n' "$chores"; fi
  fi
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

# Classify the complete output, not the displayed tail, of a non-zero build.
build_infrastructure_failure() {
  # A compiler or analyzer diagnostic is the change's own failure, whatever else the output says
  # (a marker text can appear in a path or a message). MSBuild's own codes are not diagnostics.
  if grep -E 'error [A-Za-z]+[0-9]+' "$1" | grep -Evq 'error MSB[0-9]+'; then return 1; fi
  grep -Eq 'Fatal error|Internal CLR error|Unhandled exception' "$1" ||
    ! grep -Eiq 'error [[:alpha:]]+[[:digit:]]+|warning' "$1"
}

# What a gate run judged against: the story text (from the brief on main; the issue text for a
# chore) and the acceptance tests. A failed run stops counting when this changes, because the
# failure may have been the brief's or the tests' and not the implementer's.
story_version() {
  local file
  {
    if [ "$kind" = "story" ] && [ -n "$n" ] && git cat-file -e "$BRIEF_REF:docs/briefs/$slice/stories.md" 2>/dev/null; then
      slice_dir "$slice" ref
      assemble "$dir" "$slice" "$n"
    else
      printf '%s\n' "$body"
    fi
    # The gate and the test sources only: notes, logs and reviews in the folder are not tests.
    find "$trial" -type f \( -name '*.sh' -o -name '*.cs' -o -name '*.csproj' -o -name '*.props' -o -name '*.targets' \
        -o -name '*.json' -o -name '*.yaml' -o -name '*.yml' -o -name '*.sql' -o -name '*.proto' \) \
        -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/TestResults/*' 2>/dev/null | LC_ALL=C sort | while read -r file; do
      printf '== %s\n' "${file#"$trial"/}"
      tr -d '\r' < "$file"
    done
  } | sha256sum | cut -c1-12
}

version_of_log() { head -n 1 "$1" | grep -oE 'version [0-9a-f]+' | cut -d' ' -f2 || true; }

# Failed attempts so far, for the current version of the story: gate runs that reached the build
# and failed, and reviews that blocked. Not counted: a run that stopped at a process check, an
# infrastructure failure, a waived run, and a failure against an older story text or older
# acceptance tests. A log from before versions were recorded counts.
# Sets $stale to the number of failures left out because of their version.
failed_attempts() {
  local count=0 file old
  stale=0
  [ -n "${version:-}" ] || version="$(story_version)"
  for file in "$trial"/gate-*.txt; do
    [ -f "$file" ] || continue
    grep -q '^== dotnet build' "$file" && tail -n 1 "$file" | grep -qx 'GATE: fail' || continue
    [ ! -f "${file%.txt}.waived" ] || continue
    old="$(version_of_log "$file")"
    if [ -n "$old" ] && [ "$old" != "$version" ]; then stale=$((stale + 1)); else count=$((count + 1)); fi
  done
  for file in "$trial"/review-block-*.md; do
    [ -f "$file" ] || continue
    old="$(cat "${file%.md}.version" 2>/dev/null || true)"
    if [ -n "$old" ] && [ "$old" != "$version" ]; then stale=$((stale + 1)); else count=$((count + 1)); fi
  done
  attempts_failed="$count"
}

# Takes one failed gate run out of the count, with a reason on record. For a failure that was
# not the implementer's and that the version rule does not catch by itself.
cmd_waive() {
  load_issue "${1:-}"
  local run="${2:-}" why="${3:-}" evidence="${4:-}" log record waived
  [[ "$run" =~ ^[0-9]+$ ]] && [ -n "$why" ] && [ -n "$evidence" ] \
    || die "usage: waive <issue> <run> infrastructure|test-defect \"<evidence: what failed, and where it is fixed or recorded>\""
  case "$why" in
    infrastructure|test-defect) ;;
    *) die "a run can be waived as 'infrastructure' (the machine, or a flaky test the change did not touch) or 'test-defect' (the acceptance test was wrong). A failure of the implementation cannot be waived" ;;
  esac
  [ "${#evidence}" -ge 20 ] || die "the evidence must say what failed and where that is fixed or recorded"
  log="$trial/gate-$run.txt"
  [ -f "$log" ] || die "no gate run $run for $story"
  grep -q '^== dotnet build' "$log" && tail -n 1 "$log" | grep -qx 'GATE: fail' || die "run $run of $story is not a counted failure; there is nothing to waive"
  record="${log%.txt}.waived"
  [ ! -f "$record" ] || die "run $run of $story is already waived"
  waived="$(find "$trial" -maxdepth 1 -name 'gate-*.waived' | wc -l)"
  [ "$waived" -lt 2 ] || die "$story already has $waived waived runs. A third is the maintainer's decision: park it on the maintainer"
  if [ "${AIAKOS_NO_WRITE:-}" = "1" ]; then echo "would waive run $run of $story ($why): $evidence"; return; fi
  printf 'kind: %s\nby: %s\nat: %s\nevidence: %s\n' "$why" "${OPENRIG_SESSION_NAME:-${USER:-unknown}}" "$(date -u +%FT%TZ)" "$evidence" > "$record"
  gh issue comment "$number" --repo "$REPO" --body "Gate run $run waived as **$why**: $evidence" >/dev/null
  failed_attempts
  echo "waived   run $run of $story ($why); recorded on #$number"
  echo "attempts $attempts_failed failed so far"
  if has_label blocked; then echo "The issue is labelled blocked: send it back with 'start $number --retry'."; fi
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
  if [ "$kind" = "story" ]; then [ -f "$trial/gate.sh" ] || die "no acceptance gate: $trial/gate.sh"; else mkdir -p "$trial"; fi
  git fetch --quiet origin main
  git worktree add --quiet -b "$branch" "$worktree" origin/main
  mkdir -p "$worktree/artifacts/briefs"
  if [ "$kind" = "chore" ]; then printf '# #%s: %s\n\n' "$number" "$title" > "$worktree/artifacts/briefs/$story.md"; fi
  printf '%s\n' "$body" >> "$worktree/artifacts/briefs/$story.md"
  relabel ready in-progress
  echo "story    $story (#$number, $origin, $route)"
  echo "branch   $branch"
  echo "worktree $(native "$worktree")"
  echo "label    in-progress"
  if [ "$kind" = "chore" ]; then
    print_task "Do what the issue in artifacts/briefs/$story.md asks, and nothing more. There is no brief and no acceptance test: the gate is the build with zero warnings and every existing test. A bug fix adds a test that fails without the fix. Run build and tests until green. One commit."
    return
  fi
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
  git fetch --quiet origin main
  if [ -n "$last" ]; then
    # The gate must have seen the work. Merges of main made after it (by this command, or by
    # hand after a conflict) are not work.
    local seen commit second
    seen="$(head -n 1 "$last" | grep -oE 'commit [0-9a-f]+' | cut -d' ' -f2)"
    git -C "$worktree" merge-base --is-ancestor "$seen" HEAD 2>/dev/null \
      || die "the last gate ran on $seen, which is not in the branch any more; run 'done $number' first"
    for commit in $(git -C "$worktree" rev-list --first-parent "$seen..HEAD"); do
      second="$(git -C "$worktree" rev-parse --quiet --verify "$commit^2" || true)"
      if [ -z "$second" ] || ! git -C "$worktree" merge-base --is-ancestor "$second" origin/main; then
        die "the branch has commits the gate has not seen; run 'done $number' first"
      fi
    done
  fi
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
      # The review judged what the last gate run judged; it stops counting when that changes.
      if [ -n "$last" ] && [ -n "$(version_of_log "$last")" ]; then version_of_log "$last" > "$trial/review-block-$count.version"; fi
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
  echo "story    $story (#$number, $origin, $route), retry"
  echo "branch   $branch (origin/main merged in)"
  echo "worktree $(native "$worktree")"
  echo "label    in-progress"
  failed_attempts
  echo "attempts $attempts_failed failed so far for this version of the story"
  if [ "$stale" -gt 0 ]; then echo "         $stale earlier failure(s) were against an older story text or older acceptance tests and do not count"; fi
  print_task "The story at artifacts/briefs/$story.md was sent back. Read it again: its text may have changed. ${reason}Fix what is named there and nothing else. Add one new commit."
}

# The gate. Everything it checks is decided before the run; nothing is judged here.
cmd_done() {
  load_issue "${1:-}"
  [ -d "$worktree" ] || die "no worktree for $story: $worktree"
  if [ "$kind" = "story" ]; then [ -f "$trial/gate.sh" ] || die "no acceptance gate: $trial/gate.sh"; else mkdir -p "$trial"; fi
  local run failed attempt log paths path file bad="" ok=1 infrastructure=0 build_log
  # The file number counts every run; the attempt counts only runs that can use up the retry.
  run=$(( $(find "$trial" -maxdepth 1 -name 'gate-*.txt' | wc -l) + 1 ))
  failed_attempts
  failed="$attempts_failed"
  attempt=$((failed + 1))
  log="$trial/gate-$run.txt"
  {
    echo "Gate for story $story, run $run, attempt $attempt, commit $(git -C "$worktree" rev-parse --short HEAD), version $version."
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
    # Only merged exceptions can approve a restricted added line.
    local exceptions restricted
    exceptions="$(mktemp)"
    git show origin/main:tools/gate-exceptions.tsv > "$exceptions" 2>/dev/null || :
    while IFS= read -r -d '' file; do
      restricted="$(git -C "$worktree" diff --no-ext-diff --unified=0 origin/main...HEAD -- "$file" |
        awk -v story="$story" -v file="$file" '
          FILENAME == ARGV[1] {
            split($0, row, "\t")
            if (row[1] == story && row[2] == file) approved[row[3]] = 1
            next
          }
          /^@@/ { hunk = 1; next }
          hunk && /^\+/ {
            line = substr($0, 2)
            if (line !~ /NoWarn|#pragma warning disable|SuppressMessage|<PackageReference[^>]*Version=/) next
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", line)
            if (!(line in approved)) print "FAIL: " file ": " line " has no approved gate exception"
          }
        ' "$exceptions" -)"
      if [ -n "$restricted" ]; then printf '%s\n' "$restricted"; ok=0; fi
    done < <(git -C "$worktree" diff --name-only -z origin/main...HEAD)
    rm -f "$exceptions"
    if git -C "$worktree" diff --name-only origin/main...HEAD | xargs -r git -C "$worktree" ls-files --eol -- | grep -v 'i/lf' | grep -v 'i/-text' | grep -v 'i/none' | grep . ; then
      echo "FAIL: the files above are not LF in the index"; ok=0
    fi

    if [ "$ok" -eq 1 ]; then
      echo "== dotnet build -c Release"
      build_log="$(mktemp)"
      if (cd "$worktree" && dotnet build -c Release > "$build_log" 2>&1); then
        tail -n 15 "$build_log"
      else
        ok=0
        if build_infrastructure_failure "$build_log"; then
          infrastructure=1
          cat "$build_log"
          echo "FAIL: build infrastructure"
        else
          tail -n 15 "$build_log"
          echo "FAIL: build"
        fi
      fi
      rm -f "$build_log"
    fi
    if [ "$ok" -eq 1 ] && [ "$kind" = "chore" ]; then
      # A chore or a bug has no acceptance tests: every existing test is its gate.
      echo "== tests (dotnet test -c Release --no-build)"
      if ! (cd "$worktree" && dotnet test -c Release --no-build 2>&1 | tail -n 40); then echo "FAIL: tests"; ok=0; fi
      if [ -n "$(git -C "$worktree" status --porcelain)" ]; then
        echo "FAIL: the tests left files behind in the worktree:"; git -C "$worktree" status --short; ok=0
      fi
    elif [ "$ok" -eq 1 ]; then
      echo "== acceptance (gate.sh)"
      if ! (cd "$worktree" && STORY="$story" TRIAL="$trial" bash "$trial/gate.sh" 2>&1); then echo "FAIL: acceptance"; ok=0; fi
      if [ -n "$(git -C "$worktree" status --porcelain)" ]; then
        echo "FAIL: gate.sh left files behind in the worktree:"; git -C "$worktree" status --short; ok=0
      fi
    fi
    echo
    if [ "$infrastructure" -eq 1 ]; then echo "GATE: infrastructure failure"
    elif [ "$ok" -eq 1 ]; then echo "GATE: pass"; else echo "GATE: fail"; fi
  } > "$log" 2>&1 || true
  cat "$log"
  echo
  # What kind of failure it was, in one word, for whoever reports or tags it.
  if ! tail -n 1 "$log" | grep -q 'GATE: pass'; then
    if tail -n 1 "$log" | grep -qx 'GATE: infrastructure failure'; then echo "failure  infrastructure"
    elif ! grep -q '^== dotnet build' "$log"; then echo "failure  process check"
    elif grep -q '^FAIL: build$' "$log"; then echo "failure  build"
    elif grep -q '^FAIL: acceptance$' "$log"; then echo "failure  acceptance tests"
    elif grep -q '^FAIL: tests$' "$log"; then echo "failure  tests"
    else echo "failure  files left behind in the worktree"; fi
  fi
  if [ "$stale" -gt 0 ]; then echo "note     $stale earlier failure(s) were against an older story text or older acceptance tests and do not count"; fi
  if tail -n 1 "$log" | grep -q 'GATE: pass'; then
    set_state needs-review
    echo "label    needs-review"
    echo "Inputs for the reviewer (one read of the diff):"
    echo "  story     $story"
    echo "  worktree  $(native "$worktree")"
    echo "  brief     $(native "$worktree/artifacts/briefs/$story.md")"
    echo "  gate      $(native "$log")"
  elif tail -n 1 "$log" | grep -qx 'GATE: infrastructure failure'; then
    echo "The build failed for infrastructure reasons; the raw cause is above. This run is not an attempt."
    # The same crash every time is not going away by itself: stop after three in a row.
    local streak=0 file
    for file in $(ls -t "$trial"/gate-*.txt); do
      if tail -n 1 "$file" | grep -qx 'GATE: infrastructure failure'; then streak=$((streak + 1)); else break; fi
    done
    if [ "$streak" -ge 3 ]; then
      set_state blocked
      echo "label    blocked"
      echo "This is infrastructure failure $streak in a row. Do not rerun: hand it to lead, who looks"
      echo "at the machine (SDK, memory, disk) or at whether the change itself crashes the compiler."
    else
      echo "The issue label is unchanged; rerun 'done $number' ($streak in a row; it stops at 3)."
    fi
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
    if [ -f "$trial/pr-body.md" ]; then cat "$trial/pr-body.md"
    elif [ "$kind" = "chore" ]; then echo "$title (#$number). No brief: gated by the build and every existing test."
    else echo "Story $story: ${title#*: }"; fi
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
    if [ "$kind" = "chore" ]; then printf '\nCloses #%s.\n' "$number"; else printf '\nCloses #%s. Part of #%s.\n' "$number" "$parent"; fi
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

# The index row of a slice in docs/briefs/README.md: status, story count, no open findings.
# Adds the row when the slice has none.
index_row() {
  local readme="$1" slice="$2" brief="$3" stories="$4" count title issue route row
  count="$(grep -cE '^## S[0-9]+:' "$stories")"
  if grep -qE "^\| \[$slice " "$readme"; then
    awk -v slice="$slice" -v count="$count" 'BEGIN { FS = OFS = "|" }
      index($0, "| [" slice " ") == 1 { $5 = " approved "; $6 = " [" count "](" slice "/stories.md) "; $7 = " " }
      { print }' "$readme" > "$readme.new" && mv "$readme.new" "$readme"
  else
    title="$(front "$brief" title)"; issue="$(front "$brief" issue)"; route="$(norm_route "$(front "$brief" route)")"
    row="| [$slice $title]($slice/brief.md) | #$issue | \`$route\` | approved | [$count]($slice/stories.md) | | |"
    awk -v row="$row" 'NR == FNR { if ($0 ~ /^\| \[[0-9]+-[0-9]+ /) last = FNR; next }
      { print } FNR == last { print row }' "$readme" "$readme" > "$readme.new" && mv "$readme.new" "$readme"
  fi
}

# Opens the one pull request of a slice's analysis: checks the split and the architect's review,
# sets the status and the index row, and says whether the maintainer needs to read it or can
# merge it as routine. The maintainer's merge is the approval. Run it in the analysis worktree.
cmd_analysis_pr() {
  local slice="${1:-}" look="" branch_name reviewed file open title issue subject body_file url
  is_slice "$slice" || die "usage: analysis-pr <slice> [--look \"<why the maintainer should read it>\"]"
  if [ "${2:-}" = "--look" ]; then look="${3:-}"; [ -n "$look" ] || die "--look needs the reason"
  elif [ -n "${2:-}" ]; then die "unknown option for analysis-pr: $2"; fi
  dir="$here/docs/briefs/$slice"
  [ -f "$dir/brief.md" ] && [ -f "$dir/items.tsv" ] && [ -f "$dir/stories.md" ] || die "docs/briefs/$slice needs brief.md, items.tsv and stories.md"
  branch_name="$(git -C "$here" rev-parse --abbrev-ref HEAD)"
  [ "$branch_name" != "main" ] && [ "$branch_name" != "HEAD" ] || die "run this on the slice's branch (tools/story.sh analysis $slice), not on $branch_name"
  [ -z "$(git -C "$here" status --porcelain)" ] || die "uncommitted changes in $(native "$here"): commit them first"
  run_check "$dir" > /dev/null || die "the story check fails (tools/story.sh check $slice)"

  # The architect's review: no open finding, and nothing but bookkeeping changed after it.
  [ -f "$dir/findings.md" ] || die "no docs/briefs/$slice/findings.md: the architect has not reviewed this slice"
  open="$(grep -c '^- \[ \]' "$dir/findings.md" || true)"
  [ "$open" -eq 0 ] || die "docs/briefs/$slice/findings.md has $open open finding(s)"
  reviewed="$(grep -oE 'Reviewed at commit [0-9a-f]{7,40}' "$dir/findings.md" | tail -n 1 | cut -d' ' -f4)"
  [ -n "$reviewed" ] || die "findings.md has no 'Reviewed at commit <sha>' line"
  git -C "$here" merge-base --is-ancestor "$reviewed" HEAD 2>/dev/null || die "findings.md names commit $reviewed, which is not on this branch"
  for file in $(git -C "$here" diff --name-only "$reviewed" HEAD); do
    case "$file" in
      "docs/briefs/$slice/findings.md"|docs/briefs/README.md) ;;
      "docs/briefs/$slice/brief.md")
        if git -C "$here" diff -U0 "$reviewed" HEAD -- "$file" | grep -E '^[+-][^+-]' | grep -vqE '^[+-]status:'; then
          die "brief.md changed after the architect's review at $reviewed: hand the slice back to the architect"
        fi ;;
      *) die "$file changed after the architect's review at $reviewed: hand the slice back to the architect" ;;
    esac
  done

  # What makes this more than routine for the maintainer.
  git -C "$here" fetch --quiet origin main
  for file in $(git -C "$here" diff --name-only origin/main...HEAD); do
    case "$file" in "docs/briefs/$slice/"*|docs/briefs/README.md) ;; *) look="${look:+$look; }it changes $file, outside the brief" ;; esac
  done
  if grep -qE '^route:[[:space:]]*impl/senior' "$dir/stories.md" || [ "$(front "$dir/brief.md" route)" = "impl/senior" ]; then
    look="${look:+$look; }a story is escalated to impl/senior"
  fi

  title="$(front "$dir/brief.md" title)"; issue="$(front "$dir/brief.md" issue)"
  subject="docs(briefs): brief $slice, $title (#$issue)"
  echo "slice    $slice: $title (part of #$issue)"
  echo "review   no open finding; reviewed at $reviewed"
  echo "stories  $(grep -cE '^## S[0-9]+:' "$dir/stories.md")"
  if [ -n "$look" ]; then echo "for the maintainer: READ IT: $look"; else echo "for the maintainer: routine (brief only, reviewed by the architect)"; fi
  if [ "${AIAKOS_NO_WRITE:-}" = "1" ]; then echo "dry run: would set status approved, update the index row, commit, push and open the pull request"; return; fi

  awk 'NR == 1 && /^---/ { infront = 1; print; next } infront && /^---/ { infront = 0 } infront && /^status:/ { sub(/^status:[[:space:]]*[a-z]+/, "status: approved") } { print }' "$dir/brief.md" > "$dir/brief.md.new" && mv "$dir/brief.md.new" "$dir/brief.md"
  index_row "$here/docs/briefs/README.md" "$slice" "$dir/brief.md" "$dir/stories.md"
  if [ -n "$(git -C "$here" status --porcelain)" ]; then
    git -C "$here" add "docs/briefs/$slice/brief.md" docs/briefs/README.md
    git -C "$here" commit --quiet -m "$subject"
  fi
  git -C "$here" push --quiet -u origin "$branch_name"
  body_file="$(mktemp)"
  {
    if [ -n "$look" ]; then echo "**For the maintainer: read this one.** $look."
    else echo "**For the maintainer: routine.** Only the brief of slice $slice changes, and the architect reviewed it."; fi
    echo
    echo "Analysis of slice $slice: $title. Part of #$issue. Merging this is the approval; the status is already \`approved\` in the brief, so no second pull request follows."
    echo
    echo '```text'
    run_check "$dir"
    echo '```'
    echo
    echo "Story review: no open finding, reviewed at \`$reviewed\` (\`docs/briefs/$slice/findings.md\`)."
  } > "$body_file"
  url="$(gh pr create --repo "$REPO" --base main --head "$branch_name" --title "$subject" --body-file "$body_file")"
  rm -f "$body_file"
  echo "pr       $url"
  echo "Park this on the maintainer with the link. After the merge: tools/story.sh analysis $slice --remove"
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

# Passing work to a seat of the delivery rig (rigs/aiakos-delivery/). A long conversation is
# sent again with every request, so an item is delivered into an empty conversation where that
# can be done: a fresh launch of the seat (rig seat launch --fresh --stop), for a pool seat, the
# architect, the reviewer and the gate. OpenRig then sends the seat its startup text again. A
# busy seat is never touched; its item is queued and the log says that nothing was cleared.
# The lead is never cleared: the maintainer talks to it.
RIG="${AIAKOS_RIG:-aiakos-delivery}"
POOL_LOW="team-low1 team-low2"
POOL_HIGH="team-high1 team-high2"

# "<seat> idle|busy <open items>" for every seat of the rig that is running and started.
rig_seats() {
  rig ps --nodes --rig "$RIG" --json 2>/dev/null | node -e '
    let s = ""; process.stdin.on("data", d => s += d).on("end", () => {
      let nodes; try { nodes = JSON.parse(s); } catch { process.exit(1); }
      for (const n of nodes) {
        if (n.sessionStatus !== "running" || n.startupStatus !== "ready") continue;
        const idle = n.agentActivity && n.agentActivity.state === "idle" ? "idle" : "busy";
        console.log(n.canonicalSessionName, idle, (n.assignedWorkCount || 0) + (n.pendingWorkCount || 0));
      }
    });'
}

# How many items a seat has in progress; prints nothing when that cannot be read.
in_progress() {
  rig queue list --destination "$1" --state in-progress --limit 1000 --json 2>/dev/null | node -e '
    let s = ""; process.stdin.on("data", d => s += d).on("end", () => {
      let rows; try { rows = JSON.parse(s); } catch { process.exit(1); }
      if (!Array.isArray(rows)) process.exit(1);
      console.log(rows.length);
    });'
}

cmd_hand() {
  local usage='usage: story.sh hand <low|high|architect|reviewer|gate|lead> [--role <role>] [--item <qitem>] --summary "<one line>" (--body "<text>" | --body-file <path>) [--evidence <path or link>]'
  local target="${1:-}" role="" item="" summary="" body="" body_file="" evidence=""
  [ $# -eq 0 ] || shift
  while [ $# -gt 0 ]; do
    case "$1" in
      --role)      role="${2:-}"; shift 2 ;;
      --item)      item="${2:-}"; shift 2 ;;
      --summary)   summary="${2:-}"; shift 2 ;;
      --body)      body="${2:-}"; shift 2 ;;
      --body-file) body_file="${2:-}"; shift 2 ;;
      --evidence)  evidence="${2:-}"; shift 2 ;;
      *)           die "$usage" ;;
    esac
  done
  [ -n "$target" ] && [ -n "$summary" ] || die "$usage"
  if [ -n "$body_file" ]; then
    [ -z "$body" ] || die "hand: give --body or --body-file, not both"
    [ -f "$body_file" ] || die "hand: no file $body_file"
  else
    [ -n "$body" ] || die "hand: give --body or --body-file"
  fi

  # clear: whether the destination's conversation can be emptied ("fresh") or not ("no").
  local pool="" dest="" clear="no" roles="$main_root/rigs/aiakos-delivery/roles"
  case "$target" in
    low)        pool="$POOL_LOW"; clear="fresh" ;;
    high)       pool="$POOL_HIGH"; clear="fresh" ;;
    architect)  dest="team-architect@$RIG"; clear="fresh" ;;
    reviewer)   dest="team-reviewer@$RIG"; clear="fresh" ;;
    gate)       dest="team-gate@$RIG"; clear="fresh" ;;
    lead)       dest="desk-lead@$RIG" ;;
    *)          die "$usage" ;;
  esac
  if [ -n "$pool" ]; then
    [ -n "$role" ] && [ -f "$roles/$role.md" ] || die "hand $target needs --role, one of: $(ls "$roles" 2>/dev/null | sed 's/\.md$//' | tr '\n' ' ')"
  else
    [ -z "$role" ] || die "hand: --role is only for the pools low and high"
  fi
  command -v rig >/dev/null 2>&1 || die "hand: rig is not on the PATH"
  command -v node >/dev/null 2>&1 || die "hand: node is not on the PATH"

  local table="" seat line state open best="" best_open="" count="" kept=""
  table="$(rig_seats)" || true
  [ -n "$table" ] || die "hand: cannot read the seats of rig $RIG. Is it running?"
  if [ -n "$pool" ]; then
    # An idle seat with nothing open, else the seat with the fewest open items.
    for seat in $pool; do
      line="$(printf '%s\n' "$table" | awk -v s="$seat@$RIG" '$1 == s { print $2, $3 }')"
      [ -n "$line" ] || continue
      state="${line% *}"; open="${line#* }"
      if [ "$state" = "idle" ] && [ "$open" = "0" ]; then dest="$seat@$RIG"; break; fi
      if [ -z "$best" ] || [ "$open" -lt "$best_open" ]; then best="$seat@$RIG"; best_open="$open"; fi
    done
    [ -n "$dest" ] || dest="$best"
    [ -n "$dest" ] || die "hand: no seat of pool $target is running"
  fi
  printf '%s\n' "$table" | awk -v s="$dest" '$1 == s { found = 1 } END { exit !found }' \
    || die "hand: $dest is not running; nothing was handed over. See: rig ps --nodes --rig $RIG"
  if [ "$clear" != "no" ]; then
    state="$(printf '%s\n' "$table" | awk -v s="$dest" '$1 == s { print $2 }')"
    count="$(in_progress "$dest")" || true
    if [ -z "$state" ]; then die "hand: $dest is not running; see: rig ps --nodes --rig $RIG"
    elif [ "$state" != "idle" ]; then clear="no"; kept="it is busy"
    elif [ -z "$count" ]; then clear="no"; kept="its queue could not be read"
    elif [ "$count" != "0" ]; then clear="no"; kept="it has an item in progress"
    fi
  fi

  local text
  text="$(mktemp)"
  if [ -n "$pool" ]; then
    printf 'Role for this item: read %s before anything else.\n\n' "$roles/$role.md" > "$text"
  fi
  if [ -n "$body_file" ]; then cat "$body_file" >> "$text"; else printf '%s\n' "$body" >> "$text"; fi

  if [ "${AIAKOS_NO_WRITE:-}" = "1" ]; then
    echo "would hand to $dest (conversation: $clear${kept:+, kept because $kept}):"
    sed 's/^/  /' "$text"
    rm -f "$text"
    return
  fi

  if [ "$clear" = "fresh" ]; then
    rig seat launch "$dest" --fresh --stop --reason "clean conversation for the next item" >/dev/null 2>&1 \
      || { rm -f "$text"; die "hand: could not start a fresh conversation in $dest; nothing was handed over. See: rig ps --nodes --rig $RIG"; }
  fi

  local args=(--summary "$summary" --body-file "$text")
  [ -z "$evidence" ] || args+=(--evidence-ref "$evidence")
  if [ -n "$item" ]; then
    rig queue handoff "$item" --to "$dest" "${args[@]}" >/dev/null 2>"$text.err" \
      || { cat "$text.err" >&2; rm -f "$text" "$text.err"; die "hand: rig queue handoff failed; $item is still yours"; }
  else
    rig queue create --destination "$dest" "${args[@]}" >/dev/null 2>"$text.err" \
      || { cat "$text.err" >&2; rm -f "$text" "$text.err"; die "hand: rig queue create failed"; }
  fi
  rm -f "$text" "$text.err"

  mkdir -p "$main_root/artifacts"
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "${OPENRIG_SESSION_NAME:-shell}" "$target" "$dest" "${role:--}" "$clear${kept:+ ($kept)}" "$summary" >> "$main_root/artifacts/hand.log"
  echo "handed to $dest (conversation: $clear${kept:+, kept because $kept})"
}

# What changed on GitHub since the last look: a merged pull request, or a chore or bug that
# the maintainer labelled ready. Nothing reports these to the rig, so the lead runs this at
# the start of each move and acts on what it prints. Each change is printed once. The first
# run only records what exists.
cmd_events() {
  local seen="$main_root/artifacts/events.seen" now fresh
  mkdir -p "$main_root/artifacts"
  now="$(mktemp)"; fresh="$(mktemp)"
  if ! gh pr list --repo "$REPO" --state merged --limit 40 --json number,title,headRefName \
      --jq '.[] | "merged:\(.number)\tPull request #\(.number) was merged: \(.title) (branch \(.headRefName))"' > "$now"; then
    rm -f "$now" "$fresh"; die "events: cannot read the pull requests of $REPO"
  fi
  if ! gh issue list --repo "$REPO" --state open --label ready --limit 100 --json number,title,labels \
      --jq '.[] | select((.title | test("^[0-9]+-[0-9]+(-[0-9]+)?: ") | not) and ([.labels[].name] | any(. == "type/chore" or . == "type/bug"))) | "ready:\(.number)\tIssue #\(.number) (chore or bug) is labelled ready: \(.title)"' >> "$now"; then
    rm -f "$now" "$fresh"; die "events: cannot read the issues of $REPO"
  fi
  if [ ! -f "$seen" ]; then
    cut -f1 "$now" > "$seen"
    echo "events: first look, $(wc -l < "$seen" | tr -d ' ') recorded; nothing new"
    rm -f "$now" "$fresh"
    return
  fi
  awk -F'\t' 'NR == FNR { s[$1]; next } !($1 in s)' "$seen" "$now" > "$fresh"
  if [ -s "$fresh" ]; then
    echo "events: changed on GitHub since the last look:"
    cut -f2 "$fresh" | sed 's/^/- /'
    [ "${AIAKOS_NO_WRITE:-}" = "1" ] || cut -f1 "$fresh" >> "$seen"
  else
    echo "events: nothing new"
  fi
  rm -f "$now" "$fresh"
}

# The allowances of the two subscriptions, as the harnesses last reported them: Claude Code to
# OpenRig (one file per Claude seat), Codex in its session log. "--every <minutes>" prints only
# when that long has passed since the last print, or when a window is at 85% or more, so the
# lead can run it at every move and pass the line on about twice an hour.
cmd_allowance() {
  local every="" last="$main_root/artifacts/allowance.last" line
  if [ "${1:-}" = "--every" ]; then every="${2:-}"; [[ "$every" =~ ^[0-9]+$ ]] || die "usage: story.sh allowance [--every <minutes>]"; fi
  command -v node >/dev/null 2>&1 || die "allowance: node is not on the PATH"
  line="$(RIG="$RIG" OPENRIG_STATE="${OPENRIG_HOME:-$HOME/.openrig}/state/provider-usage" CODEX_SESSIONS="${CODEX_HOME:-$HOME/.codex}/sessions" node -e '
    const fs = require("fs"), path = require("path");
    const at = d => d.toLocaleString("en-GB", { weekday: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
    const win = (name, used, resets) => used == null ? name + " unknown" : name + " " + Math.round(used) + "%" + (resets ? " (resets " + at(resets) + ")" : "");
    let worst = 0; const note = v => { if (typeof v === "number" && v > worst) worst = v; };
    // Claude: the newest report of any Claude seat of this rig.
    let claude = "Claude: unknown";
    try {
      const dir = process.env.OPENRIG_STATE;
      const files = fs.readdirSync(dir).filter(f => f.endsWith("@" + process.env.RIG + ".json")).map(f => path.join(dir, f)).sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);
      for (const f of files) {
        const r = (JSON.parse(fs.readFileSync(f, "utf8")).rateLimits) || {};
        if (!r.five_hour && !r.seven_day) continue;
        const f5 = r.five_hour || {}, w = r.seven_day || {};
        note(f5.usedPercent); note(w.usedPercent);
        claude = "Claude: " + win("5h", f5.usedPercent, f5.resetsAt && new Date(f5.resetsAt)) + ", " + win("week", w.usedPercent, w.resetsAt && new Date(w.resetsAt)) + ", as of " + at(new Date(fs.statSync(f).mtimeMs));
        break;
      }
    } catch {}
    // Codex: the last rate limits in the newest session log.
    let codex = "Codex: unknown";
    try {
      const logs = [];
      const walk = d => { for (const e of fs.readdirSync(d, { withFileTypes: true })) { const p = path.join(d, e.name); if (e.isDirectory()) walk(p); else if (p.endsWith(".jsonl")) logs.push([fs.statSync(p).mtimeMs, p]); } };
      walk(process.env.CODEX_SESSIONS);
      logs.sort((a, b) => b[0] - a[0]);
      for (const [, p] of logs.slice(0, 8)) {
        const lines = fs.readFileSync(p, "utf8").split("\n");
        let found = null;
        for (let i = lines.length - 1; i >= 0 && !found; i--) {
          if (!lines[i].includes("\"rate_limits\"")) continue;
          try { const o = JSON.parse(lines[i]); const r = o.payload && o.payload.rate_limits; if (r && r.primary) found = [r, o.timestamp]; } catch {}
        }
        if (!found) continue;
        const [r, ts] = found, p1 = r.primary || {}, p2 = r.secondary || {};
        note(p1.used_percent); note(p2.used_percent);
        codex = "Codex: " + win("5h", p1.used_percent, p1.resets_at && new Date(p1.resets_at * 1000)) + ", " + win("week", p2.used_percent, p2.resets_at && new Date(p2.resets_at * 1000)) + ", as of " + at(new Date(ts));
        break;
      }
    } catch {}
    console.log(worst + "\t" + (worst >= 85 ? "LOW ALLOWANCE. " : "Allowances. ") + claude + ". " + codex + ".");
  ')" || die "allowance: could not read the allowances"
  if [ -n "$every" ] && [ "${line%%$'\t'*}" -lt 85 ] 2>/dev/null && [ -f "$last" ] \
      && [ $(( $(date +%s) - $(cat "$last") )) -lt $(( every * 60 )) ]; then
    return
  fi
  mkdir -p "$main_root/artifacts"
  [ -z "$every" ] || date +%s > "$last"
  printf '%s\n' "${line#*$'\t'}"
}

case "${1:-}" in
  check)      cmd_check "${2:-}" ;;
  split)      cmd_split "${2:-}" ;;
  split-done) cmd_split_done "${2:-}" ;;
  show)       cmd_show "${2:-}" "${3:-}" ;;
  status)     cmd_status ;;
  hand)       shift; cmd_hand "$@" ;;
  events)     cmd_events ;;
  allowance)  cmd_allowance "${2:-}" "${3:-}" ;;
  ready)      cmd_ready "${2:-}" "${3:-}" "${4:-}" ;;
  next)       cmd_next "${2:-}" ;;
  start)      cmd_start "${2:-}" "${3:-}" ;;
  analysis)   cmd_analysis "${2:-}" "${3:-}" ;;
  analysis-pr) cmd_analysis_pr "${2:-}" "${3:-}" "${4:-}" ;;
  baseline)   cmd_baseline "${2:-}" "${3:-}" ;;
  done)       cmd_done "${2:-}" ;;
  waive)      cmd_waive "${2:-}" "${3:-}" "${4:-}" "${5:-}" ;;
  pr)         cmd_pr "${2:-}" "${3:-}" "${4:-}" ;;
  cleanup)    cmd_cleanup "${2:-}" ;;
  *)          awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 2 ;;
esac
