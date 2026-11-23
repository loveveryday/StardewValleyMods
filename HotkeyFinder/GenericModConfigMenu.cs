using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace HotkeyFinder;

/// <summary>Generic Mod Config Menu 的 API，只声明用到的方法；签名需与 GMCM 一致，SMAPI 按签名对接。</summary>
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
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
        gmcm.AddKeybindList(m, () => config.OpenMenu, v => config.OpenMenu = v, () => "打开快捷键列表");
    }
}
