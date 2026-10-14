using System;

namespace CrossPet
{
    /// <summary>网页缩放的自我纠正（不依赖界面，tests/windows/LogicTests 测）。
    /// 1.3.4 有几位用户（系统缩放大于 100%、人物没调到 100%）遇到：窗口大小对，网页却没按设的缩放排版——
    /// 网页比窗口大，最上面的气泡、两边的光晕被切掉。我们自己的电脑上复现不了，原因没查清，所以不猜：
    /// 量网页实际排出来的大小，和应有的画布比，按比例修正给 WebView2 的缩放（实际缩放 = 人物大小 × 修正系数）。
    /// 网页宽 = 窗口宽 ÷ 实际生效的缩放，所以要让网页回到应有的宽，系数 × 实际宽 ÷ 应有宽</summary>
    static class ZoomFix
    {
        /// <summary>返回新的修正系数；尺寸对得上（差 2% 以内，取整误差）或量到的数不对劲时返回 null（不改）</summary>
        public static double? Next(double fix, double expectedW, double expectedH, double actualW, double actualH)
        {
            if (actualW <= 0 || actualH <= 0 || expectedW <= 0 || expectedH <= 0) return null;
            double rw = actualW / expectedW, rh = actualH / expectedH;
            if (Math.Abs(rw - 1) <= 0.02 && Math.Abs(rh - 1) <= 0.02) return null;
            var r = Math.Sqrt(rw * rh);   // 宽高两个比例取几何平均：正常情况下它们相等
            return Math.Min(4, Math.Max(0.25, fix * r));
        }
    }
}
