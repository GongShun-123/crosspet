using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossPet
{
    // 额度回复的排版（不依赖 Windows，tests/windows/LogicTests 在 macOS 上测）。取额度的部分在 AntigravityQuota.cs
    static partial class AntigravityQuota
    {
        /// <summary>
        /// 额度按「额度池」共享（比如所有 Gemini 模型一个池，Claude 和 GPT-OSS 一个池）：按 (剩余比例, 重置时间) 分组，
        /// 每组用模型家族名命名，Gemini 池排前面；重置时间标在第一个池和快用完的池上。和 macOS 版 antigravityQuota 一样。
        /// </summary>
        public static Dictionary<string, object> Parse(object obj)
        {
            if (!(obj is Dictionary<string, object> root) || !(root.TryGetValue("userStatus", out var st) && st is Dictionary<string, object> status)) return null;
            if (!(status.TryGetValue("cascadeModelConfigData", out var cfg) && cfg is Dictionary<string, object> config)) return null;
            if (!(config.TryGetValue("clientModelConfigs", out var ms) && ms is object[] models)) return null;
            var pools = new List<(string key, double left, DateTime? reset, List<string> families)>();
            foreach (var m in models.OfType<Dictionary<string, object>>())
            {
                if (!(m.TryGetValue("quotaInfo", out var qi) && qi is Dictionary<string, object> info)) continue;
                // 不可用的模型不算：比如只给 Ultra 套餐的「Gemini 4 Argon」，别的账号里标着 disabled、额度信息是空的，
                // 以前被当成「剩 0%」排在第一行（2026-10-14 两位用户反馈）。和 macOS 版 antigravityQuota 一致
                if ((m.TryGetValue("disabled", out var dis) && dis is bool off && off) || (!info.ContainsKey("remainingFraction") && !info.ContainsKey("resetTime"))) continue;
                var left = info.TryGetValue("remainingFraction", out var f) ? Convert.ToDouble(f) : 0;   // 用完时这个字段会被省略（但还有重置时间）
                var resetText = info.TryGetValue("resetTime", out var r) ? r as string ?? "" : "";
                var family = ((m.TryGetValue("label", out var l) ? l as string : null) ?? "?").Split(' ')[0];
                var key = left + "|" + resetText;
                var i = pools.FindIndex(p => p.key == key);
                if (i >= 0) { if (!pools[i].families.Contains(family)) pools[i].families.Add(family); }
                else pools.Add((key, left, DateTime.TryParse(resetText, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime() : (DateTime?)null, new List<string> { family }));
            }
            if (pools.Count == 0) return null;
            pools = pools.OrderBy(p => p.families.Contains("Gemini") ? 0 : 1).ThenBy(p => p.left).ToList();
            var parts = pools.Select((p, i) =>
            {
                var piece = $"{string.Join("/", p.families)} 剩余 {(int)Math.Round(p.left * 100)}%";
                if ((i == 0 || p.left <= 0.1) && p.reset is DateTime d)
                    piece += $" · {((d - DateTime.Now).TotalHours < 24 ? d.ToString("HH:mm") : $"{d.Month}/{d.Day}")} 重置";
                return piece;
            });
            return new Dictionary<string, object> { ["text"] = string.Join(" ｜ ", parts), ["low"] = pools.Min(p => p.left) <= 0.1 };
        }
    }
}
