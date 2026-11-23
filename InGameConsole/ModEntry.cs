using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace InGameConsole;

public class ModConfig
{
    public KeybindList Toggle { get; set; } = KeybindList.Parse("LeftControl + OemQuestion, RightControl + OemQuestion");

    // 控制台窗口的位置和大小；null 表示还没拖过，用默认值（屏幕中间）
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>控制台文字的字号倍数，控制台里按 Ctrl + = / Ctrl + - 调整。</summary>
    public float FontScale { get; set; } = 1f;
}

public partial class ModEntry : Mod
{
    private ModConfig config = new();
    private SmapiBridge bridge = null!;

    public override void Entry(IModHelper helper)
    {
        LineEditor.SelfCheck();
        config = helper.ReadConfig<ModConfig>();
        try
        {
            bridge = new SmapiBridge();
            bridge.HookConsoleOutput(new Harmony(ModManifest.UniqueID));
        }
        catch (Exception ex)
        {
            Monitor.Log($"找不到本地编译的 SMAPI 里的控制台内部类型，游戏内控制台已停用：{ex.Message}", LogLevel.Error);
            return;
        }

        helper.Events.GameLoop.GameLaunched += (_, _) => RegisterConfigMenu();
        helper.Events.GameLoop.UpdateTicked += (_, _) =>
        {
            while (SmapiBridge.PendingLogs.TryDequeue(out var line))
                Scrollback.Add(line);
        };
        helper.Events.Input.ButtonsChanged += OnButtonsChanged;
    }

    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (config.Toggle.JustPressed())
        {
            Helper.Input.SuppressActiveKeybinds(config.Toggle); // 不然关掉控制台后，游戏会把这次的 / 当成打开聊天框
            if (Game1.activeClickableMenu is ConsoleMenu console)
                console.Close();
            else if (Game1.keyboardDispatcher.Subscriber == null) // 别的输入框（聊天、取名）正在打字时不抢
                Game1.activeClickableMenu = new ConsoleMenu(Game1.activeClickableMenu, bridge, config, () => Helper.WriteConfig(config));
            return;
        }

        // 控制台打开时键盘只归控制台：不然游戏还会响应 - = 切换工具栏格子等按键。
        // 修饰键不屏蔽：被屏蔽的键在 SMAPI 里会一直显示为松开，Ctrl 被屏蔽后 Ctrl+/ 就关不掉控制台了
        if (Game1.activeClickableMenu is ConsoleMenu)
            foreach (SButton button in e.Pressed)
                if (button.TryGetKeyboard(out var key) && key is not (Keys.LeftControl or Keys.RightControl or Keys.LeftAlt or Keys.RightAlt or Keys.LeftShift or Keys.RightShift))
                    Helper.Input.Suppress(button);
    }
}
