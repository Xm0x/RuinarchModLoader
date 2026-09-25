#!/usr/bin/env bash
# Launch Ruinarch through Steam with the RuinarchDebug test harness armed, wait for it to
# finish (it quits the game itself), and print the result log. Requires Steam running and
# the RuinarchDebug mod deployed.
#
# Usage: tools/run-autotest.sh [timeout-seconds] [suites]   (default 1800; all suites)
#   suites: comma-separated harness suite names to run alone, e.g. FamineSuite,TradeSuite
set -uo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

APPID=909320
timeout="${1:-1800}"
suites="${2:-}"
mods="$RUIN_GAME_DIR/Mods/RuinarchDebug"
log="$mods/autotest.log"

[ -d "$mods" ] || { echo "RuinarchDebug is not deployed at $mods" >&2; exit 2; }
pgrep -f 'Ruinarch.exe' >/dev/null && { echo "Ruinarch is already running; close it first" >&2; exit 2; }

# The harness keeps the previous run's log (in logs/) and starts a new autotest.log; only a
# log written after this marker belongs to this run.
marker="$(mktemp)"
trap 'rm -f "$marker"' EXIT
fresh() { [ -f "$log" ] && [ "$log" -nt "$marker" ]; }
sleep 1
printf '%s' "$suites" > "$mods/autotest.flag"
steam "steam://rungameid/$APPID" >/dev/null 2>&1 &

# A world that stops (nothing written to mods.log or autotest.log for 5 minutes; a running
# world logs every few seconds) is a hang: note whether the game's threads are busy (a loop)
# or idle (a deadlock or a dead main thread), then stop instead of waiting out the timeout.
modslog="$RUIN_GAME_DIR/Mods/mods.log"
silent() { [ -f "$1" ] && [ $(( $(date +%s) - $(stat -c %Y "$1") )) -ge 300 ]; }
stalled() { fresh && silent "$log" && silent "$modslog"; }
started=0
for ((t = 0; t < timeout; t += 5)); do
  sleep 5
  if pgrep -f 'Ruinarch.exe' >/dev/null; then
    started=1
  elif [ "$started" = 1 ]; then
    break  # the game exited
  fi
  fresh && grep -q 'AUTOTEST DONE' "$log" 2>/dev/null && { sleep 5; break; }
  if stalled; then
    echo "(stalled: mods.log silent for 5 minutes; busiest game threads:)"
    for pid in $(pgrep -f 'Ruinarch.exe'); do ps -L -o tid,stat,pcpu,time,comm -p "$pid" --sort=-pcpu | head -6; done
    break
  fi
done

# Under Proton the game hangs in Application.Quit (window up, main thread gone), so a
# finished run usually still needs stopping.
if pgrep -f 'Ruinarch.exe' >/dev/null; then
  if fresh && grep -q 'AUTOTEST DONE' "$log" 2>/dev/null; then echo "(run done; stopping the game, which hangs on quit)"
  else echo "(timeout: stopping the game)"; fi
  pkill -f 'Ruinarch.exe'
  # Gone before returning, so the next run does not find it still running.
  for ((w = 0; w < 30 && $(pgrep -fc 'Ruinarch.exe') > 0; w++)); do sleep 1; done
  pgrep -f 'Ruinarch.exe' >/dev/null && pkill -9 -f 'Ruinarch.exe'
fi
rm -f "$mods/autotest.flag"
if fresh; then cat "$log"; else echo "no autotest.log produced (harness never ran)"; fi
fresh && grep -q 'AUTOTEST DONE .* fail=0 ' "$log" 2>/dev/null
