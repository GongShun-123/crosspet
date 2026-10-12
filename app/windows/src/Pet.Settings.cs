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

        /// <summary>改大小：窗口和网页一起按比例缩放，底边中点不动（脚下的位置不变）</summary>
        void ApplyScale(double scale)
        {
            Native.GetWindowRect(hwnd, out var old);
            Window.Width = BaseWidth * scale; Window.Height = BaseHeight * scale;
            Window.UpdateLayout();
            if (web != null) web.ZoomFactor = scale;
            Native.GetWindowRect(hwnd, out var now);
            int w = now.Right - now.Left, h = now.Bottom - now.Top;
            var (x, y) = Clamp((old.Left + old.Right) / 2 - w / 2, old.Bottom - h, w, h);
            Native.MoveTo(hwnd, x, y);
            SavePosition();
        }

        /// <summary>拖滑块时每次只变一点，直接跟手；一下子变很多（点「还原」）时用 0.2 秒过渡过去</summary>
        DispatcherTimer scaleTimer;
        double shownScale;
        void AnimateScale(double target)
        {
            scaleTimer?.Stop();
            var from = shownScale > 0 ? shownScale : Window.Width / BaseWidth;
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
