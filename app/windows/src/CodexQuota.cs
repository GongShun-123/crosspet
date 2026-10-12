using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrossPet
{
    /// <summary>
    /// GPT（Codex）额度来源，和 macOS 版 CodexQuotaSource 同一套规则（docs/superpowers/specs/2026-10-12-codex-quota-design.md）：
    /// 主来源起 `codex app-server` 问 account/rateLimits/read（Codex 用自己的登录查实时额度，不对话、不耗额度；CrossPet 不碰凭据）；
    /// 备用读会话记录（CODEX_HOME，加上开着的 WSL 里的）。排版在网页 codex-quota.js 里做，这里只交原始数据
    /// </summary>
    static class CodexQuota
    {
        static string cached;

        public static string FindBinary()
        {
            var o = Environment.GetEnvironmentVariable("CROSSPET_CODEX_BIN");
            if (!string.IsNullOrEmpty(o)) return File.Exists(o) ? o : null;
            if (cached != null && File.Exists(cached)) return cached;
            cached = null;
            // Codex 桌面版开着时，它自己的 codex.exe 在跑：用它（装在受保护目录里也能拿到路径），记下来下次直接用
            try
            {
                foreach (var p in Process.GetProcessesByName("codex"))
                    try
                    {
                        var f = p.MainModule?.FileName;
                        if (f == null || !File.Exists(f)) continue;
                        if (!(Store.Settings.TryGetValue("codexBin", out var old) && old as string == f)) { Store.Settings["codexBin"] = f; Store.SaveSettings(); }
                        return cached = f;
                    }
                    catch { }
            }
            catch { }
            if (Store.Settings.TryGetValue("codexBin", out var s) && s is string saved && File.Exists(saved)) return cached = saved;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                foreach (var name in new[] { "codex.exe", "codex.cmd" })
                    try { var f = Path.Combine(dir.Trim(), name); if (File.Exists(f)) return cached = f; } catch { }
            var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd");
            return File.Exists(npm) ? cached = npm : null;
        }

        /// 查询失败时调：程序可能被更新挪走了，下次重新找
        public static void Forget() { cached = null; }

        public static async Task<(Dictionary<string, object> rateLimits, string error, int ms)> QueryLive(string binary, int timeoutMs = 15000)
        {
            var sw = Stopwatch.StartNew();
            var cmd = binary.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            var psi = new ProcessStartInfo
            {
                FileName = cmd ? "cmd.exe" : binary,
                Arguments = cmd ? $"/d /s /c \"\"{binary}\" app-server\"" : "app-server",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
            };
            Process p;
            try { p = Process.Start(psi); } catch (Exception e) { return (null, "起不来 Codex：" + e.Message, (int)sw.ElapsedMilliseconds); }
            try
            {
                p.ErrorDataReceived += (_, __) => { }; p.BeginErrorReadLine();   // 不读 stderr 会把管道塞满
                var stdin = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                stdin.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"crosspet\",\"version\":\"1\"}}}");
                stdin.WriteLine("{\"method\":\"initialized\"}");
                stdin.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
                var read = Task.Run(() =>
                {
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        Dictionary<string, object> msg;
                        try { msg = Store.Json.DeserializeObject(line) as Dictionary<string, object>; } catch { continue; }
                        if (msg == null || !(msg.TryGetValue("id", out var id) && Convert.ToString(id) == "2")) continue;
                        if (msg.TryGetValue("error", out var e) && e is Dictionary<string, object> ed)
                            return ((Dictionary<string, object>)null, ed.TryGetValue("message", out var m) ? m as string ?? "Codex 服务返回错误" : "Codex 服务返回错误");
                        var r = msg.TryGetValue("result", out var rv) ? rv as Dictionary<string, object> : null;
                        Dictionary<string, object> rl = null;
                        if (r != null && r.TryGetValue("rateLimitsByLimitId", out var by) && by is Dictionary<string, object> byd && byd.TryGetValue("codex", out var c)) rl = c as Dictionary<string, object>;
                        if (rl == null && r != null && r.TryGetValue("rateLimits", out var rr)) rl = rr as Dictionary<string, object>;
                        return (rl, rl == null ? "Codex 服务没给额度" : null);
                    }
                    return ((Dictionary<string, object>)null, "Codex 服务提前退出");
                });
                var winner = await Task.WhenAny(read, Task.Delay(timeoutMs));
                if (winner != read) return (null, $"超时（{timeoutMs / 1000} 秒没回）", (int)sw.ElapsedMilliseconds);
                var (rl2, err) = read.Result;
                return (rl2, err, (int)sw.ElapsedMilliseconds);
            }
            catch (Exception e) { return (null, "问 Codex 出错：" + e.Message, (int)sw.ElapsedMilliseconds); }
            finally { try { if (!p.HasExited) p.Kill(); } catch { } p.Dispose(); }
        }

        /// 给网页的 payload（rateLimits 原样，不含账号标识——accountId 在 result 里，不在 rateLimits 里）
        public static Dictionary<string, object> Payload(string source, Dictionary<string, object> rateLimits, string error, int ms)
        {
            var d = new Dictionary<string, object> { ["source"] = source, ["ms"] = ms, ["at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            if (rateLimits != null) d["rateLimits"] = rateLimits;
            if (error != null) d["error"] = error;
            return d;
        }

        /// 运行状态里的简写：5小时已用 3%，本周已用 40%
        public static string Brief(Dictionary<string, object> rl)
        {
            var parts = new List<string>();
            foreach (var k in new[] { "primary", "secondary" })
            {
                if (!(rl.TryGetValue(k, out var w) && w is Dictionary<string, object> win)) continue;
                var used = win.TryGetValue("usedPercent", out var u) ? u : win.TryGetValue("used_percent", out var u2) ? u2 : null;
                var mins = win.TryGetValue("windowDurationMins", out var m) ? m : win.TryGetValue("window_minutes", out var m2) ? m2 : null;
                if (!IsNumber(used)) continue;
                var mm = IsNumber(mins) ? Convert.ToInt32(mins) : 0;
                parts.Add($"{(mm >= 10080 ? "本周" : mm >= 1440 ? "今日" : $"{Math.Max(1, mm / 60)}小时")}已用 {(int)Math.Round(Convert.ToDouble(used), MidpointRounding.AwayFromZero)}%");
            }
            return string.Join("，", parts);
        }

        // ---------------- 备用：会话记录 ----------------

        /// 最近 4 天的会话记录里（Windows 这边加上开着的 WSL 里的），按每行自带的 timestamp 取最新的一条 rate_limits
        public static Dictionary<string, object> Records()
        {
            var files = new List<FileInfo>();
            foreach (var home in new[] { Store.Codex }.Concat(WslHomes()))
                for (int back = 0; back < 4; back++)
                {
                    var day = DateTime.Now.AddDays(-back);
                    var dir = Path.Combine(home, "sessions", day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
                    try { files.AddRange(new DirectoryInfo(dir).GetFiles("*.jsonl")); } catch { }
                }
            Dictionary<string, object> best = null; var bestTs = "";
            // Codex 开着会话文件写的时候，文件列表里的修改时间常常不更新：修改 / 创建时间取新的排，最后按记录自己的时间比
            foreach (var f in files.OrderByDescending(f => f.LastWriteTimeUtc > f.CreationTimeUtc ? f.LastWriteTimeUtc : f.CreationTimeUtc).Take(12))
            {
                var (rl, ts) = FromFile(f.FullName);
                if (rl != null && (best == null || string.CompareOrdinal(ts, bestTs) > 0)) { best = rl; bestTs = ts; }
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
                object parsed; Dictionary<string, object> rl;
                try { parsed = Store.Json.DeserializeObject(line); rl = Find(parsed); } catch { continue; }
                if (rl == null) continue;
                var hasWindow = (rl.TryGetValue("primary", out var pw) && pw != null) || (rl.TryGetValue("secondary", out var sw) && sw != null);
                if (!hasWindow) continue;
                var stamp = parsed is Dictionary<string, object> top && top.TryGetValue("timestamp", out var tsv) ? tsv as string ?? "" : "";
                return (rl, stamp);
            }
            return (null, "");
        }

        // ---------------- WSL ----------------
        // 在 WSL 里用 Codex 的人，记录写在 WSL 的 ~/.codex。访问 \\wsl.localhost 会把没开的发行版拉起来（很重），
        // 所以只在 WSL 虚拟机开着时、只访问正在运行的发行版；找到的目录缓存 10 分钟
        static List<string> wslHomes = new List<string>();
        static DateTime wslCheckedAt = DateTime.MinValue;
        public static string WslNote = "";

        static IEnumerable<string> WslHomes()
        {
            bool vmUp;
            try { vmUp = Process.GetProcessesByName("vmmemWSL").Length > 0 || Process.GetProcessesByName("vmmem").Length > 0; } catch { vmUp = false; }
            if (!vmUp) { WslNote = ""; return Array.Empty<string>(); }
            if ((DateTime.UtcNow - wslCheckedAt).TotalMinutes < 10) return wslHomes;
            wslCheckedAt = DateTime.UtcNow;
            var found = new List<string>();
            try
            {
                var psi = new ProcessStartInfo("wsl.exe", "-l --running -q") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.Unicode };
                using (var p = Process.Start(psi))
                {
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } }
                    var distros = (outTask.Wait(1000) ? outTask.Result : "").Replace("\0", "")
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
                    foreach (var d in distros)
                        try
                        {
                            foreach (var u in Directory.GetDirectories($@"\\wsl.localhost\{d}\home"))
                                if (Directory.Exists(Path.Combine(u, ".codex", "sessions"))) found.Add(Path.Combine(u, ".codex"));
                        }
                        catch { }
                }
            }
            catch { }
            wslHomes = found;
            WslNote = found.Count == 0 ? "" : "，含 WSL：" + string.Join("、", found.Select(h => h.Replace(@"\\wsl.localhost\", "")));
            return wslHomes;
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
