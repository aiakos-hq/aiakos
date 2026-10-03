#!/usr/bin/env bash
# Traceability check for a brief that was split into stories (see docs/workflow.md).
#
#   tools/story-check.sh <items.tsv> <stories.md> [<brief.md>]
#
# items.tsv   the closed list of the brief's items, tab-separated: id, type, scope, needs.
#             type   rule | change | output | test
#             scope  once  = exactly one story has it
#                    first = the first story has it
#                    all   = applies to every story; it is carried into each one and is not
#                            listed in stories.md
#             needs  ids that must be in the same story or in a story it depends on ("-" = none)
# stories.md  one block per story:
#
#               ## S1: <title>
#               goal: <one or two sentences>
#               depends: -            (or: S1, S2; only earlier stories)
#               owns: C5, R7          (rules and changes this story implements)
#               outputs: AIK3003-git  (expected outputs this story must produce)
#               tests: -              (other tests this story adds)
#               notes: <what ties this story to another one, or "none">
#               route: impl/senior    (optional; overrides the brief's route)
#               escalation: <why>     (required with route impl/senior: why it cannot be split)
#
#             Each key is one line. Lists are comma-separated; "-" is an empty list.
# brief.md    optional. With it, the check also verifies that the brief is closed: every item
#             is marked in the brief, every mark is in items.tsv, and every rule is exercised
#             by at least one output or test.
#
# Checks: every item is in the stories its scope asks for, under the key of its type; no story
# names an item that is not in items.tsv; "depends" names earlier stories only; whatever an
# item needs is in the same story or in a story it depends on; every story owns at least one
# rule or change, at most MAX_RULES rules and at most MAX_OUTPUTS outputs (default 8 each; a brief
# can set max_rules and max_outputs in its front matter). Exit code 0 when all checks pass, 1
# otherwise.
set -euo pipefail

