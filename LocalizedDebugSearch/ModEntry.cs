using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace LocalizedDebugSearch;

/// <summary>原版 debug 命令按名字查找时只认英文内部名；这里在英文名找不到时，再按当前语言名查找。</summary>
public class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        var h = new Harmony(ModManifest.UniqueID);
        foreach (string cmd in new[] { nameof(DebugCommands.DefaultHandlers.FuzzyItemNamed), nameof(DebugCommands.DefaultHandlers.GetIndex) })
            h.Patch(AccessTools.Method(typeof(DebugCommands.DefaultHandlers), cmd), prefix: new(typeof(ModEntry), nameof(DebugItemNamePrefix)));
        h.Patch(AccessTools.Method(typeof(DebugCommands.DefaultHandlers), nameof(DebugCommands.DefaultHandlers.ItemNamed)), prefix: new(typeof(ModEntry), nameof(DebugItemNamedPrefix)));
        h.Patch(AccessTools.Method(typeof(DebugCommands.DefaultHandlers), nameof(DebugCommands.DefaultHandlers.Lookup)), prefix: new(typeof(ModEntry), nameof(DebugLookupPrefix)));
        // 这两个只有调试命令调用（已查 1.6.15 DLL），直接补在 Utility 上，一次覆盖所有 NPC / 地点命令
        h.Patch(AccessTools.Method(typeof(Utility), nameof(Utility.fuzzyCharacterSearch)), postfix: new(typeof(ModEntry), nameof(FuzzyNpcPostfix)));
        h.Patch(AccessTools.Method(typeof(Utility), nameof(Utility.fuzzyLocationSearch)), postfix: new(typeof(ModEntry), nameof(FuzzyLocationPostfix)));
    }

    // ---- Harmony 补丁，参数按位置取（__0 = 第 1 个参数） ----

    // debug f / fin / getindex 原版只按英文内部名模糊搜；英文搜不到时按当前语言名搜，再把参数换成内部名交回原版处理
    // 只补这两个命令，不补 Utility.fuzzyItemSearch 本身，因为收集包生成和特殊订单也调用它
    // shortcut: 不同类型的物品可能内部名相同，换成内部名后原版可能选中同名的另一个物品；遇到再改成直接按 ID 生成
    private static void DebugItemNamePrefix(string[] __0)
    {
        if (__0.Length < 2 || Utility.fuzzyItemSearch(__0[1]) != null)
            return;
        if (Utility.fuzzyItemSearch(__0[1], useLocalizedNames: true) is Item item)
            __0[1] = ItemRegistry.GetData(item.QualifiedItemId).InternalName;
    }

    // debug in / lu 原版要求和 (O) 物品内部名完全相同；不是内部名但等于某个当前语言名时，换成内部名
    private static string? ObjectInternalNameFor(string name)
    {
        var type = ItemRegistry.GetObjectTypeDefinition();
        var all = type.GetAllIds().Select(type.GetData).Where(d => d != null).ToList();
        if (all.Any(d => string.Equals(d.InternalName, name, StringComparison.OrdinalIgnoreCase)))
            return null;
        return all.FirstOrDefault(d => string.Equals(d.DisplayName, name, StringComparison.OrdinalIgnoreCase))?.InternalName;
    }

    private static void DebugItemNamedPrefix(string[] __0)
    {
        if (__0.Length >= 2 && ObjectInternalNameFor(__0[1]) is string name)
            __0[1] = name;
    }

    // lu 把第 1 个参数之后的全部内容当作名字，所以整组参数一起替换
    private static void DebugLookupPrefix(ref string[] __0)
    {
        if (__0.Length >= 2 && ObjectInternalNameFor(string.Join(" ", __0.Skip(1))) is string name)
            __0 = new[] { __0[0], name };
    }

    // 原版按 NPC.Name 模糊搜；搜不到时按当前语言名再搜一次
    private static void FuzzyNpcPostfix(string __0, bool __1, ref NPC? __result)
    {
        if (__result != null)
            return;
        var byName = new Dictionary<string, NPC>();
        Utility.ForEachCharacter(npc =>
        {
            if ((!__1 || npc.IsVillager) && !string.IsNullOrEmpty(npc.displayName))
                byName.TryAdd(npc.displayName, npc);
            return true;
        });
        if (Utility.fuzzySearch(__0, byName.Keys) is string match)
            __result = byName[match];
    }

    // 原版按地点内部名模糊搜；搜不到时按当前语言名再搜一次。室内会沿用所在地点的名字，同名时取先遍历到的
    private static void FuzzyLocationPostfix(string __0, ref GameLocation? __result)
    {
        if (__result != null)
            return;
        var byName = new Dictionary<string, GameLocation>();
        Utility.ForEachLocation(loc =>
        {
            if (!string.IsNullOrEmpty(loc.DisplayName))
                byName.TryAdd(loc.DisplayName, loc);
            return true;
        });
        if (Utility.fuzzySearch(__0, byName.Keys) is string match)
            __result = byName[match];
    }
}
