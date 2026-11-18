using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace CheatPanel;

/// <summary>修改器式面板：每行一个功能，左侧开关/执行按钮，右侧热键和数值。</summary>
internal class TrainerMenu : IClickableMenu
{
    private const int RowH = 56, Header = 96, Footer = 48;

    // 功能 ID → ModConfig 中对应的数值属性
    private static readonly Dictionary<string, string> ValueProps = new()
    {
        ["Money"] = nameof(ModConfig.Money),
        ["QiCoins"] = nameof(ModConfig.QiCoins),
        ["QiGems"] = nameof(ModConfig.QiGems),
        ["ItemAmount"] = nameof(ModConfig.ItemAmount),
        ["ItemQuality"] = nameof(ModConfig.ItemQuality),
        ["ExpMul"] = nameof(ModConfig.ExpMultiplier),
        ["FriendshipMul"] = nameof(ModConfig.FriendshipMultiplier),
        ["TimeSpeed"] = nameof(ModConfig.TimeSpeed),
        ["Defense"] = nameof(ModConfig.DefenseMultiplier),
        ["Knockback"] = nameof(ModConfig.KnockbackMultiplier),
        ["DamageMul"] = nameof(ModConfig.DamageMultiplier),
        ["MoveSpeed"] = nameof(ModConfig.MoveSpeed),
        ["GameSpeed"] = nameof(ModConfig.GameSpeed),
    };
    internal static readonly string[] QualityNames = { "无星", "银星", "金星", "紫星" };

    private readonly Action<string, string> trigger;
    private readonly Action save;
    private readonly Dictionary<string, TextBox> boxes = new();
    private int scroll;

    public TrainerMenu(Action<string, string> trigger, Action save)
    {
        this.trigger = trigger;
        this.save = save;
        var tex = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
        foreach (var id in ValueProps.Keys.Where(id => id != "ItemQuality" && Prop(id).PropertyType == typeof(int)))
            boxes[id] = new TextBox(tex, null, Game1.smallFont, Game1.textColor) { numbersOnly = true, textLimit = 9, Width = 230, Text = Get(id).ToString() };
        Layout();
    }

    private int Visible => Math.Max(1, (height - Header - Footer) / RowH);
    private int ValueX => xPositionOnScreen + width - 310;

    private void Layout()
    {
        width = Math.Min(1040, Game1.uiViewport.Width - 64);
        height = Math.Min(Header + Footer + RowH * ModEntry.Features.Length, Game1.uiViewport.Height - 64);
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
        initializeUpperRightCloseButton();
        scroll = Math.Clamp(scroll, 0, Math.Max(0, ModEntry.Features.Length - Visible));
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();

    private IEnumerable<(string Id, string Name, int X, int Y)> Rows()
    {
        for (int i = scroll; i < Math.Min(ModEntry.Features.Length, scroll + Visible); i++)
            yield return (ModEntry.Features[i].Id, ModEntry.Features[i].Name, xPositionOnScreen + 40, yPositionOnScreen + Header + (i - scroll) * RowH);
    }

    private static Rectangle ToggleRect(string id, int x, int y) =>
        ModEntry.OneShots.Contains(id) ? new Rectangle(x, y + 4, 96, 44) : new Rectangle(x + 30, y + 8, 36, 36);

    private static PropertyInfo Prop(string id) => typeof(ModConfig).GetProperty(ValueProps[id])!;
    private static object Get(string id) => Prop(id).GetValue(ModEntry.cfg)!;

    private static void Set(string id, object value)
    {
        Prop(id).SetValue(ModEntry.cfg, value);
        if (id == "GameSpeed") ModEntry.ApplyGameSpeed();
    }

    private void Step(string id, int dir)
    {
        Game1.playSound("smallSelect");
        if (id == "ItemQuality")
        {
            Set(id, Math.Clamp((int)Get(id) + dir, 1, 4));
            return;
        }
        float v = (float)Get(id);
        float step = v < 1 || (v == 1 && dir < 0) ? 0.1f : 0.5f;
        Set(id, (float)Math.Round(Math.Clamp(v + dir * step, 0.1f, 100f), 1));
    }

    private void Commit()
    {
        foreach (var (id, box) in boxes)
        {
            if (int.TryParse(box.Text, out int v) && v >= 0) Set(id, v);
            else box.Text = Get(id).ToString();
            box.Selected = false;
        }
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound); // 右上角关闭按钮
        Commit();
        foreach (var (id, name, rx, ry) in Rows())
        {
            if (ToggleRect(id, rx, ry).Contains(x, y))
            {
                Game1.playSound("drumkit6");
                trigger(id, name);
                return;
            }
            if (!ValueProps.ContainsKey(id))
                continue;
            if (boxes.TryGetValue(id, out var box))
            {
                if (new Rectangle(ValueX, ry + 4, box.Width, 48).Contains(x, y)) box.Selected = true;
            }
            else if (new Rectangle(ValueX, ry + 8, 40, 40).Contains(x, y)) Step(id, -1);
            else if (new Rectangle(ValueX + 200, ry + 8, 40, 40).Contains(x, y)) Step(id, 1);
        }
    }

