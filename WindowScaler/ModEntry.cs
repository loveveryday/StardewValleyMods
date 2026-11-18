using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace WindowScaler;

public class ModConfig
{
    // 只绑主键盘的 =/+ 和 -：小键盘 +/- 已被 CheatPanel 占用
    public KeybindList WindowBigger { get; set; } = KeybindList.Parse("LeftControl + OemPlus, RightControl + OemPlus");
    public KeybindList WindowSmaller { get; set; } = KeybindList.Parse("LeftControl + OemMinus, RightControl + OemMinus");
}

/// <summary>保存在 window.json 里的窗口状态。删除该文件即可重置。</summary>
public class WindowState
{
    /// <summary>1 倍对应的尺寸：第一次在窗口模式下运行时的窗口大小，之后固定不变，避免倍数叠加。</summary>
    public int BaseWidth { get; set; }
    public int BaseHeight { get; set; }
    public int ScaleIndex { get; set; } = 1;
}

public class ModEntry : Mod
{
    private const string StateFile = "window.json";
    private static readonly float[] Scales = { 0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 3.5f, 4f };

    private ModConfig config = new();
    private WindowState state = new();
    private readonly List<(string Text, int Until)> toasts = new();

    public override void Entry(IModHelper helper)
    {
        config = helper.ReadConfig<ModConfig>();
        state = helper.Data.ReadJsonFile<WindowState>(StateFile) ?? new WindowState();
        helper.Events.GameLoop.UpdateTicked += RestoreWindowOnce;
        helper.Events.Input.ButtonsChanged += OnButtonsChanged;
        // 自己画提示：屏幕下方居中、工具栏之上；打开菜单时画在菜单之上
        helper.Events.Display.RenderedHud += (_, e) => { if (Game1.activeClickableMenu == null) DrawToasts(e.SpriteBatch); };
        helper.Events.Display.RenderedActiveMenu += (_, e) => DrawToasts(e.SpriteBatch);
    }

    // 游戏在启动后的头几帧才应用自己的窗口模式和尺寸，等约 1 秒再恢复，避免被覆盖
    private void RestoreWindowOnce(object? sender, UpdateTickedEventArgs e)
    {
        if (e.Ticks < 60)
            return;
        Helper.Events.GameLoop.UpdateTicked -= RestoreWindowOnce;

        var bounds = Game1.game1.Window.ClientBounds;
        Monitor.Log($"启动时窗口 {bounds.Width}×{bounds.Height}，窗口模式：{Game1.options.isCurrentlyWindowed()}", LogLevel.Trace);
        if (!Game1.options.isCurrentlyWindowed())
        {
            if (state.ScaleIndex != 1)
                Monitor.Log($"上次窗口为 {Scales[state.ScaleIndex]} 倍，但当前不是窗口模式，未恢复", LogLevel.Info);
            return;
        }
        if (EnsureBase())
            return;

        state.ScaleIndex = Math.Clamp(state.ScaleIndex, 0, Scales.Length - 1);
        if (state.ScaleIndex != 1)
            ApplyScale();
    }

    /// <summary>还没有 1 倍尺寸时，用当前窗口大小作为 1 倍并保存。只应在窗口模式下调用。</summary>
    private bool EnsureBase()
    {
        if (state.BaseWidth > 0 && state.BaseHeight > 0)
            return false;
        var bounds = Game1.game1.Window.ClientBounds;
        (state.BaseWidth, state.BaseHeight, state.ScaleIndex) = (bounds.Width, bounds.Height, 1);
        Helper.Data.WriteJsonFile(StateFile, state);
        return true;
    }

    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        int dir = config.WindowBigger.JustPressed() ? 1 : config.WindowSmaller.JustPressed() ? -1 : 0;
        if (dir == 0 || Game1.keyboardDispatcher.Subscriber != null)
            return;

        if (!Game1.options.isCurrentlyWindowed())
        {
            Notify("窗口大小只能在窗口模式下调整");
            return;
        }

        EnsureBase();
        int next = Math.Clamp(state.ScaleIndex + dir, 0, Scales.Length - 1);
        if (next == state.ScaleIndex)
        {
            Notify($"窗口已是 {Scales[state.ScaleIndex]} 倍（{(dir > 0 ? "最大" : "最小")}）");
            return;
        }
        state.ScaleIndex = next;
        Helper.Data.WriteJsonFile(StateFile, state);
        ApplyScale();
    }

    private void ApplyScale()
    {
        float scale = Scales[state.ScaleIndex];
        int w = (int)Math.Round(state.BaseWidth * scale), h = (int)Math.Round(state.BaseHeight * scale);
        Game1.game1.SetWindowSize(w, h);
        Notify($"窗口 {scale} 倍：{w}×{h}");
    }

    private void Notify(string text)
    {
        Monitor.Log(text, LogLevel.Info);
        toasts.Add((text, Game1.ticks + 240)); // 约 4 秒
        if (toasts.Count > 5) toasts.RemoveAt(0);
    }

    /// <summary>在屏幕下方居中、工具栏之上，自下而上堆叠显示提示（最新的在最下面）。</summary>
    private void DrawToasts(SpriteBatch b)
    {
        toasts.RemoveAll(t => Game1.ticks > t.Until);
        int y = Game1.uiViewport.Height - 136; // 给底部工具栏留出位置
        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            string text = toasts[i].Text;
            var size = Game1.dialogueFont.MeasureString(text);
            int w = (int)size.X + 56, h = (int)size.Y + 40;
            y -= h + 8;
            int x = (Game1.uiViewport.Width - w) / 2;
            IClickableMenu.drawTextureBox(b, x, y, w, h, Color.White);
            Utility.drawTextWithShadow(b, text, Game1.dialogueFont, new Vector2(x + 28, y + 20), Game1.textColor);
        }
    }
}
