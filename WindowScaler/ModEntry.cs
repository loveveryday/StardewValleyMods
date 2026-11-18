using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace WindowScaler;

public class ModConfig
{
    // 只绑主键盘的 =/+ 和 -：小键盘 +/- 已被 CheatPanel 占用
    public KeybindList WindowBigger { get; set; } = KeybindList.Parse("LeftControl + OemPlus, RightControl + OemPlus");
    public KeybindList WindowSmaller { get; set; } = KeybindList.Parse("LeftControl + OemMinus, RightControl + OemMinus");
}

public class ModEntry : Mod
{
    private static readonly float[] Scales = { 0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 3.5f, 4f };

    private ModConfig config = new();
    private int scaleIndex = 1; // 1 倍
    private int baseWidth, baseHeight;

    public override void Entry(IModHelper helper)
    {
        config = helper.ReadConfig<ModConfig>();
        helper.Events.GameLoop.GameLaunched += (_, _) =>
        {
            var bounds = Game1.game1.Window.ClientBounds;
            (baseWidth, baseHeight) = (bounds.Width, bounds.Height);
        };
        helper.Events.Input.ButtonsChanged += OnButtonsChanged;
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

        int next = Math.Clamp(scaleIndex + dir, 0, Scales.Length - 1);
        if (next == scaleIndex)
        {
            Notify($"窗口已是 {Scales[scaleIndex]} 倍（{(dir > 0 ? "最大" : "最小")}）");
            return;
        }
        scaleIndex = next;
        int w = (int)Math.Round(baseWidth * Scales[scaleIndex]), h = (int)Math.Round(baseHeight * Scales[scaleIndex]);
        Game1.game1.SetWindowSize(w, h);
        Notify($"窗口 {Scales[scaleIndex]} 倍：{w}×{h}");
    }

    private void Notify(string text)
    {
        Monitor.Log(text, LogLevel.Info);
        if (Context.IsWorldReady)
            Game1.addHUDMessage(new HUDMessage(text) { noIcon = true });
    }
}
