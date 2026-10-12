// GPT（Codex）额度排版：外壳交来原始额度（问 Codex 服务得到的 camelCase，或会话记录里的 snake_case），
// 这里统一成名牌下面那一行。两个平台共用这一份，规则只写一遍
;(function (root) {
  const pad = n => String(n).padStart(2, '0')
  const label = mins => mins >= 7 * 24 * 60 ? '本周' : mins >= 24 * 60 ? '今日' : `${Math.max(1, Math.floor(mins / 60))}小时`
  function windowsOf(rl) {
    const out = []
    for (const key of ['primary', 'secondary']) {
      const w = rl && rl[key]
      if (!w) continue
      const used = w.usedPercent ?? w.used_percent
      if (typeof used !== 'number') continue
      const mins = w.windowDurationMins ?? w.window_minutes ?? 0
      const reset = w.resetsAt ?? w.resets_at
      out.push({ label: label(mins), used, reset: typeof reset === 'number' ? reset : 0 })
    }
    return out
  }
  function when(sec, nowSec) {
    const d = new Date(sec * 1000)
    return sec - nowSec < 86400 ? `${pad(d.getHours())}:${pad(d.getMinutes())}` : `${d.getMonth() + 1}/${d.getDate()}`
  }
  function view(p, nowSec) {
    if (!p || !p.rateLimits) return null
    const windows = windowsOf(p.rateLimits)
    if (!windows.length) return null
    // 过了重置时间、又没有新数据的窗口现在是满的，不算「快用完」
    const live = windows.filter(w => !w.reset || w.reset > nowSec)
    const maxUsed = live.length ? Math.max(...live.map(w => w.used)) : 0
    let marked = false
    const parts = windows.map(w => {
      if (w.reset > 0 && w.reset <= nowSec) return `${w.label} 剩余 100%`
      let s = `${w.label} 剩余 ${Math.max(0, 100 - Math.round(w.used))}%`
      if (!marked && w.used === maxUsed && w.reset > 0) { marked = true; s += ` · ${when(w.reset, nowSec)} 重置` }
      return s
    })
    let text = parts.join(' ｜ ')
    if (p.source === 'records') text += ' · 本机记录'   // 没问到 Codex，用的是本机会话记录，可能不是最新
    return { text, low: maxUsed >= 90, windows, source: p.source || 'live' }
  }
  root.CodexQuota = { view }
  if (typeof module !== 'undefined') module.exports = { view }
})(typeof window !== 'undefined' ? window : globalThis)
