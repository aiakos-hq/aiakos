#!/usr/bin/env bash
# The board of the delivery rig: what waits for the maintainer and what the team is doing.
# Once a minute it also tells the router what changed on GitHub (tools/story.sh events),
# because nothing else does. It uses no model and changes nothing but that one queue item.
# The rig starts it in the seat desk-board (rig.yaml). By hand, from anywhere:
#   bash rigs/aiakos-delivery/board.sh           refresh until Ctrl+C
#   bash rigs/aiakos-delivery/board.sh --once    print once, without looking at GitHub changes
set -u

RIG="${AIAKOS_RIG:-aiakos-delivery}"
REPO="${AIAKOS_REPO:-aiakos-hq/aiakos}"
MAINTAINER="${AIAKOS_MAINTAINER:-operator-human@kernel}"
INTERVAL="${INTERVAL:-60}"
# OpenRig takes the caller from the environment; a plain shell must say who it is.
export OPENRIG_SESSION_NAME="${OPENRIG_SESSION_NAME:-$MAINTAINER}"
export AIAKOS_RIG="$RIG" AIAKOS_REPO="$REPO" AIAKOS_MAINTAINER="$MAINTAINER"

cd "$(dirname "${BASH_SOURCE[0]}")/../.." || exit 1
for tool in rig node gh; do
  command -v "$tool" >/dev/null 2>&1 || { echo "board: $tool is not on the PATH" >&2; exit 1; }
done

render() { # $1: the line of the last look at GitHub
  echo "$RIG   $(date +%H:%M)   every ${INTERVAL}s   answer in desk-lead"
  echo
  rig queue list --destination "$MAINTAINER" --state pending,in-progress,blocked --full --limit 200 --json 2>/dev/null | RIG="$RIG" node -e '
    let s = ""; process.stdin.on("data", d => s += d).on("end", () => {
      let rows; try { rows = JSON.parse(s); } catch { console.log("WAITING FOR YOU   unknown: the queue could not be read"); return; }
      rows = rows.filter(r => (r.sourceSession || "").endsWith("@" + process.env.RIG)).sort((a, b) => a.tsCreated < b.tsCreated ? -1 : 1);
      console.log("WAITING FOR YOU   " + rows.length);
      for (const r of rows) {
        const t = new Date(r.tsCreated);
        console.log("  " + String(t.getHours()).padStart(2, "0") + ":" + String(t.getMinutes()).padStart(2, "0") + "  " + (r.summary || "(no summary)"));
        console.log("         look at: " + (r.evidenceRef || "-") + "   item: " + r.qitemId);
      }
    });'
  echo
  echo "OPEN PULL REQUESTS"
  gh pr list --repo "$REPO" --state open --limit 30 --json number,title,isDraft \
    --jq '.[] | "  #\(.number)  \(.title)\(if .isDraft then "  (draft)" else "" end)"' 2>/dev/null | cut -c1-150 \
    || echo "  unknown: GitHub could not be read"
  echo
  echo "SEATS"
  rig ps --nodes --rig "$RIG" --json 2>/dev/null | node -e '
    let s = ""; process.stdin.on("data", d => s += d).on("end", () => {
      let nodes; try { nodes = JSON.parse(s); } catch { console.log("  unknown: the rig could not be read"); return; }
      for (const n of nodes) {
        if (n.runtime === "terminal") continue;
        const name = n.canonicalSessionName.split("@")[0].padEnd(16);
        const state = n.sessionStatus !== "running" ? n.sessionStatus : (n.agentActivity && n.agentActivity.state) || "unknown";
        const open = (n.assignedWorkCount || 0) + (n.pendingWorkCount || 0);
        const flag = n.lifecycleState && n.lifecycleState !== "running" ? "   " + n.lifecycleState : "";
        console.log("  " + name + String(state).padEnd(10) + (open ? open + " open" : "") + flag);
      }
    });'
  echo
  echo "LAST DELIVERIES"
  if [ -f artifacts/hand.log ]; then
    tail -n 6 artifacts/hand.log | while IFS=$'\t' read -r at from _ to _ cleared summary; do
      printf '  %s  %s -> %s  [%s]  %s\n' "$(date -d "$at" +%H:%M)" "${from%%@*}" "${to%%@*}" "$cleared" "$summary"
    done | cut -c1-150
  else
    echo "  none yet"
  fi
  echo
  echo "GITHUB   $1"
}

if [ "${1:-}" = "--once" ]; then
  render "not looked at (--once)"
  exit 0
fi
while :; do
  github="$(bash tools/story.sh events 2>&1 | tail -n 1)"
  screen="$(render "$github")"
  clear
  printf '%s\n' "$screen"
  sleep "$INTERVAL"
done
