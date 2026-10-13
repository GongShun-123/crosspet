using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CrossPet
{
    sealed partial class Pet
    {
        // ---------------- 设置窗口（web/settings.html，和 macOS 版共用） ----------------
        static double Scale => Store.Settings.TryGetValue("size", out var v) && (v is double || v is int || v is decimal) &&
                               Convert.ToDouble(v) >= 0.6 && Convert.ToDouble(v) <= 1.6 ? Convert.ToDouble(v) : 1;
        static bool ShowName => !(Store.Settings.TryGetValue("showName", out var v) && v is bool b && !b);
        static bool EggsEnabled => !(Store.Settings.TryGetValue("eggs", out var v) && v is bool b && !b);
        /// <summary>文字大小（只在解锁时用）和锁：没存过 = 锁着，文字跟着人物，和以前一样</summary>
        static double TextScale => Store.Settings.TryGetValue("textSize", out var v) && (v is double || v is int || v is decimal) &&
                                   Convert.ToDouble(v) >= 0.6 && Convert.ToDouble(v) <= 1.6 ? Convert.ToDouble(v) : 1;
        static bool TextLinked => !(Store.Settings.TryGetValue("textLinked", out var v) && v is bool b && !b);
        static string Num(double d) => d.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>改大小：窗口 = 画布 × 人物大小，网页按人物大小缩放。
        /// 改人物大小时底边中点不动（脚下的位置不变）；改文字大小时底栏变高 / 变矮多少，窗口底边就往下 / 往上挪多少，人物不动</summary>
        void ApplyScale(double scale)
        {
            Native.GetWindowRect(hwnd, out var old);
            Window.Width = pageW * scale; Window.Height = pageH * scale;
            Window.UpdateLayout();
            if (web != null) web.ZoomFactor = scale;
            Native.GetWindowRect(hwnd, out var now);
            int w = now.Right - now.Left, h = now.Bottom - now.Top;
            int down = (int)Math.Round((pageFoot - shownFoot) * w / pageW);   // 网页像素 → 物理像素（含系统缩放）
            shownFoot = pageFoot;
            var (x, y) = Clamp((old.Left + old.Right) / 2 - w / 2, old.Bottom + down - h, w, h);
            Native.MoveTo(hwnd, x, y);
            SavePosition();
        }

        /// <summary>人物大小 / 文字大小变了：交给网页算画布（规则在 app/web/text-size.js），按回来的尺寸设窗口。
        /// target：人物大小要动画到哪；null = 只是文字变了，按现在的人物大小重设一次</summary>
        async void ApplySizes(double? target = null)
        {
            if (ready && web?.CoreWebView2 != null)   // 网页还没加载完：先按旧画布，加载完（NavigationCompleted）会再调一次
            {
                try
                {
                    var json = await Eval($"setSizes({Num(target ?? Scale)}, {Num(TextScale)}, {(TextLinked ? "true" : "false")})");
                    if (Store.Json.DeserializeObject(Store.Json.Deserialize<string>(json)) is Dictionary<string, object> o)
                    {
                        double w = Convert.ToDouble(o["w"]), h = Convert.ToDouble(o["h"]), foot = Convert.ToDouble(o["foot"]);
                        if (w != pageW || h != pageH || foot != pageFoot) dockCut.Clear();   // 画布变了：贴边切口要重新量
                        pageW = w; pageH = h; pageFoot = foot;
                    }
                }
                catch (Exception e) { Store.Log("文字大小：算画布失败 " + e.Message); }
            }
            if (target is double t) AnimateScale(t); else ApplyScale(shownScale > 0 ? shownScale : Scale);
        }

        /// <summary>拖滑块时每次只变一点，直接跟手；一下子变很多（点「还原」）时用 0.2 秒过渡过去</summary>
        DispatcherTimer scaleTimer;
        double shownScale;
        void AnimateScale(double target)
        {
            scaleTimer?.Stop();
            var from = shownScale > 0 ? shownScale : Scale;
            if (Math.Abs(target - from) <= 0.06) { shownScale = target; ApplyScale(target); return; }
            var start = DateTime.Now;
            scaleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            scaleTimer.Tick += (_, __) =>
            {
                var k = Math.Min(1, (DateTime.Now - start).TotalSeconds / 0.2);
                shownScale = from + (target - from) * (1 - Math.Pow(1 - k, 3));
                ApplyScale(shownScale);
                if (k >= 1) { scaleTimer.Stop(); if (dock != null) ApplyDock(); }   // 贴着边时调完大小重新对齐边缘
            };
            scaleTimer.Start();
        }

        void OpenSettings()
        {
            if (settings == null) { settings = new SettingsWindow(this, env); settings.Closed += () => settings = null; }
            settings.Show();
        }

        List<string> integrated;   // 已接入的 AI（设置窗口打开时查一次，接入 / 撤销后再查）
    }
}
