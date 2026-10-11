using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CrossPet
{
    /// <summary>
    /// GPT 额度（可选，默认关）：读 Codex 会话记录里最后一条 rate_limits，只取额度数字，不读也不保存对话内容。
    /// 和 macOS 版 codexQuota() 同一套规则：剩余百分比、按窗口长度标「5小时 / 今日 / 本周」、重置时间标在用得最多的窗口上。
    /// </summary>
    static class CodexQuota
    {
        public static Dictionary<string, object> Read()
        {
            var files = new List<FileInfo>();
            for (int back = 0; back < 4; back++)
            {
                var day = DateTime.Now.AddDays(-back);
                var dir = Path.Combine(Store.Codex, "sessions", day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
                try { files.AddRange(new DirectoryInfo(dir).GetFiles("*.jsonl")); } catch { }
            }
            // 挑「最新的额度记录」：Codex 开着会话文件一直往里写时，Windows 文件列表里的修改时间常常不更新（要等它关掉文件），
            // 按修改时间挑会挑到旧对话、显示旧额度。所以多看几个最近的会话（修改时间、创建时间取新的那个排），
            // 比较每条额度记录自己带的时间戳，取最新的
            Dictionary<string, object> best = null; var bestTs = "";
            foreach (var f in files.OrderByDescending(f => f.LastWriteTimeUtc > f.CreationTimeUtc ? f.LastWriteTimeUtc : f.CreationTimeUtc).Take(12))
            {
                var (q, ts) = FromFile(f.FullName);
                if (q != null && string.CompareOrdinal(ts, bestTs) > 0) { best = q; bestTs = ts; }
            }
            return best;
        }

        static (Dictionary<string, object>, string) FromFile(string path)
        {
            string text;
            try
            {
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var n = (int)Math.Min(s.Length, 400_000);
                    s.Seek(-n, SeekOrigin.End);
                    var buf = new byte[n]; var read = 0;
                    while (read < n) { var r = s.Read(buf, read, n - read); if (r <= 0) break; read += r; }
                    text = Encoding.UTF8.GetString(buf, 0, read);  // 截断处切到中文也不怕：坏的字节变成替换符
                }
            }
            catch { return (null, ""); }

            foreach (var line in text.Split('\n').Reverse())
            {
                if (!line.Contains("\"rate_limits\"")) continue;
                Dictionary<string, object> rl; object parsed;
                try { parsed = Store.Json.DeserializeObject(line); rl = Find(parsed); } catch { continue; }
                if (rl == null) continue;
                // 每行开头的 "timestamp"：ISO 时间（UTC），按字符串比较就是按时间比较
                var stamp = parsed is Dictionary<string, object> top && top.TryGetValue("timestamp", out var tsv) ? tsv as string ?? "" : "";
                var windows = new List<(string label, double used, double reset)>();
                foreach (var key in new[] { "primary", "secondary" })
                {
                    if (!(rl.TryGetValue(key, out var w) && w is Dictionary<string, object> win)) continue;
                    if (!win.TryGetValue("used_percent", out var u) || !IsNumber(u)) continue;
                    var minutes = win.TryGetValue("window_minutes", out var m) && IsNumber(m) ? Convert.ToInt32(m) : 0;
                    var label = minutes >= 7 * 24 * 60 ? "本周" : minutes >= 24 * 60 ? "今日" : $"{Math.Max(1, minutes / 60)}小时";
                    var reset = win.TryGetValue("resets_at", out var r) && IsNumber(r) ? Convert.ToDouble(r) : 0;
                    windows.Add((label, Convert.ToDouble(u), reset));
                }
                if (windows.Count == 0) continue;
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var live = windows.Where(w => w.reset == 0 || w.reset > now).ToList();
                var maxUsed = live.Count > 0 ? live.Max(w => w.used) : 0;
                var marked = false;
                var parts = windows.Select(w =>
                {
                    // 记录之后过了重置时间、又没有新记录（没再用 Codex）：这个窗口现在是满的
                    if (w.reset > 0 && w.reset <= now) return $"{w.label} 剩余 100%";
                    var piece = $"{w.label} 剩余 {Math.Max(0, 100 - (int)Math.Round(w.used))}%";
                    if (!marked && w.used == maxUsed && w.reset > 0)
                    {
                        marked = true;
                        var d = DateTimeOffset.FromUnixTimeSeconds((long)w.reset).LocalDateTime;
                        piece += $" · {((d - DateTime.Now).TotalHours < 24 ? d.ToString("HH:mm") : $"{d.Month}/{d.Day}")} 重置";
                    }
                    return piece;
                });
                return (new Dictionary<string, object>
                {
                    ["text"] = string.Join(" ｜ ", parts),
                    ["low"] = maxUsed >= 90,
                    // 给桌宠比对用：某个窗口在预定重置时间前突然恢复一大截 → 播 reset 动画
                    ["windows"] = windows.Select(w => new Dictionary<string, object> { ["label"] = w.label, ["used"] = w.used, ["reset"] = w.reset }).ToList(),
                }, stamp);
            }
            return (null, "");
        }

        static bool IsNumber(object v) => v is int || v is long || v is double || v is decimal;

        static Dictionary<string, object> Find(object o)
        {
            if (o is Dictionary<string, object> d)
            {
                if (d.TryGetValue("rate_limits", out var rl) && rl is Dictionary<string, object> r) return r;
                foreach (var v in d.Values) { var f = Find(v); if (f != null) return f; }
            }
            else if (o is object[] arr) foreach (var v in arr) { var f = Find(v); if (f != null) return f; }
            return null;
        }
    }
}
