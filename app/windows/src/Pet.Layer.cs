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
        // ---------------- 显示层级 ----------------
        // always：始终在最上层；ai：前台是 AI 应用、AI 等你回答、刚干完活时在最上层，其他时候是普通窗口（会被挡住）；
        // normal：普通窗口。另外全屏程序（视频 / 游戏 / 演示）在前台时整个隐藏
        static string LayerMode => Store.Settings.TryGetValue("layer", out var v) && v is string s && (s == "ai" || s == "normal") ? s : "always";
        static bool HideFullscreen => !(Store.Settings.TryGetValue("hideFullscreen", out var v) && v is bool b && !b);
        readonly Dictionary<string, (string pose, DateTime at)> lastPose = new Dictionary<string, (string, DateTime)>();
        bool frontIsAI, hiddenForFullscreen;

        bool AIActive()
        {
            if (frontIsAI) return true;
            var now = DateTime.UtcNow;
            // 等你回答：一直等到你回答（最长 10 分钟）；刚干完活：浮上来一会儿。
            // AI 在后台干活时不浮上来：选这个模式的人就是嫌挡路，干活时往往正是你切去忙别的
            return lastPose.Values.Any(p => p.pose == "asking" ? (now - p.at).TotalMinutes < 10
                : (now - p.at).TotalSeconds < 8 && (p.pose == "happy" || p.pose == "proud"));
        }

        // ---- 藏起来 / 露出来 ----
        // 不用 Window.Hide() / Show()：透明分层窗口里嵌着显卡渲染的 WebView2，反复藏和放有概率让 WPF 的渲染线程挂掉
        // （0x88980406 UCEERR_RENDERTHREADFAILURE），之后画面冻住、摸头没反应，只有网页还在跑（1.3.2 用户反馈）。
        // 改成「隐身」：窗口一直开着，不透明度 0（透明分层窗口全透明的地方鼠标直接穿过去）、取消置顶（不压在全屏游戏 / 视频上面），网页动画暂停
        bool concealed;
        void SetConcealed(bool c)
        {
            if (Window == null || concealed == c) return;
            concealed = c;
            if (c) { Window.Opacity = 0; if (hwnd != IntPtr.Zero) Native.DropFromTop(hwnd); }
            else { Window.Opacity = 1; Raise(); }
            UpdatePageHidden();
        }
        /// 置顶（层级要求置顶、又没在隐身时）
        void Raise() { if (!concealed && Window.Topmost && hwnd != IntPtr.Zero) Native.BringToTop(hwnd); }
        void UpdatePageHidden() => Js($"setPageHidden({(concealed || sessionLocked ? "true" : "false")})");

        void ApplyLayer()
        {
            if (Window == null || hwnd == IntPtr.Zero) return;
            if (userHidden) return;   // 你用快捷键藏起来了：什么都别把她翻出来
            // 全屏时隐藏
            var hide = HideFullscreen && !fullscreenOverride && Native.InFullscreen(hwnd);
            if (hide != hiddenForFullscreen)
            {
                hiddenForFullscreen = hide;
                Store.Log(hide ? $"全屏中，先藏起来（前台 {Native.ForegroundExe()}）" : "退出全屏，出来了");
                SetConcealed(hide);
            }
            var top = LayerMode == "always" || (LayerMode == "ai" && AIActive());
            if (Window.Topmost == top || surfacing) return;
            // 原本被别的窗口挡着、要浮上来：先藏起来，换完层级再淡入（没被挡住就直接换，免得闪一下）
            if (top && !hiddenForFullscreen && Native.Covered(hwnd)) { _ = Surface(); return; }
            Window.Topmost = top;
            if (!top) Native.DropFromTop(hwnd); else Raise();
        }

        bool surfacing;
        async Task Surface()
        {
            surfacing = true;
            try
            {
                await Eval("sink()");
                await Task.Delay(50);
                Window.Topmost = true;
                Raise();
            }
            finally
            {
                surfacing = false;
                Js("surface()");
            }
        }
    }
}
