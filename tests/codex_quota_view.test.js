process.env.TZ = 'Asia/Shanghai'
const test = require('node:test')
const assert = require('node:assert')
const { view } = require('../app/web/codex-quota.js')

const NOW = Date.UTC(2026, 9, 12, 4, 0, 0) / 1000   // 2026-10-12 12:00 北京时间

test('服务返回（camelCase）两个窗口', () => {
  const v = view({ source: 'live', rateLimits: {
    primary: { usedPercent: 3, windowDurationMins: 300, resetsAt: NOW + 3600 },
    secondary: { usedPercent: 40, windowDurationMins: 10080, resetsAt: NOW + 3 * 86400 } } }, NOW)
  assert.strictEqual(v.text, '5小时 剩余 97% ｜ 本周 剩余 60% · 10/15 重置')
  assert.strictEqual(v.low, false)
  assert.deepStrictEqual(v.windows, [{ label: '5小时', used: 3, reset: NOW + 3600 }, { label: '本周', used: 40, reset: NOW + 3 * 86400 }])
  assert.strictEqual(v.source, 'live')
})

test('记录（snake_case）带「本机记录」', () => {
  const v = view({ source: 'records', rateLimits: {
    primary: { used_percent: 91, window_minutes: 300, resets_at: NOW + 1800 }, secondary: null } }, NOW)
  assert.strictEqual(v.text, '5小时 剩余 9% · 12:30 重置 · 本机记录')
  assert.strictEqual(v.low, true)
})

test('过了重置时间的窗口显示 100%，不参与 low', () => {
  const v = view({ source: 'records', rateLimits: {
    primary: { used_percent: 95, window_minutes: 300, resets_at: NOW - 10 },
    secondary: { used_percent: 20, window_minutes: 10080, resets_at: NOW + 86400 * 2 } } }, NOW)
  assert.strictEqual(v.text, '5小时 剩余 100% ｜ 本周 剩余 80% · 10/14 重置 · 本机记录')
  assert.strictEqual(v.low, false)
})

test('小数百分比四舍五入；只有一个窗口', () => {
  const v = view({ source: 'live', rateLimits: { primary: { usedPercent: 12.6, windowDurationMins: 10080, resetsAt: NOW + 86400 * 5 } } }, NOW)
  assert.strictEqual(v.text, '本周 剩余 87% · 10/17 重置')
})

test('没有可用窗口 / 空 payload 返回 null', () => {
  assert.strictEqual(view(null, NOW), null)
  assert.strictEqual(view({ source: 'live', rateLimits: { primary: null, secondary: null } }, NOW), null)
  assert.strictEqual(view({ source: 'live', rateLimits: { primary: { windowDurationMins: 300 } } }, NOW), null)
})

test('「今日」窗口标签', () => {
  const v = view({ source: 'live', rateLimits: { primary: { usedPercent: 0, windowDurationMins: 1440, resetsAt: 0 } } }, NOW)
  assert.strictEqual(v.text, '今日 剩余 100%')
})
