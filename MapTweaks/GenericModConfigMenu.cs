using System;
using StardewModdingAPI;

namespace MapTweaks;

/// <summary>Generic Mod Config Menu 的 API，只声明用到的方法；签名需与 GMCM 一致，SMAPI 按签名对接。</summary>
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
}

public partial class ModEntry
{
    /// <summary>装了 GMCM 时注册配置页；没装就什么都不做。</summary>
    private void RegisterConfigMenu()
    {
        var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm == null)
            return;

        var m = ModManifest;
        gmcm.Register(m, reset: () => config = new ModConfig(), save: () => Helper.WriteConfig(config));
        gmcm.AddBoolOption(m, () => config.StayOnBlankClick, v => config.StayOnBlankClick = v, () => "点空白处不跳背包", () => "按 M 打开地图后，点地图空白处不再跳到背包");
        gmcm.AddBoolOption(m, () => config.EscapeBackToMenu, v => config.EscapeBackToMenu = v, () => "ESC 回到菜单", () => "从菜单标签页切到地图时，按 ESC 回到上一个标签页，而不是关掉整个菜单");
        gmcm.AddBoolOption(m, () => config.MenuKeyBackToMenu, v => config.MenuKeyBackToMenu = v, () => "菜单键回到菜单", () => "从菜单标签页切到地图时，按菜单键（默认 E）回到上一个标签页，而不是关掉整个菜单");
        gmcm.AddBoolOption(m, () => config.CloseButtonBackToMenu, v => config.CloseButtonBackToMenu = v, () => "关闭按钮回到菜单", () => "从菜单标签页切到地图时，点右上角关闭按钮回到上一个标签页，而不是关掉整个菜单");
    }
}
