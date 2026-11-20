using System;
using StardewModdingAPI;

namespace AutoDoor;

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
        gmcm.AddBoolOption(m, () => config.Enabled, v => config.Enabled = v, () => "自动开门", () => "关闭后恢复原版：要右键点门才能进屋");
    }
}
