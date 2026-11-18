using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace CheatPanel;

/// <summary>Generic Mod Config Menu 的 API，只声明用到的方法；签名需与 GMCM 一致，SMAPI 按签名对接。</summary>
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);
    void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string>? formatValue = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue, Func<string> name, Func<string>? tooltip = null, float? min = null, float? max = null, float? interval = null, Func<float, string>? formatValue = null, string? fieldId = null);
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
        Monitor.Log("正在注册 Generic Mod Config Menu 配置页", LogLevel.Trace);
        gmcm.Register(m,
            reset: () => cfg = new ModConfig(),
            save: () =>
            {
                Helper.WriteConfig(cfg);
                RebuildBindings();
                ApplyGameSpeed();
            });

        gmcm.AddSectionTitle(m, () => "通用");
        gmcm.AddKeybindList(m, () => cfg.OpenMenu, v => cfg.OpenMenu = v, () => "打开修改器面板");
        gmcm.AddKeybindList(m, () => cfg.ToggleHotkeys, v => cfg.ToggleHotkeys = v, () => "启用/禁用全部热键");

        gmcm.AddSectionTitle(m, () => "数值");
        AddInt(gmcm, "Money", "金钱", () => cfg.Money, v => cfg.Money = v);
        AddInt(gmcm, "QiCoins", "齐币", () => cfg.QiCoins, v => cfg.QiCoins = v);
        AddInt(gmcm, "QiGems", "齐钻", () => cfg.QiGems, v => cfg.QiGems = v);
        AddInt(gmcm, "ItemAmount", "悬停物品数量", () => cfg.ItemAmount, v => cfg.ItemAmount = v);
        gmcm.AddNumberOption(m, () => cfg.ItemQuality, v => cfg.ItemQuality = v, () => "悬停物品品质",
            min: IntRanges["ItemQuality"].Min, max: IntRanges["ItemQuality"].Max, interval: 1,
            formatValue: v => TrainerMenu.QualityNames[Math.Clamp(v, 1, 4) - 1]);

        gmcm.AddSectionTitle(m, () => "倍率");
        AddFloat(gmcm, "MoveSpeed", () => cfg.MoveSpeed, v => cfg.MoveSpeed = v);
        AddFloat(gmcm, "GameSpeed", () => cfg.GameSpeed, v => cfg.GameSpeed = v);
        AddFloat(gmcm, "Defense", () => cfg.DefenseMultiplier, v => cfg.DefenseMultiplier = v);
        AddFloat(gmcm, "Knockback", () => cfg.KnockbackMultiplier, v => cfg.KnockbackMultiplier = v);
        AddFloat(gmcm, "DamageMul", () => cfg.DamageMultiplier, v => cfg.DamageMultiplier = v);
        AddFloat(gmcm, "TimeSpeed", () => cfg.TimeSpeed, v => cfg.TimeSpeed = v);
        AddFloat(gmcm, "ExpMul", () => cfg.ExpMultiplier, v => cfg.ExpMultiplier = v);
        AddFloat(gmcm, "FriendshipMul", () => cfg.FriendshipMultiplier, v => cfg.FriendshipMultiplier = v);

        gmcm.AddSectionTitle(m, () => "功能热键");
        foreach (var (id, name, _) in Features)
            gmcm.AddKeybindList(m, () => cfg.Hotkeys.GetValueOrDefault(id) ?? new KeybindList(), v => cfg.Hotkeys[id] = v, () => name);
    }

    private void AddInt(IGenericModConfigMenuApi gmcm, string id, string name, Func<int> get, Action<int> set)
    {
        var (min, max) = IntRanges[id];
        gmcm.AddNumberOption(ModManifest, get, set, () => name, min: min, max: max);
    }

    private void AddFloat(IGenericModConfigMenuApi gmcm, string id, Func<float> get, Action<float> set)
    {
        // 经验、好感倍率不在输入框范围表里，沿用面板里 −/+ 的范围
        var (min, max) = FloatRanges.TryGetValue(id, out var r) ? r : (0.1f, 100f);
        string name = Array.Find(Features, f => f.Id == id).Name;
        gmcm.AddNumberOption(ModManifest, get, set, () => name, min: min, max: max, interval: 0.1f, formatValue: v => $"x{FormatFloat(v)}");
    }
}
