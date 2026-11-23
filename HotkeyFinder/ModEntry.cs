using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace HotkeyFinder;

public class ModConfig
{
    public KeybindList OpenMenu { get; set; } = KeybindList.Parse("F9");
}

public partial class ModEntry : Mod
{
    private ModConfig config = new();
    private HotkeyScanner scanner = null!;

    public override void Entry(IModHelper helper)
    {
        HotkeyScanner.SelfCheck();
        config = helper.ReadConfig<ModConfig>();
        scanner = new HotkeyScanner(helper, Monitor);

        helper.ConsoleCommands.Add("hotkeys", "列出所有已加载 MOD 和原版游戏的快捷键，冲突排在最前。", (_, _) =>
        {
            foreach (var (text, warn) in scanner.Report())
                Monitor.Log(text, warn ? LogLevel.Warn : LogLevel.Info);
        });
        helper.Events.GameLoop.GameLaunched += (_, _) => RegisterConfigMenu();
        helper.Events.Input.ButtonsChanged += OnButtonsChanged;
    }

    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!config.OpenMenu.JustPressed())
            return;
        if (Game1.activeClickableMenu is HotkeyMenu menu)
            menu.exitThisMenu();
        else if (Context.IsPlayerFree)
            Game1.activeClickableMenu = new HotkeyMenu(scanner.Report());
    }
}
