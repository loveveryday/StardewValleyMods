using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace CheatPanel;

public class ModConfig
{
    public Dictionary<string, KeybindList> Hotkeys { get; set; } =
        ModEntry.Features.ToDictionary(f => f.Id, f => KeybindList.Parse(f.Key));

    public int Money { get; set; } = 99999999;
    public int QiCoins { get; set; } = 99999999;
    public int QiGems { get; set; } = 999;
    public int ItemAmount { get; set; } = 999;
    /// <summary>1=无星 2=银星 3=金星 4=紫星</summary>
    public int ItemQuality { get; set; } = 4;
    public float ExpMultiplier { get; set; } = 2f;
    public float FriendshipMultiplier { get; set; } = 2f;
    public float TimeSpeed { get; set; } = 0.5f;
    public float DefenseMultiplier { get; set; } = 2f;
    public float KnockbackMultiplier { get; set; } = 2f;
    public float DamageMultiplier { get; set; } = 2f;
    public float MoveSpeed { get; set; } = 2f;
    public float GameSpeed { get; set; } = 2.5f;
}

public class ModEntry : Mod
{
    // 热键布局照搬原修改器：数字键 / Ctrl+数字键 / Alt+数字键 / Shift+F1~F6
    public static readonly (string Id, string Name, string Key)[] Features =
    {
        ("SuperDamage", "超级伤害/一击必杀", "NumPad0"),
        ("GodMode", "无敌模式", "NumPad1"),
        ("InfHealth", "无限生命", "NumPad2"),
        ("InfStamina", "无限体力", "NumPad3"),
        ("InfWater", "无限水壶", "NumPad4"),
        ("DropRate", "100% 掉落率（对之后刷出的怪物生效）", "NumPad5"),
        ("Critical", "100% 暴击率", "NumPad6"),
        ("MoveSpeed", "移动速度倍率", "NumPad7"),
        ("GameSpeed", "游戏速度", "NumPad8"),
        ("OneHitTrees", "一击砍树", "NumPad9"),
        ("Defense", "防御倍率", "Add"),
        ("Knockback", "击退倍率", "Subtract"),
        ("DamageMul", "伤害倍率", "Decimal"),
        ("Money", "编辑金钱", "LeftControl + NumPad1"),
        ("QiCoins", "编辑齐币", "LeftControl + NumPad2"),
        ("QiGems", "编辑齐钻", "LeftControl + NumPad3"),
        ("ItemAmount", "编辑鼠标指向物品数量（悬停物品）", "LeftControl + NumPad4"),
        ("ItemQuality", "编辑鼠标指向物品品质（悬停物品）", "LeftControl + NumPad5"),
        ("FreezeTime", "冻结时间", "LeftControl + NumPad6"),
        ("TimeSpeed", "时间流逝速度", "LeftControl + NumPad7"),
        ("NoClip", "穿墙模式", "LeftAlt + NumPad1"),
        ("AutoWater", "自动浇水（每天早上）", "LeftAlt + NumPad2"),
        ("GrowCrops", "作物一天成熟（每天早上）", "LeftAlt + NumPad3"),
        ("InfExp", "无限技能经验", "LeftAlt + NumPad4"),
        ("ExpMul", "技能经验倍率", "LeftAlt + NumPad5"),
        ("MaxFriendship", "NPC 好感度最大", "LeftAlt + NumPad6"),
        ("FriendshipMul", "NPC 好感度倍率", "LeftAlt + NumPad7"),
        ("PetFriendship", "宠物好感度最大", "LeftShift + F1"),
        ("AnimalFriendship", "农场动物好感度最大", "LeftShift + F2"),
        ("AnimalFullness", "农场动物饱食度最大", "LeftShift + F3"),
        ("AnimalHappiness", "农场动物心情最大", "LeftShift + F4"),
        ("InstantBite", "钓鱼：瞬间咬钩", "LeftShift + F5"),
        ("InstantCatch", "钓鱼：瞬间钓起", "LeftShift + F6"),
    };

    private static readonly HashSet<string> OneShots = new() { "Money", "QiCoins", "QiGems", "ItemAmount", "ItemQuality" };

    private static ModConfig cfg = new();
    private static readonly HashSet<string> active = new();
    private static int pendingAmount, pendingQuality = -1;
    private (string Id, string Name, KeybindList Keys)[] bindings = Array.Empty<(string, string, KeybindList)>();

    private static bool On(string id) => active.Contains(id);

