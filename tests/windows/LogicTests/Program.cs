using System;
using System.IO;
using CrossPet;

static class Program
{
    static int fails;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) fails++; }

    static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "crosspet-sprite-test", "characters");
        string file, mime;

        Check("普通立绘", SpritePath.TryResolve(root, "/characters/claude/idle.webp", out file, out mime)
            && file == Path.GetFullPath(Path.Combine(root, "claude", "idle.webp")) && mime == "image/webp");
        Check("png", SpritePath.TryResolve(root, "/characters/gpt/edge_left.png", out file, out mime) && mime == "image/png");
        Check("扩展名大写", SpritePath.TryResolve(root, "/characters/gpt/IDLE.WEBP", out file, out mime) && mime == "image/webp");
        Check("中文角色名（网址编码）", SpritePath.TryResolve(root, "/characters/%E5%8F%AF%E8%90%9D/idle.webp", out file, out mime)
            && file == Path.GetFullPath(Path.Combine(root, "可萝", "idle.webp")));
        Check("文件名里有空格（%20）", SpritePath.TryResolve(root, "/characters/my%20pet/idle%201.webp", out file, out mime));

        Check("角色目录是 ..", !SpritePath.TryResolve(root, "/characters/%2E%2E/settings.webp", out file, out mime));
        Check("文件名里编码的斜杠", !SpritePath.TryResolve(root, "/characters/claude/..%2F..%2Fsettings.webp", out file, out mime));
        Check("文件名里编码的反斜杠", !SpritePath.TryResolve(root, "/characters/claude/..%5C..%5Cx.webp", out file, out mime));
        Check("盘符冒号", !SpritePath.TryResolve(root, "/characters/claude/C%3Ax.webp", out file, out mime));
        Check("character.json 不给", !SpritePath.TryResolve(root, "/characters/gpt/character.json", out file, out mime));
        Check("数据目录里别的文件不给", !SpritePath.TryResolve(root, "/settings.json", out file, out mime));
        Check("多一层目录不给", !SpritePath.TryResolve(root, "/characters/a/b/c.webp", out file, out mime));
        Check("空的文件名", !SpritePath.TryResolve(root, "/characters/claude/", out file, out mime));
        Check("结尾是点", !SpritePath.TryResolve(root, "/characters/claude./idle.webp", out file, out mime));
        Check("控制字符", !SpritePath.TryResolve(root, "/characters/claude/a%00.webp", out file, out mime));

        // 藏起来时鼠标穿透（WS_EX_TRANSPARENT = 0x20）：只动这一位，别的样式原样保留
        const int layered = 0x80000, noActivate = 0x08000000, toolWindow = 0x80, transparent = 0x20;
        int ex = layered | noActivate | toolWindow;
        Check("藏起来：加上鼠标穿透", ConcealStyle.ClickThrough(ex, true) == (ex | transparent));
        Check("叫回来：去掉鼠标穿透", ConcealStyle.ClickThrough(ex | transparent, false) == ex);
        Check("重复藏不会出错", ConcealStyle.ClickThrough(ConcealStyle.ClickThrough(ex, true), true) == (ex | transparent));
        Check("没藏时叫回来不变", ConcealStyle.ClickThrough(ex, false) == ex);

        Console.WriteLine(fails == 0 ? "全部通过" : $"{fails} 项失败");
        return fails == 0 ? 0 : 1;
    }
}
