using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace MapTweaks;

public class ModConfig
{
    public bool StayOnBlankClick { get; set; } = true;
    public bool EscapeBackToMenu { get; set; } = true;
    public bool MenuKeyBackToMenu { get; set; } = true;
    public bool CloseButtonBackToMenu { get; set; } = true;
}

public partial class ModEntry : Mod
{
    private ModConfig config = new();

    // 按 M 直接打开的那个菜单；从其他标签页切到地图的不算
    private GameMenu? directMap;

    public override void Entry(IModHelper helper)
    {
        config = helper.ReadConfig<ModConfig>();
        helper.Events.GameLoop.GameLaunched += (_, _) => RegisterConfigMenu();
        helper.Events.Display.MenuChanged += (_, e) =>
            directMap = e.NewMenu is GameMenu menu && menu.currentTab == GameMenu.mapTab ? menu : null;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        // 按 M 打开后一旦离开地图页（关掉第一项时点空白会跳背包），再切回地图就算从标签页切来的
        if (directMap?.currentTab != GameMenu.mapTab)
            directMap = null;

        if (Game1.activeClickableMenu is not GameMenu menu || menu.currentTab != GameMenu.mapTab
            || menu.GetCurrentPage() is not MapPage page)
            return;

        // 菜单收到的是 UI 缩放后的坐标
        int x = Game1.getMouseX(ui_scale: true), y = Game1.getMouseY(ui_scale: true);
        bool onClose = e.Button == SButton.MouseLeft && menu.upperRightCloseButton?.containsPoint(x, y) == true;

        if (menu != directMap)
        {
            bool escape = e.Button == SButton.Escape;
            // 菜单键默认是 E（Escape 也在原版菜单键列表里，归上一项管）
            bool menuKey = !escape && e.Button.TryGetKeyboard(out Keys key) && Game1.options.doesInputListContain(Game1.options.menuButton, key);
            if ((escape && config.EscapeBackToMenu) || (menuKey && config.MenuKeyBackToMenu) || (onClose && config.CloseButtonBackToMenu))
            {
                Helper.Input.Suppress(e.Button);
                menu.changeTab(menu.lastOpenedNonMapTab);
            }
            return;
        }

        if (!config.StayOnBlankClick || e.Button != SButton.MouseLeft || onClose)
            return;
        foreach (ClickableComponent point in page.points.Values)
            if (point.containsPoint(x, y))
                return; // 点在地点上照常交给游戏（个别地点有彩蛋音效）

        Helper.Input.Suppress(e.Button);
    }
}