    public override void Entry(IModHelper helper)
    {
        cfg = helper.ReadConfig<ModConfig>();
        // 组合键优先匹配，按 Ctrl+NumPad1 时不会同时触发 NumPad1
        bindings = Features
            .Select(f => (f.Id, f.Name, Keys: cfg.Hotkeys.GetValueOrDefault(f.Id) ?? new KeybindList()))
            .OrderByDescending(b => b.Keys.Keybinds.Select(k => k.Buttons.Length).DefaultIfEmpty(0).Max())
            .ToArray();

        helper.Events.Input.ButtonsChanged += OnButtonsChanged;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecond;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { active.Clear(); ApplyGameSpeed(); };
        helper.Events.Content.AssetRequested += OnAssetRequested;

        var h = new Harmony(ModManifest.UniqueID);
        h.Patch(AccessTools.Method(typeof(Farmer), nameof(Farmer.takeDamage)), prefix: new(typeof(ModEntry), nameof(TakeDamagePrefix)));
        h.Patch(AccessTools.Method(typeof(Farmer), nameof(Farmer.gainExperience)), prefix: new(typeof(ModEntry), nameof(GainExpPrefix)));
        h.Patch(AccessTools.Method(typeof(Farmer), nameof(Farmer.changeFriendship)), prefix: new(typeof(ModEntry), nameof(FriendshipPrefix)));
        h.Patch(AccessTools.Method(typeof(Farmer), nameof(Farmer.getMovementSpeed)), postfix: new(typeof(ModEntry), nameof(MoveSpeedPostfix)));
        h.Patch(AccessTools.Method(typeof(Axe), nameof(Axe.DoFunction)), prefix: new(typeof(ModEntry), nameof(AxePrefix)), postfix: new(typeof(ModEntry), nameof(AxePostfix)));
        h.Patch(AccessTools.Method(typeof(SObject), nameof(SObject.getCategoryName)), postfix: new(typeof(ModEntry), nameof(HoverPostfix)));
        // 取参数最多的重载（真正的实现），短重载会转调它，避免倍率被叠加两次
        var damageMonster = typeof(GameLocation).GetMethods()
            .Where(m => m.Name == nameof(GameLocation.damageMonster))
            .OrderByDescending(m => m.GetParameters().Length).First();
        h.Patch(damageMonster, prefix: new(typeof(ModEntry), nameof(DamageMonsterPrefix)));
    }

    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        // 菜单打开时也响应（编辑悬停物品需要开着背包），但正在输入文字时不响应
        if (!Context.IsWorldReady || Game1.keyboardDispatcher.Subscriber != null)
            return;

