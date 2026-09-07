#!/usr/bin/env bash
# End-to-end smoke test: start the server, attach a client, prove frames arrive.
#
# Runs headlessly, so it works on a CI runner with no terminal. The client
# already supports this: with stdin redirected it becomes a pure observer
# (Console.KeyAvailable throws when there is no keyboard), which is exactly the
# mode needed here.
#
# What this proves on each platform:
#   - the server binds, seeds a pattern, and ticks
#   - a client connects over TCP and decodes frames
#   - the half-block glyph survives the platform's console encoding
#   - the browser bridge serves its page and completes an RFC 6455 handshake
#
# What it cannot prove: that a real terminal *displays* the ANSI escapes
# correctly. That needs an interactive console and a human eye. See
# docs/ai-dlc/bolts/09-cross-platform/plan.md.

set -uo pipefail

PORT="${PORT:-5410}"
WEB_PORT="${WEB_PORT:-5411}"
SECONDS_TO_RUN="${SECONDS_TO_RUN:-6}"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

server_log="$(mktemp)"
client_log="$(mktemp)"
failures=0

cleanup() {
  [ -n "${client_pid:-}" ] && kill "$client_pid" 2>/dev/null
  [ -n "${server_pid:-}" ] && kill "$server_pid" 2>/dev/null
  wait 2>/dev/null
  rm -f "$server_log" "$client_log"
}
trap cleanup EXIT

check() {
  if [ "$1" = "ok" ]; then
    printf '  \033[32mPASS\033[0m  %s\n' "$2"
  else
    printf '  \033[31mFAIL\033[0m  %s\n' "$2"
    failures=$((failures + 1))
  fi
}

echo "Smoke test on $(uname -s) — .NET $(dotnet --version)"
echo

dotnet run --project src/GameOfLife.Server -- \
  --port "$PORT" --web-port "$WEB_PORT" --tick 40 --run > "$server_log" 2>&1 &
server_pid=$!

# Wait for the listener rather than sleeping a fixed amount, so a slow runner
# does not produce a spurious failure.
for _ in $(seq 1 60); do
  grep -q "listening on" "$server_log" && break
  sleep 1
done

grep -q "listening on" "$server_log" \
  && check ok "server started" \
  || { check fail "server started"; cat "$server_log"; exit 1; }

grep -q "Seeded" "$server_log" \
  && check ok "seeded a pattern from patterns/" \
  || check fail "seeded a pattern from patterns/"

dotnet run --project src/GameOfLife.Client.Console -- \
  --port "$PORT" < /dev/null > "$client_log" 2>&1 &
client_pid=$!

sleep "$SECONDS_TO_RUN"
kill "$client_pid" 2>/dev/null
wait "$client_pid" 2>/dev/null
client_pid=""

# The half-block is the load-bearing glyph: if the platform's console encoding
# mangles it, the whole display is wrong and this is where it shows.
if grep -q "▀" "$client_log"; then
  check ok "client rendered half-block glyphs (UTF-8 output works)"
else
  check fail "client rendered half-block glyphs (UTF-8 output works)"
fi

if grep -qE "gen.\[0m [0-9]+" "$client_log"; then
  generations="$(grep -oE "gen.\[0m [0-9]+" "$client_log" | grep -oE "[0-9]+$" | sort -n | tail -1)"
  if [ "${generations:-0}" -gt 0 ]; then
    check ok "simulation advanced to generation $generations"
  else
    check fail "simulation advanced (stuck at generation 0)"
  fi
else
  check fail "client displayed a generation counter"
fi

grep -q "client 1 connected" "$server_log" \
  && check ok "server saw the client connect" \
  || check fail "server saw the client connect"

# Browser bridge: the page, then a real RFC 6455 handshake.
page_status="$(curl -s -o /dev/null -w "%{http_code}" "http://127.0.0.1:$WEB_PORT/" || echo 000)"
[ "$page_status" = "200" ] \
  && check ok "browser client page served (HTTP $page_status)" \
  || check fail "browser client page served (got HTTP $page_status)"

# Read only the response head: what follows the handshake is binary WebSocket
# traffic, which upsets text tools in a non-UTF-8 locale.
handshake="$(curl -s -i -N --max-time 5 \
  -H "Connection: Upgrade" -H "Upgrade: websocket" \
  -H "Sec-WebSocket-Version: 13" -H "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==" \
  "http://127.0.0.1:$WEB_PORT/ws" 2>/dev/null | head -c 400 | LC_ALL=C tr -d '\r')"

# The accept value RFC 6455 section 1.3 publishes for that key.
if printf '%s' "$handshake" | grep -q "s3pPLMBiTxaQ9kYGzzhZRbK+xOo="; then
  check ok "websocket handshake matches the RFC 6455 worked example"
else
  check fail "websocket handshake matches the RFC 6455 worked example"
fi

echo
if [ "$failures" -eq 0 ]; then
  printf '\033[32mAll smoke checks passed.\033[0m\n'
else
  printf '\033[31m%d smoke check(s) failed.\033[0m\n' "$failures"
  echo "--- server log ---"; cat "$server_log"
fi

exit "$failures"