[ $# -eq 2 ] || [ $# -eq 3 ] || { sed -n '2,34p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
for file in "$@"; do
  [ -f "$file" ] || { echo "story-check: no such file: $file" >&2; exit 2; }
done

awk -v env_rules="${MAX_RULES:-}" -v env_outputs="${MAX_OUTPUTS:-}" -v with_brief="$(( $# == 3 ))" '
function trim(s) { gsub(/^[ \t\r]+|[ \t\r]+$/, "", s); return s }
function fail(message) { errors[++error_count] = message }
function split_list(text, out,    n, i, parts, count) {
  delete out
  text = trim(text)
  if (text == "" || text == "-") return 0
  n = split(text, parts, ",")
  count = 0
  for (i = 1; i <= n; i++) if (trim(parts[i]) != "") out[++count] = trim(parts[i])
  return count
}
# The id an item mark on this line names, or "" (marks: "ID. ", "- ID. ", "| `ID` |").
function mark_of(line,    m) {
  if (match(line, /^(- )?[A-Za-z][A-Za-z0-9_.-]*\. /)) {
    m = substr(line, 1, RLENGTH - 2); sub(/^- /, "", m); return m
  }
  if (match(line, /^\| `[^`]+` \|/)) return substr(line, 4, RLENGTH - 6)
  return ""
}

FNR == 1 { file_number++ }

# First file: items.tsv
file_number == 1 {
  sub(/\r$/, "")
  if ($0 ~ /^#/ || trim($0) == "") next
  n = split($0, f, "\t")
  if (n < 4 || f[2] !~ /^(rule|change|output|test)$/ || f[3] !~ /^(once|first|all)$/) {
    fail("items.tsv line " FNR ": expected id<TAB>rule|change|output|test<TAB>once|first|all<TAB>needs"); next
  }
  if (f[1] in type) { fail("items.tsv line " FNR ": " f[1] " listed twice"); next }
  type[f[1]] = f[2]; scope[f[1]] = f[3]; needs[f[1]] = f[4]; item_order[++item_count] = f[1]
  next
}

# Second file: stories.md
file_number == 2 { sub(/\r$/, "") }
file_number == 2 && /^## / {
  if (match($0, /^## S[0-9]+: */)) {
    id = substr($0, 4, RLENGTH - 4); sub(/:.*/, "", id)
    if (id in story_index) fail(id ": defined twice")
    story_order[++story_count] = id; story_index[id] = story_count
    if (id != "S" story_count) fail(id ": stories are numbered S1, S2, ... in order; expected S" story_count)
    title[id] = trim(substr($0, RLENGTH + 1))
    current = id
  } else {
    fail("stories.md line " FNR ": a story heading must look like \"## S1: <title>\"")
    current = ""
  }
  next
}
file_number == 2 && current != "" && match($0, /^[a-z_]+:/) {
  key = substr($0, 1, RLENGTH - 1)
  if (key !~ /^(goal|depends|owns|outputs|tests|notes|route|escalation)$/) { fail(current ": unknown key \"" key ":\""); next }
  if ((current, key) in field) fail(current ": \"" key ":\" given twice")
  field[current, key] = trim(substr($0, RLENGTH + 1))
  next
}
file_number == 2 { next }

# Third file: brief.md. Its front matter may set max_rules and max_outputs.
file_number == 3 {
  sub(/\r$/, "")
  if (FNR == 1 && /^---/) { in_front = 1; next }
  if (in_front) {
    if (/^---/) in_front = 0
    else if ($0 ~ /^max_rules:[ \t]*[0-9]+/) brief_rules = substr($0, 11) + 0
    else if ($0 ~ /^max_outputs:[ \t]*[0-9]+/) brief_outputs = substr($0, 13) + 0
    next
  }
  m = mark_of($0)
  if (m == "") next
  if (m in type) marked[m] = 1
  else if ($0 !~ /^\|/ && m ~ /^[A-Z]+[0-9]+[a-z]?$/) fail("brief line " FNR ": \"" m "\" is marked like an item but is not in items.tsv")
  next
}

END {
  max_rules = env_rules != "" ? env_rules + 0 : (brief_rules ? brief_rules : 8)
  max_outputs = env_outputs != "" ? env_outputs + 0 : (brief_outputs ? brief_outputs : 8)
  if (story_count == 0) fail("no stories found")
  split("goal depends owns outputs tests notes", required, " ")
  key_of["rule"] = "owns"; key_of["change"] = "owns"; key_of["output"] = "outputs"; key_of["test"] = "tests"

  for (s = 1; s <= story_count; s++) {
    id = story_order[s]
    for (k = 1; k in required; k++) {
      if (!((id, required[k]) in field)) fail(id ": missing \"" required[k] ":\"")
      else if (field[id, required[k]] == "") fail(id ": \"" required[k] ":\" is empty (write \"-\" for an empty list)")
    }
    if (title[id] == "") fail(id ": no title")
    if (field[id, "route"] == "impl/senior" && field[id, "escalation"] == "") fail(id ": route impl/senior needs \"escalation:\" with the reason it cannot be split")
    if (field[id, "escalation"] != "" && field[id, "route"] != "impl/senior") fail(id ": \"escalation:\" is only for a story with \"route: impl/senior\"")

    # depends: earlier stories only, which also rules out cycles
    count = split_list(field[id, "depends"], list)
    for (i = 1; i <= count; i++) {
      d = list[i]
      if (!(d in story_index)) fail(id ": depends on unknown story " d)
      else if (story_index[d] >= s) fail(id ": depends on " d ", which is not an earlier story")
      else {
        reach[id, d] = 1
        for (t = 1; t < s; t++) if ((d, story_order[t]) in reach) reach[id, story_order[t]] = 1
      }
    }

    # items of this story
    rules[id] = 0; changes[id] = 0; outputs[id] = 0; tests[id] = 0
    split("owns outputs tests", keys, " ")
    for (k = 1; k in keys; k++) {
      count = split_list(field[id, keys[k]], list)
      for (i = 1; i <= count; i++) {
        item = list[i]
        if (!(item in type)) { fail(id ": \"" item "\" is not an item of the brief (see items.tsv)"); continue }
        if (scope[item] == "all") { fail(id ": " item " applies to every story and is not listed"); continue }
        if (key_of[type[item]] != keys[k]) { fail(id ": " item " has type " type[item] " and belongs under \"" key_of[type[item]] ":\""); continue }
        if ((id, item) in has) { fail(id ": " item " listed twice"); continue }
        has[id, item] = 1
        owners[item] = (item in owners) ? owners[item] ", " id : id
        owner_count[item]++
        if (!(item in owner)) owner[item] = id
        if (type[item] == "rule") rules[id]++
        else if (type[item] == "change") changes[id]++
        else if (type[item] == "output") outputs[id]++
        else tests[id]++
      }
    }
    if (rules[id] + changes[id] == 0) fail(id ": owns no rule or change; merge it into another story")
    if (rules[id] > max_rules) fail(id ": owns " rules[id] " rules, the cap is " max_rules "; split it further")
    if (outputs[id] > max_outputs) fail(id ": has " outputs[id] " outputs, the cap is " max_outputs "; split it further")
  }

  # coverage
  for (n = 1; n <= item_count; n++) {
    item = item_order[n]
    if (scope[item] == "all") continue
    if (!(item in owner_count)) fail(item ": in no story")
    else if (owner_count[item] > 1) fail(item ": in more than one story (" owners[item] ")")
    else if (scope[item] == "first" && owner[item] != story_order[1]) fail(item ": must be in " story_order[1] ", found in " owner[item])
  }

  # needs: same story or a story it depends on
  for (n = 1; n <= item_count; n++) {
    item = item_order[n]
    count = split_list(needs[item], list)
    for (i = 1; i <= count; i++) {
      need = list[i]
      if (!(need in type)) { fail("items.tsv: " item " needs unknown item " need); continue }
      if (type[need] == "rule") exercised[need] = 1
      if (scope[item] == "all" || scope[need] == "all" || !(item in owner) || !(need in owner)) continue
      id = owner[item]; o = owner[need]
      if (o != id && !((id, o) in reach)) fail(id ": " item " needs " need ", which is in " o "; move it or add " o " to \"depends:\"")
    }
  }

  # the brief is closed
  if (with_brief) {
    for (n = 1; n <= item_count; n++) {
      item = item_order[n]
      if (!(item in marked)) fail("brief: " item " is in items.tsv but not marked in the brief")
      if (type[item] == "rule" && scope[item] != "all" && !(item in exercised)) fail("items.tsv: rule " item " has no output or test that needs it")
    }
  }

  printf "%-5s %5s %7s %7s %5s  %-12s %s\n", "story", "rules", "changes", "outputs", "tests", "depends", "title"
  for (s = 1; s <= story_count; s++) {
    id = story_order[s]
    printf "%-5s %5d %7d %7d %5d  %-12s %s\n", id, rules[id], changes[id], outputs[id], tests[id], ((id, "depends") in field ? field[id, "depends"] : "?"), title[id]
  }
  print ""
  if (error_count == 0) { print "story-check: ok (" story_count " stories, " item_count " items)"; exit 0 }
  for (e = 1; e <= error_count; e++) print "error: " errors[e]
  print "story-check: " error_count " error(s)"
  exit 1
}
' "$@"