        foreach (var (id, name, keys) in bindings)
        {
            if (!keys.JustPressed())
                continue;
            Trigger(id, name);
            return;
        }
    }

    private void Trigger(string id, string name)
    {
        var p = Game1.player;
        if (OneShots.Contains(id))
        {
            switch (id)
            {
                case "Money": p.Money = cfg.Money; break;
                case "QiCoins": p.clubCoins = cfg.QiCoins; break;
                case "QiGems": p.QiGems = cfg.QiGems; break;
                case "ItemAmount": pendingAmount = cfg.ItemAmount; break;
                case "ItemQuality": pendingQuality = new[] { 0, 1, 2, 4 }[Math.Clamp(cfg.ItemQuality, 1, 4) - 1]; break;
            }
            Hud(id.StartsWith("Item") ? $"{name}：请把鼠标悬停到物品上" : $"{name}：已设置");
            return;
        }

        bool on = active.Add(id) || !active.Remove(id);
        Hud($"{name}：{(on ? "开启" : "关闭")}");

        switch (id)
        {
            case "NoClip" when !on: p.ignoreCollisions = false; break;
            case "GameSpeed": ApplyGameSpeed(); break;
            case "DropRate": Helper.GameContent.InvalidateCache("Data/Monsters"); break;
        }
    }

    private static void Hud(string text) => Game1.addHUDMessage(new HUDMessage(text) { noIcon = true });

    private static void ApplyGameSpeed()
    {
        // shortcut: 只提高每秒逻辑帧数，依赖游戏使用固定时间步长；按真实毫秒计时的部分（如时钟）不会加速，需要时改为补丁 Game1.Update
        float speed = On("GameSpeed") ? Math.Max(0.1f, cfg.GameSpeed) : 1f;
        Game1.game1.TargetElapsedTime = TimeSpan.FromTicks((long)(TimeSpan.TicksPerSecond / 60.0 / speed));
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || active.Count == 0)
            return;
        var p = Game1.player;

        if (On("NoClip")) p.ignoreCollisions = true;
        if (On("InfHealth")) p.health = p.maxHealth;
        if (On("InfStamina")) p.Stamina = p.MaxStamina;
        if (On("InfWater"))
            foreach (var can in p.Items.OfType<WateringCan>())
                can.WaterLeft = can.waterCanMax;

        if (On("FreezeTime"))
            Game1.gameTimeInterval = 0;
        else if (On("TimeSpeed") && Game1.shouldTimePass())
            Game1.gameTimeInterval = Math.Max(0, Game1.gameTimeInterval + (int)(Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds * (cfg.TimeSpeed - 1)));

        if (On("InstantBite") && p.CurrentTool is FishingRod rod && rod.isFishing && !rod.isNibbling && rod.timeUntilFishingBite > 0)
            rod.fishingBiteAccumulator = rod.timeUntilFishingBite + 1;
        if (On("InstantCatch") && Game1.activeClickableMenu is BobberBar bar)
            bar.distanceFromCatching = 1f;
    }

    private void OnOneSecond(object? sender, OneSecondUpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !(On("PetFriendship") || On("AnimalFriendship") || On("AnimalFullness") || On("AnimalHappiness")))
            return;
        Utility.ForEachLocation(loc =>
        {
            if (On("PetFriendship"))
                foreach (var pet in loc.characters.OfType<Pet>())
                    pet.friendshipTowardFarmer.Value = 1000;
            foreach (var a in loc.animals.Values)
            {
                if (On("AnimalFriendship")) a.friendshipTowardFarmer.Value = 1000;
                if (On("AnimalFullness")) a.fullness.Value = 255;
                if (On("AnimalHappiness")) a.happiness.Value = 255;
            }
            return true;
        });
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        bool water = On("AutoWater"), grow = On("GrowCrops");
        if (!Context.IsMainPlayer || !(water || grow))
            return;
        Utility.ForEachLocation(loc =>
        {
            var dirts = loc.terrainFeatures.Values.OfType<HoeDirt>()
                .Concat(loc.objects.Values.OfType<IndoorPot>().Select(pot => pot.hoeDirt.Value));
            foreach (var dirt in dirts)
            {
                if (water) dirt.state.Value = HoeDirt.watered;
                if (grow) dirt.crop?.growCompletely();
            }
            return true;
        });
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!On("DropRate") || !e.NameWithoutLocale.IsEquivalentTo("Data/Monsters"))
            return;
        e.Edit(asset =>
        {
            var data = asset.AsDictionary<string, string>().Data;
            foreach (var key in data.Keys.ToList())
            {
                var fields = data[key].Split('/');
                if (fields.Length <= 6)
                    continue;
                // 第 7 段是 "物品ID 概率 物品ID 概率 ..."，把概率全部改成 1
                var drops = fields[6].Split(' ');
                for (int i = 1; i < drops.Length; i += 2)
                    drops[i] = "1";
                fields[6] = string.Join(' ', drops);
                data[key] = string.Join('/', fields);
            }
        });
    }

    // ---- Harmony 补丁，参数按位置取（__0 = 第 1 个参数） ----

    private static bool TakeDamagePrefix(Farmer __instance, ref int __0)
    {
        if (!__instance.IsLocalPlayer) return true;
        if (On("GodMode")) return false;
        if (On("Defense")) __0 = (int)(__0 / Math.Max(0.01f, cfg.DefenseMultiplier));
        return true;
    }

    private static void GainExpPrefix(Farmer __instance, ref int __1)
    {
        if (!__instance.IsLocalPlayer) return;
        if (On("InfExp")) __1 = 100000;
        else if (On("ExpMul")) __1 = (int)(__1 * cfg.ExpMultiplier);
    }

    private static void FriendshipPrefix(Farmer __instance, ref int __0)
    {
        if (!__instance.IsLocalPlayer) return;
        if (On("MaxFriendship")) __0 = 3500; // 14 心（配偶上限），游戏内部会再按角色上限截断
        else if (On("FriendshipMul") && __0 > 0) __0 = (int)(__0 * cfg.FriendshipMultiplier);
    }

    private static void MoveSpeedPostfix(Farmer __instance, ref float __result)
    {
        if (On("MoveSpeed") && __instance.IsLocalPlayer) __result *= cfg.MoveSpeed;
    }

    private static void AxePrefix(Axe __instance, out bool __state)
    {
        __state = On("OneHitTrees");
        if (__state) __instance.additionalPower.Value += 1000;
    }

    private static void AxePostfix(Axe __instance, bool __state)
    {
        if (__state) __instance.additionalPower.Value -= 1000;
    }

    // 原修改器同样挂在 getCategoryName 上：悬停提示框会调用它
    private static void HoverPostfix(SObject __instance)
    {
        if (pendingAmount > 0) { __instance.Stack = pendingAmount; pendingAmount = 0; }
        if (pendingQuality >= 0) { __instance.Quality = pendingQuality; pendingQuality = -1; }
    }

    // damageMonster(区域, 最小伤害, 最大伤害, 是否炸弹, 击退, 精准, 暴击率, 暴击倍数, ...)
    private static void DamageMonsterPrefix(ref int __1, ref int __2, ref float __4, ref float __6)
    {
        if (On("SuperDamage")) { __1 = __2 = 99999; }
        else if (On("DamageMul")) { __1 = (int)(__1 * cfg.DamageMultiplier); __2 = (int)(__2 * cfg.DamageMultiplier); }
        if (On("Knockback")) __4 *= cfg.KnockbackMultiplier;
        if (On("Critical")) __6 = 1f;
    }
}