    public override void receiveScrollWheelAction(int direction) =>
        scroll = Math.Clamp(scroll - Math.Sign(direction), 0, Math.Max(0, ModEntry.Features.Length - Visible));

    public override void receiveKeyPress(Keys key)
    {
        // 输入数字时不让 E/Esc 关掉面板；Esc/回车只结束输入
        if (boxes.Values.Any(b => b.Selected))
        {
            if (key is Keys.Escape or Keys.Enter) Commit();
            return;
        }
        base.receiveKeyPress(key);
    }

    protected override void cleanupBeforeExit()
    {
        Commit();
        save();
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);
        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);
        SpriteText.drawStringHorizontallyCenteredAt(b, "修改器", xPositionOnScreen + width / 2, yPositionOnScreen + 32);

        var font = Game1.smallFont;
        foreach (var (id, name, x, y) in Rows())
        {
            var toggle = ToggleRect(id, x, y);
            if (ModEntry.OneShots.Contains(id))
            {
                drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), toggle.X, toggle.Y, toggle.Width, toggle.Height, Color.White, 4f, false);
                Utility.drawTextWithShadow(b, "执行", font, new Vector2(toggle.Center.X - font.MeasureString("执行").X / 2, toggle.Y + 8), Game1.textColor);
            }
            else
            {
                b.Draw(Game1.mouseCursors, new Vector2(toggle.X, toggle.Y), ModEntry.On(id) ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked,
                    Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
            }

            Utility.drawTextWithShadow(b, name, font, new Vector2(x + 116, y + 12), ModEntry.On(id) ? Color.DarkGreen : Game1.textColor);
            Utility.drawTextWithShadow(b, KeyLabel(id), font, new Vector2(ValueX - 230, y + 12), Color.DimGray);

            if (!ValueProps.ContainsKey(id))
                continue;
            if (boxes.TryGetValue(id, out var box))
            {
                box.X = ValueX;
                box.Y = y + 4;
                box.Draw(b);
                continue;
            }
            string text = id == "ItemQuality" ? QualityNames[Math.Clamp((int)Get(id), 1, 4) - 1] : $"x{(float)Get(id):0.0}";
            b.Draw(Game1.mouseCursors, new Vector2(ValueX, y + 12), OptionsPlusMinus.minusButtonSource, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
            Utility.drawTextWithShadow(b, text, font, new Vector2(ValueX + 120 - font.MeasureString(text).X / 2, y + 12), Game1.textColor);
            b.Draw(Game1.mouseCursors, new Vector2(ValueX + 200, y + 12), OptionsPlusMinus.plusButtonSource, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
        }

        int last = Math.Min(ModEntry.Features.Length, scroll + Visible);
        string footer = $"{scroll + 1}-{last} / {ModEntry.Features.Length}    滚轮翻页 · {ModEntry.cfg.OpenMenu} 关闭 · 修改后自动保存";
        Utility.drawTextWithShadow(b, footer, font, new Vector2(xPositionOnScreen + 40, yPositionOnScreen + height - Footer - 4), Color.DimGray);

        base.draw(b);
        drawMouse(b);
    }

    private static string KeyLabel(string id) =>
        (ModEntry.cfg.Hotkeys.GetValueOrDefault(id)?.ToString() ?? "")
            .Replace("LeftControl", "Ctrl").Replace("LeftAlt", "Alt").Replace("LeftShift", "Shift")
            .Replace("NumPad", "Num").Replace("Add", "Num+").Replace("Subtract", "Num-").Replace("Decimal", "Num.");
}
