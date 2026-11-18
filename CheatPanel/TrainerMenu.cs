using System;
using System.Collections.Generic;
using System.Globalization;
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
    // 和背包里"目前持有现金"一行同一字体；行高、列宽都按这个字体量出来
    private static SpriteFont Font => Game1.dialogueFont;
    private static int RowH => Math.Max(56, Font.LineSpacing + 16);
    private static int Footer => Font.LineSpacing + 32;
    private const int Header = 96;

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
    // 面板第一行是热键总开关，其后是全部功能
    private const string HotkeysId = "__Hotkeys";
    private static readonly (string Id, string Name)[] Entries =
        ModEntry.Features.Select(f => (f.Id, f.Name)).Prepend((HotkeysId, "启用全部热键")).ToArray();

    internal static readonly string[] QualityNames = { "无星", "银星", "金星", "紫星" };

    private readonly Action<string, string> trigger;
    private readonly Action save;
    private readonly Dictionary<string, NumberBox> boxes = new();
    private string? lastClickedBox;
    private NumberBox? dragging;
    private double lastClickTime;
    private int scroll, keyOffset, valueOffset;

    public TrainerMenu(Action<string, string> trigger, Action save)
    {
        this.trigger = trigger;
        this.save = save;
        var tex = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
        foreach (var id in ValueProps.Keys)
        {
            if (id != "ItemQuality" && ModEntry.IntRanges.TryGetValue(id, out var ints))
                boxes[id] = new NumberBox(tex, ints.Max.ToString().Length, 0) { Text = Math.Clamp((int)Get(id), ints.Min, ints.Max).ToString() };
            else if (ModEntry.FloatRanges.TryGetValue(id, out var floats))
                boxes[id] = new NumberBox(tex, ((int)floats.Max).ToString().Length, 2) { Text = ModEntry.FormatFloat(Math.Clamp((float)Get(id), floats.Min, floats.Max)) };
        }
        Layout();
    }

    private int Visible => Math.Max(1, (height - Header - Footer) / RowH);
    private int KeyX => xPositionOnScreen + keyOffset;
    private int ValueX => xPositionOnScreen + valueOffset;

    private void Layout()
    {
        int nameWidth = (int)Entries.Max(f => Font.MeasureString(f.Name).X);
        int keyWidth = (int)Entries.Max(f => Font.MeasureString(KeyLabel(f.Id)).X);
        keyOffset = 40 + 136 + nameWidth + 32;
        valueOffset = keyOffset + keyWidth + 32;
        width = Math.Min(valueOffset + 240 + 48, Game1.uiViewport.Width - 64);
        height = Math.Min(Header + Footer + RowH * Entries.Length, Game1.uiViewport.Height - 64);
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
        initializeUpperRightCloseButton();
        scroll = Math.Clamp(scroll, 0, Math.Max(0, Entries.Length - Visible));
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();

    private IEnumerable<(string Id, string Name, int X, int Y)> Rows()
    {
        for (int i = scroll; i < Math.Min(Entries.Length, scroll + Visible); i++)
            yield return (Entries[i].Id, Entries[i].Name, xPositionOnScreen + 40, yPositionOnScreen + Header + (i - scroll) * RowH);
    }

    private static Rectangle ToggleRect(string id, int x, int y) =>
        ModEntry.OneShots.Contains(id) ? new Rectangle(x, y + RowH / 2 - 26, 116, 52) : new Rectangle(x + 40, y + RowH / 2 - 18, 36, 36);

    /// <summary>让一行文字在行内垂直居中的 Y 坐标。</summary>
    private static float TextY(int rowY) => rowY + (RowH - Font.LineSpacing) / 2f + 4;

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
            Set(id, Math.Clamp((int)Get(id) + dir, ModEntry.IntRanges[id].Min, ModEntry.IntRanges[id].Max));
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
            if (ModEntry.FloatRanges.TryGetValue(id, out var floats))
            {
                if (float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f >= floats.Min && f <= floats.Max) Set(id, f);
                else box.Text = ModEntry.FormatFloat((float)Get(id));
            }
            else
            {
                var (min, max) = ModEntry.IntRanges[id];
                if (int.TryParse(box.Text, out int v) && v >= min && v <= max) Set(id, v);
                else box.Text = Get(id).ToString();
            }
            box.Selected = false;
            box.ClearSelection();
        }
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound); // 右上角关闭按钮
        Commit();
        foreach (var (id, name, rx, ry) in Rows())
        {
            if (Disabled(id))
                continue;
            if (ToggleRect(id, rx, ry).Contains(x, y))
            {
                Game1.playSound("drumkit6");
                if (id == HotkeysId) ModEntry.ToggleHotkeys();
                else trigger(id, name);
                return;
            }
            if (!ValueProps.ContainsKey(id))
                continue;
            if (boxes.TryGetValue(id, out var box))
            {
                if (new Rectangle(ValueX, ry + RowH / 2 - 24, box.Width, 48).Contains(x, y))
                {
                    // 500 毫秒内连点同一个输入框算双击：全选；否则把光标放到点击处，按住拖动可选择
                    double now = Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
                    bool doubleClick = lastClickedBox == id && now - lastClickTime < 500;
                    box.Selected = true;
                    if (doubleClick) box.SelectAll();
                    else
                    {
                        box.PlaceCaret(box.IndexAt(x), extend: false);
                        dragging = box;
                    }
                    (lastClickedBox, lastClickTime) = (doubleClick ? null : id, now);
                }
            }
            else if (new Rectangle(ValueX, ry + RowH / 2 - 20, 40, 40).Contains(x, y)) Step(id, -1);
            else if (new Rectangle(ValueX + 200, ry + RowH / 2 - 20, 40, 40).Contains(x, y)) Step(id, 1);
        }
    }

    public override void receiveScrollWheelAction(int direction) =>
        scroll = Math.Clamp(scroll - Math.Sign(direction), 0, Math.Max(0, Entries.Length - Visible));

    public override void leftClickHeld(int x, int y) => dragging?.PlaceCaret(dragging.IndexAt(x), extend: true);

    public override void releaseLeftClick(int x, int y) => dragging = null;

    public override void receiveKeyPress(Keys key)
    {
        // 输入数字时不让 E/Esc 关掉面板；Esc/回车只结束输入，方向键等交给输入框
        var editing = boxes.Values.FirstOrDefault(b => b.Selected);
        if (editing != null)
        {
            var kb = Keyboard.GetState();
            if (key is Keys.Escape or Keys.Enter) Commit();
            else editing.HandleKey(key, kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift));
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

        var font = Font;
        foreach (var (id, name, x, y) in Rows())
        {
            var toggle = ToggleRect(id, x, y);
            float a = Disabled(id) ? 0.35f : 1f; // 禁用的行变淡
            if (ModEntry.OneShots.Contains(id))
            {
                drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), toggle.X, toggle.Y, toggle.Width, toggle.Height, Color.White * a, 4f, false);
                Utility.drawTextWithShadow(b, "执行", font, new Vector2(toggle.Center.X - font.MeasureString("执行").X / 2, toggle.Center.Y - font.LineSpacing / 2f + 4), Game1.textColor * a);
            }
            else
            {
                b.Draw(Game1.mouseCursors, new Vector2(toggle.X, toggle.Y), IsOn(id) ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked,
                    Color.White * a, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
            }

            Utility.drawTextWithShadow(b, name, font, new Vector2(x + 136, TextY(y)), (IsOn(id) ? Color.DarkGreen : Game1.textColor) * a);
            Utility.drawTextWithShadow(b, KeyLabel(id), font, new Vector2(KeyX, TextY(y)), Color.DimGray * a);

            if (!ValueProps.ContainsKey(id))
                continue;
            if (boxes.TryGetValue(id, out var box))
            {
                box.X = ValueX;
                box.Y = y + RowH / 2 - 24;
                box.Alpha = a;
                box.Draw(b);
                continue;
            }
            string text = id == "ItemQuality" ? QualityNames[Math.Clamp((int)Get(id), 1, 4) - 1] : $"x{(float)Get(id):0.0}";
            b.Draw(Game1.mouseCursors, new Vector2(ValueX, y + RowH / 2 - 16), OptionsPlusMinus.minusButtonSource, Color.White * a, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
            Utility.drawTextWithShadow(b, text, font, new Vector2(ValueX + 120 - font.MeasureString(text).X / 2, TextY(y)), Game1.textColor * a);
            b.Draw(Game1.mouseCursors, new Vector2(ValueX + 200, y + RowH / 2 - 16), OptionsPlusMinus.plusButtonSource, Color.White * a, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
        }

        int last = Math.Min(Entries.Length, scroll + Visible);
        string footer = $"{scroll + 1}-{last} / {Entries.Length}  ·  滚轮翻页  ·  {ModEntry.cfg.OpenMenu} 关闭  ·  {KeyLabel(HotkeysId)} 启用/禁用全部热键";
        // 窗口太窄放不下时退回小字号，避免说明文字超出面板
        var footerFont = font.MeasureString(footer).X <= width - 80 ? font : Game1.smallFont;
        Utility.drawTextWithShadow(b, footer, footerFont, new Vector2(xPositionOnScreen + 40, yPositionOnScreen + height - Footer + 4), Color.DimGray);

        base.draw(b);
        drawMouse(b);
    }

    /// <summary>"启用全部热键"关闭时，其它行都不可操作。</summary>
    private static bool Disabled(string id) => id != HotkeysId && !ModEntry.hotkeysEnabled;

    private static bool IsOn(string id) => id == HotkeysId ? ModEntry.hotkeysEnabled : ModEntry.On(id);

    private static string KeyLabel(string id) =>
        (id == HotkeysId ? ModEntry.cfg.ToggleHotkeys.ToString() : ModEntry.cfg.Hotkeys.GetValueOrDefault(id)?.ToString() ?? "")
            .Replace("LeftControl", "Ctrl").Replace("LeftAlt", "Alt").Replace("LeftShift", "Shift")
            .Replace("NumPad", "Num").Replace("Add", "Num+").Replace("Subtract", "Num-").Replace("Decimal", "Num.");

    /// <summary>运行一遍输入框编辑规则的自检，失败时返回原因。</summary>
    internal static string? SelfCheck()
    {
        static float W(string t) => t.Length * 10; // 假定每个字符宽 10
        var checks = new (bool Ok, string What)[]
        {
            (DigitEdit.Group("1234567") == "1,234,567", "千位符分组"),
            (DigitEdit.Group("007") == "007", "保留前导零"),
            (DigitEdit.Group("") == "", "空文本"),
            (DigitEdit.Group("1234.5") == "1,234.5", "小数只给整数部分分组"),
            (DigitEdit.Replace("12345", 1, 3, "9", t => DigitEdit.IsValid(t, 9, 0)) == ("1945", 2), "替换选区"),
            (DigitEdit.Replace("12345", 5, 5, "6", t => DigitEdit.IsValid(t, 5, 0)) == ("12345", 5), "超出位数时不插入"),
            (DigitEdit.Replace("12345", 0, 5, "", t => DigitEdit.IsValid(t, 9, 0)) == ("", 0), "删除全部"),
            (DigitEdit.IsValid("12.5", 3, 2) && DigitEdit.IsValid("100", 3, 2) && DigitEdit.IsValid(".5", 3, 2), "合法小数"),
            (!DigitEdit.IsValid("1.2.3", 3, 2) && !DigitEdit.IsValid("1000", 3, 2) && !DigitEdit.IsValid("1.234", 3, 2) && !DigitEdit.IsValid("1.5", 3, 0), "非法小数"),
            (string.Join(",", DigitEdit.Boundaries("1.5", W)) == "0,10,20,30", "小数点也是可定位字符"),
            (string.Join(",", DigitEdit.Boundaries("1234", W)) == "0,10,30,40,50", "光标位置跳过逗号"),
            (DigitEdit.Nearest(new float[] { 0, 10, 30, 40 }, 22) == 2, "点击位置取最近的字符边界"),
            (DigitEdit.Nearest(new float[] { 0, 10 }, -5) == 0, "点在文本左侧"),
        };
        var failed = checks.Where(c => !c.Ok).Select(c => c.What).ToArray();
        return failed.Length == 0 ? null : string.Join("、", failed);
    }

    /// <summary>数字输入框的纯编辑规则，不依赖游戏，便于自检。</summary>
    internal static class DigitEdit
    {
        /// <summary>整数部分从右往左每三位加逗号，保留全部字符（包括前导零和小数部分），保证显示字符和原文一一对应。</summary>
        public static string Group(string raw)
        {
            int dot = raw.IndexOf('.');
            string whole = dot < 0 ? raw : raw[..dot];
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < whole.Length; i++)
            {
                if (i > 0 && (whole.Length - i) % 3 == 0) sb.Append(',');
                sb.Append(whole[i]);
            }
            return dot < 0 ? sb.ToString() : sb + raw[dot..];
        }

        /// <summary>是否为最多 intDigits 位整数、最多 decimals 位小数的非负数（允许输入到一半，比如空、"1."）。</summary>
        public static bool IsValid(string text, int intDigits, int decimals)
        {
            string pattern = decimals > 0 ? $@"^\d{{0,{intDigits}}}(\.\d{{0,{decimals}}})?$" : $@"^\d{{0,{intDigits}}}$";
            return System.Text.RegularExpressions.Regex.IsMatch(text, pattern);
        }

        /// <summary>第 k 个元素是"前 k 个数字之后"的光标在显示文本中的 X 偏移，k = 0..raw.Length。</summary>
        public static float[] Boundaries(string raw, Func<string, float> measure)
        {
            string shown = Group(raw);
            var result = new float[raw.Length + 1];
            int digits = 0;
            for (int i = 0; i < shown.Length; i++)
                if (shown[i] != ',')
                    result[++digits] = measure(shown[..(i + 1)]);
            return result;
        }

        public static int Nearest(float[] bounds, float x)
        {
            int best = 0;
            for (int k = 1; k < bounds.Length; k++)
                if (Math.Abs(bounds[k] - x) < Math.Abs(bounds[best] - x)) best = k;
            return best;
        }

        /// <summary>把 [start, end) 替换成 insert；结果不合法则不改动。返回新文本和光标位置。</summary>
        public static (string Text, int Caret) Replace(string text, int start, int end, string insert, Func<string, bool> accept)
        {
            string next = text[..start] + insert + text[end..];
            return accept(next) ? (next, start + insert.Length) : (text, end);
        }
    }

    /// <summary>数字输入框（整数或小数）：在原版 TextBox 基础上补上光标定位、拖动选择、双击全选、方向键。</summary>
    private class NumberBox : TextBox
    {
        private readonly int intDigits, decimals; // 整数位数由最大值推出（999 → 3 位）；decimals 为 0 时只能输整数
        private const float FontScale = 1.3f;
        private readonly Texture2D texture;
        private int caret, anchor; // 选区是 [min, max)，两者相等时没有选区
        public float Alpha = 1f;

        private int SelStart => Math.Min(caret, anchor);
        private int SelEnd => Math.Max(caret, anchor);
        private float TextX => X + 16;

        public NumberBox(Texture2D texture, int intDigits, int decimals) : base(texture, null, Game1.smallFont, Game1.textColor)
        {
            this.texture = texture;
            this.intDigits = intDigits;
            this.decimals = decimals;
            textLimit = intDigits + (decimals > 0 ? decimals + 1 : 0);
            Width = 230;
        }

        public void ClearSelection() => caret = anchor = Text.Length;
        public void SelectAll() => (anchor, caret) = (0, Text.Length);

        public void PlaceCaret(int index, bool extend)
        {
            caret = Math.Clamp(index, 0, Text.Length);
            if (!extend) anchor = caret;
        }

        public int IndexAt(int mouseX) => DigitEdit.Nearest(Bounds(), mouseX - TextX);

        private float[] Bounds() => DigitEdit.Boundaries(Text, t => Game1.smallFont.MeasureString(t).X * FontScale);

        private void ReplaceSelection(string insert)
        {
            (Text, caret) = DigitEdit.Replace(Text, SelStart, SelEnd, insert, t => DigitEdit.IsValid(t, intDigits, decimals));
            anchor = caret;
        }

        public void HandleKey(Keys key, bool shift)
        {
            switch (key)
            {
                case Keys.Left: PlaceCaret(caret - 1, shift); break;
                case Keys.Right: PlaceCaret(caret + 1, shift); break;
                case Keys.Home: PlaceCaret(0, shift); break;
                case Keys.End: PlaceCaret(Text.Length, shift); break;
                case Keys.Delete:
                    if (SelStart == SelEnd && caret < Text.Length) anchor = caret + 1; // 没有选区时删光标后一位
                    ReplaceSelection("");
                    break;
            }
        }

        public override void RecieveTextInput(char inputChar)
        {
            ReplaceSelection(inputChar.ToString()); // 是否接受由 IsValid 决定
        }

        public override void RecieveTextInput(string text) => ReplaceSelection(text);

        public override void RecieveCommandInput(char command)
        {
            if (command != '\b')
            {
                base.RecieveCommandInput(command);
                return;
            }
            if (SelStart == SelEnd && caret > 0) anchor = caret - 1; // 没有选区时删光标前一位
            ReplaceSelection("");
        }

        // 原版 TextBox 的字号固定，这里整个自己画：底框沿用原贴图三段拉伸，数字放大并带千位符
        public override void Draw(SpriteBatch b, bool drawShadow = true)
        {
            // 文本可能被外部重置（比如输入无效时还原），先把光标收回范围内
            caret = Math.Min(caret, Text.Length);
            anchor = Math.Min(anchor, Text.Length);

            int h = texture.Height;
            b.Draw(texture, new Rectangle(X, Y, 16, h), new Rectangle(0, 0, 16, h), Color.White * Alpha);
            b.Draw(texture, new Rectangle(X + 16, Y, Width - 32, h), new Rectangle(16, 0, 4, h), Color.White * Alpha);
            b.Draw(texture, new Rectangle(X + Width - 16, Y, 16, h), new Rectangle(texture.Width - 16, 0, 16, h), Color.White * Alpha);

            var bounds = Bounds();
            float textH = Game1.smallFont.MeasureString("0").Y * FontScale;
            var pos = new Vector2(TextX, Y + (h - textH) / 2 + 2);
            if (Selected && SelStart != SelEnd)
                b.Draw(Game1.staminaRect, new Rectangle((int)(pos.X + bounds[SelStart]), (int)pos.Y, (int)(bounds[SelEnd] - bounds[SelStart]), (int)textH), Color.CornflowerBlue * 0.45f);
            Utility.drawTextWithShadow(b, DigitEdit.Group(Text), Game1.smallFont, pos, Game1.textColor * Alpha, FontScale);

            bool caretOn = (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 500) % 2 == 0;
            if (Selected && SelStart == SelEnd && caretOn)
                b.Draw(Game1.staminaRect, new Rectangle((int)(pos.X + bounds[caret]) + 1, (int)pos.Y + 2, 3, (int)textH - 4), Game1.textColor);
        }
    }
}
