#!/bin/sh
# 用假 codex 测 Mac 版问服务的各条路径：先 app/build.sh 编译，再 CROSSPET_CODEX_TIMEOUT=3 tests/codex_quota_probe.sh
set -eu
cd "$(dirname "$0")/.."
BIN=build/CrossPet.app/Contents/MacOS/CrossPet
FAKE="$(pwd)/tests/fake_codex.py"
EMPTY=$(mktemp -d)   # 空的 CODEX_HOME：没有会话记录，失败时应该是 none
fail=0
check() {   # 模式 期望的 source 期望 error 里包含的字（可空）
  out=$(CODEX_HOME="$EMPTY" CROSSPET_CODEX_BIN="$FAKE" FAKE_CODEX_MODE="$1" "$BIN" --codex-quota-probe)
  python3 - "$1" "$2" "$3" "$out" <<'PY' || fail=1
import json, sys
mode, src, err, out = sys.argv[1:5]
p = json.loads(out)
ok = p["source"] == src and (err in (p.get("error") or "")) and "acct-secret" not in out
print(("PASS" if ok else "FAIL"), mode, out[:160])
sys.exit(0 if ok else 1)
PY
}
check ok live ""
check single live ""
check auth none "authentication"
check nomethod none "method not found"
check garbage none "超时"
start=$(date +%s); check hang none "超时"; [ $(( $(date +%s) - start )) -le 8 ] || { echo "FAIL hang 超过 8 秒（超时没生效）"; fail=1; }
out=$(CODEX_HOME="$EMPTY" CROSSPET_CODEX_BIN=/nonexistent/codex "$BIN" --codex-quota-probe)
echo "$out" | grep -q '"source":"none"' && echo "PASS missing-binary" || { echo "FAIL missing-binary $out"; fail=1; }
# 备用来源：服务失败时读会话记录，挑时间戳最新的一条（不是文件最新的那个）
REC=$(mktemp -d); D="$REC/sessions/$(date +%Y/%m/%d)"; mkdir -p "$D"
now=$(date +%s)
printf '{"timestamp":"2026-01-01T00:00:09Z","payload":{"rate_limits":{"primary":{"used_percent":70,"window_minutes":300,"resets_at":%s}}}}\n' $((now+600)) > "$D/rollout-a.jsonl"
printf '{"timestamp":"2026-01-01T00:00:01Z","payload":{"rate_limits":{"primary":{"used_percent":10,"window_minutes":300,"resets_at":%s}}}}\n' $((now+600)) > "$D/rollout-b.jsonl"
touch "$D/rollout-a.jsonl"; sleep 1; touch "$D/rollout-b.jsonl"   # b 文件更新，但记录更旧
out=$(CODEX_HOME="$REC" CROSSPET_CODEX_BIN="$FAKE" FAKE_CODEX_MODE=auth "$BIN" --codex-quota-probe)
echo "$out" | grep -q '"source":"records"' && echo "$out" | grep -q '"used_percent":70' && echo "PASS records-newest-by-timestamp" || { echo "FAIL records $out"; fail=1; }
rm -rf "$REC"
rm -rf "$EMPTY"
exit $fail
