#!/usr/bin/env bash
# Gives the seats of the aiakos-delivery rig a new, empty conversation between items, so that
# a seat does not carry the context of finished work into every request of the next item.
# Run it in WSL next to the running rig, from anywhere:  bash rigs/aiakos-delivery/fresh.sh
#
#   fresh.sh              watch the rig until stopped (Ctrl+C)
#   fresh.sh --once       look once and exit
#   fresh.sh --dry-run    say what would be done and change nothing (combine with --once)
#
# A seat gets a new conversation (rig seat launch --fresh --stop) when all of this holds:
#   - it is running and started, and this script has seen it idle for QUIET seconds
#     (QUIET_LEAD for the lead, which the maintainer talks to); with --once that needs an
#     earlier look that saw it idle too, unless QUIET=0;
#   - it has no item in progress;
#   - it closed or handed off an item since its last new conversation.
# Anything this script cannot read counts as "do not touch the seat". Pending and parked items
# stay in the queue; the new conversation reads the queue when it starts. A seat cannot do this
# for itself: Claude's auto mode refuses the command.
set -u

RIG="${RIG:-aiakos-delivery}"
INTERVAL="${INTERVAL:-30}"
QUIET="${QUIET:-30}"
QUIET_LEAD="${QUIET_LEAD:-900}"
STATE="${XDG_STATE_HOME:-$HOME/.local/state}/aiakos-rig/$RIG/fresh"
# OpenRig takes the caller from the environment; a plain shell must say who it is.
export OPENRIG_SESSION_NAME="${OPENRIG_SESSION_NAME:-operator-human@kernel}"

once=0
dry=0
for arg in "$@"; do
  case "$arg" in
    --once)    once=1 ;;
    --dry-run) dry=1 ;;
    *)         awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 2 ;;
  esac
done

for tool in rig node; do
  command -v "$tool" >/dev/null 2>&1 || { echo "fresh: $tool is not on the PATH" >&2; exit 1; }
done
mkdir -p "$STATE"

# "<seat> idle|busy" for every seat that is running and started.
seats() {
  rig ps --nodes --rig "$RIG" --json 2>/dev/null | node -e '
    let s = ""; process.stdin.on("data", d => s += d).on("end", () => {
      let nodes; try { nodes = JSON.parse(s); } catch { process.exit(1); }
      for (const n of nodes) {
        if (n.sessionStatus !== "running" || n.startupStatus !== "ready") continue;
        console.log(n.canonicalSessionName, n.agentActivity && n.agentActivity.state === "idle" ? "idle" : "busy");
      }
    });'
}

# "<count> <newest change>" of the seat's items in the given states; nothing when unknown.
items() {
  rig queue list --destination "$1" --state "$2" --limit 1000 --json 2>/dev/null | node -e '
    let s = ""; process.stdin.on("data", d => s += d).on("end", () => {
      let rows; try { rows = JSON.parse(s); } catch { process.exit(1); }
      if (!Array.isArray(rows)) process.exit(1);
      console.log(rows.length, rows.map(r => r.tsUpdated).sort().pop() || "-");
    });'
}

look() {
  local seat activity since need count newest stamp now
  while read -r seat activity; do
    # OpenRig does not say how long a seat has been idle, so this script remembers when it
    # first saw the seat idle and forgets it when the seat works again.
    if [ "$activity" != "idle" ]; then rm -f "$STATE/$seat.idle"; continue; fi
    [ -f "$STATE/$seat.idle" ] || date +%s > "$STATE/$seat.idle"
    since="$(cat "$STATE/$seat.idle")"
    need="$QUIET"
    case "$seat" in lead-*) need="$QUIET_LEAD" ;; esac
    [ $(( $(date +%s) - since )) -ge "$need" ] || continue
    read -r count newest < <(items "$seat" in-progress) || continue
    [ "$count" = "0" ] || continue
    read -r count newest < <(items "$seat" done,handed-off,canceled,failed,denied) || continue
    [ "$count" != "0" ] || continue
    stamp="$(cat "$STATE/$seat" 2>/dev/null || true)"
    [[ "$newest" > "$stamp" ]] || continue
    if [ "$dry" = 1 ]; then
      echo "$(date +%H:%M:%S) would renew $seat (last item closed $newest)"
      continue
    fi
    now="$(date -u +%Y-%m-%dT%H:%M:%S.000Z)"
    if rig seat launch "$seat" --fresh --stop --reason "new conversation between items (fresh.sh)" > /dev/null 2> "$STATE/$seat.err"; then
      printf '%s\n' "$now" > "$STATE/$seat"
      rm -f "$STATE/$seat.idle"
      echo "$(date +%H:%M:%S) renewed $seat"
    else
      echo "$(date +%H:%M:%S) FAILED $seat: $(head -n 1 "$STATE/$seat.err")" >&2
      echo "         the seat may be stopped; start it with: rig seat launch $seat --fresh --reason recover" >&2
    fi
  done < <(seats)
}

if [ "$once" = 1 ]; then
  look
  exit 0
fi
echo "fresh: watching $RIG every ${INTERVAL}s (quiet ${QUIET}s, lead ${QUIET_LEAD}s). Ctrl+C stops."
while :; do
  look
  sleep "$INTERVAL"
done
