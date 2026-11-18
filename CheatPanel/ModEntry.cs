using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

    /// <summary>禁用/恢复其它全部热键（已开启的功能保持不变）</summary>
    /// <summary>打开/关闭修改器面板</summary>
    public KeybindList OpenMenu { get; set; } = KeybindList.Parse("F8");

    public KeybindList ToggleHotkeys { get; set; } = KeybindList.Parse("LeftControl + LeftShift + Home");

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
        ("DropRate", "100% 掉落率（新刷出的怪物生效）", "NumPad5"),
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
        ("ItemAmount", "编辑悬停物品数量", "LeftControl + NumPad4"),
        ("ItemQuality", "编辑悬停物品品质", "LeftControl + NumPad5"),
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

    internal static readonly HashSet<string> OneShots = new() { "Money", "QiCoins", "QiGems", "ItemAmount", "ItemQuality" };

    internal static ModConfig cfg = new();
    private static readonly HashSet<string> active = new();
    // 待执行的悬停物品编辑：按下热键后，下一个弹出提示框的物品会被校验并修改
    private static string? pendingId, pendingName;
    private static int pendingUntil;
    // 最近一次弹出提示框的物品，用来判断按下热键时鼠标是否正悬停在物品上
    private static Item? hovered;
    private static int hoveredTick = -100;
    private static readonly List<(string Text, Color Color, int Until)> toasts = new();
    private static readonly HashSet<int> QualityCategories = new()
    {
        SObject.VegetableCategory, SObject.FruitsCategory, SObject.flowersCategory, SObject.GreensCategory,
        SObject.FishCategory, SObject.EggCategory, SObject.MilkCategory, SObject.sellAtPierresAndMarnies, SObject.artisanGoodsCategory,
    };
    private static bool hotkeysEnabled = true;
    private (string Id, string Name, KeybindList Keys)[] bindings = Array.Empty<(string, string, KeybindList)>();

    internal static bool On(string id) => active.Contains(id);

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
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { active.Clear(); pendingId = null; toasts.Clear(); ApplyGameSpeed(); };
        helper.Events.Content.AssetRequested += OnAssetRequested;
        // 原版 HUD 消息固定在左下角，这里自己画在屏幕下方居中；打开菜单时画在菜单之上
        helper.Events.Display.RenderedHud += (_, e) => { if (Game1.activeClickableMenu == null) DrawToasts(e.SpriteBatch); };
        helper.Events.Display.RenderedActiveMenu += (_, e) => DrawToasts(e.SpriteBatch);

        var h = new Harmony(ModManifest.UniqueID);
        h.Patch(AccessTools.Method(typeof(Farmer), nameof(Farmer.takeDamage)), prefix: new(typeof(ModEntry), nameof(TakeDamagePrefix)));
        h.Patch(AccessTools.Method(typeof(Farmer), nameof(Farmer.gainExperience)), prefix: new(typeof(ModEntry), nameof(GainExpPrefix)));
        h.Patch(AccessTools.Method(typeof(Farmer), nameof(Farmer.changeFriendship)), prefix: new(typeof(ModEntry), nameof(FriendshipPrefix)));
        h.Patch(AccessTools.Method(typeof(Farmer), nameof(Farmer.getMovementSpeed)), postfix: new(typeof(ModEntry), nameof(MoveSpeedPostfix)));
        h.Patch(AccessTools.Method(typeof(Axe), nameof(Axe.DoFunction)), prefix: new(typeof(ModEntry), nameof(AxePrefix)), postfix: new(typeof(ModEntry), nameof(AxePostfix)));
        h.Patch(AccessTools.Method(typeof(IClickableMenu), nameof(IClickableMenu.drawToolTip)), prefix: new(typeof(ModEntry), nameof(ToolTipPrefix)));
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

        if (cfg.ToggleHotkeys.JustPressed())
        {
            hotkeysEnabled = !hotkeysEnabled;
            Hud($"修改器热键：{(hotkeysEnabled ? "已启用" : "已禁用")}");
            return;
        }
        if (cfg.OpenMenu.JustPressed())
        {
            if (Game1.activeClickableMenu is TrainerMenu menu) menu.exitThisMenu();
            else if (Context.IsPlayerFree) Game1.activeClickableMenu = new TrainerMenu(Trigger, () => Helper.WriteConfig(cfg));
            return;
        }
        if (!hotkeysEnabled)
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
            int value = id switch
            {
                "Money" => cfg.Money, "QiCoins" => cfg.QiCoins, "QiGems" => cfg.QiGems,
                "ItemAmount" => cfg.ItemAmount, _ => cfg.ItemQuality,
            };
            if (value < 0 || (id == "ItemAmount" && value < 1) || (id == "ItemQuality" && value is < 1 or > 4))
            {
                Fail($"{name}：数值 {value} 无效，已取消");
                return;
            }
            switch (id)
            {
                case "Money": p.Money = value; break;
                case "QiCoins": p.clubCoins = value; break;
                case "QiGems": p.QiGems = value; break;
                default:
                    if (hovered != null && Game1.ticks - hoveredTick <= 2 && InPlayerStorage(hovered))
                    {
                        ApplyEdit(id, name, hovered);
                        return;
                    }
                    (pendingId, pendingName, pendingUntil) = (id, name, Game1.ticks + 600);
                    Hud($"{name}：请在 10 秒内把鼠标悬停到背包或箱子里的物品上");
                    return;
            }
            Ok($"{name}：已设置为 {value}");
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

    private static void Hud(string text) => Toast(text, Game1.textColor);

    private static void Ok(string text)
    {
        Game1.playSound("coin");
        Toast(text, new Color(60, 110, 50));
    }

    private static void Fail(string text)
    {
        Game1.playSound("clank");
        Toast(text, new Color(165, 55, 40));
    }

    private static void Toast(string text, Color color)
    {
        toasts.Add((text, color, Game1.ticks + 240)); // 约 4 秒
        if (toasts.Count > 5) toasts.RemoveAt(0);
    }

    /// <summary>在屏幕下方居中、工具栏之上，自下而上堆叠显示提示（最新的在最下面）。</summary>
    private static void DrawToasts(SpriteBatch b)
    {
        toasts.RemoveAll(t => Game1.ticks > t.Until);
        int y = Game1.uiViewport.Height - 136; // 给底部工具栏留出位置
        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            var (text, color, _) = toasts[i];
            var size = Game1.dialogueFont.MeasureString(text);
            int w = (int)size.X + 56, h = (int)size.Y + 40;
            y -= h + 8;
            int x = (Game1.uiViewport.Width - w) / 2;
            IClickableMenu.drawTextureBox(b, x, y, w, h, Color.White);
            Utility.drawTextWithShadow(b, text, Game1.dialogueFont, new Vector2(x + 28, y + 20), color);
        }
    }

    internal static void ApplyGameSpeed()
    {
        // shortcut: 只提高每秒逻辑帧数，依赖游戏使用固定时间步长；按真实毫秒计时的部分（如时钟）不会加速，需要时改为补丁 Game1.Update
        float speed = On("GameSpeed") ? Math.Max(0.1f, cfg.GameSpeed) : 1f;
        Game1.game1.TargetElapsedTime = TimeSpan.FromTicks((long)(TimeSpan.TicksPerSecond / 60.0 / speed));
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (pendingId != null && Game1.ticks > pendingUntil)
        {
            Fail($"{pendingName}：10 秒内没有悬停到物品，已取消");
            pendingId = null;
        }
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

    // 所有物品提示框（背包、箱子、工具栏、商店）都走 drawToolTip，第 4 个参数是悬停的物品
    private static void ToolTipPrefix(Item? __3)
    {
        if (__3 == null)
            return;
        (hovered, hoveredTick) = (__3, Game1.ticks);
        if (pendingId != null)
            ApplyEdit(pendingId, pendingName!, __3);
    }

    private static void ApplyEdit(string id, string name, Item item)
    {
        pendingId = null;
        string? error = EditItem(id, item, out string done);
        if (error != null) Fail($"{name}：{error}，已取消");
        else Ok($"{name}：{done}");
    }

    private static bool InPlayerStorage(Item item) =>
        Game1.player.Items.Contains(item) || (Game1.activeClickableMenu as ItemGrabMenu)?.ItemsToGrabMenu?.actualInventory.Contains(item) == true;

    /// <returns>不能修改时返回原因；成功时返回 null，并通过 done 给出结果。</returns>
    private static string? EditItem(string id, Item item, out string done)
    {
        done = "";
        string label = $"「{item.DisplayName}」";
        if (!InPlayerStorage(item))
            return $"{label}不在背包或箱子里";

        if (id == "ItemAmount")
        {
            int max = item.maximumStackSize();
            if (max <= 1)
                return $"{label}不能堆叠，无法修改数量";
            item.Stack = Math.Min(cfg.ItemAmount, max);
            done = item.Stack < cfg.ItemAmount ? $"{label}数量已设为上限 {item.Stack}" : $"{label}数量已设为 {item.Stack}";
            return null;
        }

        if (item is not SObject obj || obj.bigCraftable.Value || !QualityCategories.Contains(obj.Category))
            return $"{label}没有品质，无法修改";
        obj.Quality = new[] { 0, 1, 2, 4 }[cfg.ItemQuality - 1];
        done = $"{label}品质已设为{TrainerMenu.QualityNames[cfg.ItemQuality - 1]}";
        return null;
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
