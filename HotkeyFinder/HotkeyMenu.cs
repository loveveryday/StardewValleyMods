using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace HotkeyFinder;

/// <summary>只读的快捷键列表，滚轮翻页，冲突行标红。</summary>
internal class HotkeyMenu : IClickableMenu
{
    private const int Padding = 48;
    private const int Header = 96;
    private static SpriteFont Font => Game1.smallFont;
    private static int LineH => Font.LineSpacing + 4;

    private readonly List<(string Text, bool Warn)> lines = new();
    private int scroll;

    public HotkeyMenu(List<(string Text, bool Warn)> report)
        : base(0, 0, 0, 0, showUpperRightCloseButton: true)
    {
        width = Math.Min(1400, Game1.uiViewport.Width - 64);
        height = Game1.uiViewport.Height - 96;
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
        initializeUpperRightCloseButton();

        // 先按宽度折行，滚动按折行后的行数算
        int textWidth = width - Padding * 2;
        foreach (var (text, warn) in report)
            foreach (string part in Game1.parseText(text, Font, textWidth).Split('\n'))
                lines.Add((part, warn));
    }

    private int Visible => Math.Max(1, (height - Header - Padding) / LineH);

    public override void receiveScrollWheelAction(int direction)
    {
        scroll = Math.Clamp(scroll - Math.Sign(direction) * 3, 0, Math.Max(0, lines.Count - Visible));
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.4f);
        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);
        SpriteText.drawStringHorizontallyCenteredAt(b, "快捷键列表", xPositionOnScreen + width / 2, yPositionOnScreen + 32);

        int y = yPositionOnScreen + Header;
        foreach (var (text, warn) in lines.Skip(scroll).Take(Visible))
        {
            Utility.drawTextWithShadow(b, text, Font, new Vector2(xPositionOnScreen + Padding, y), warn ? Color.Red : Game1.textColor);
            y += LineH;
        }

        if (lines.Count > Visible)
        {
            string page = $"{scroll + 1}-{Math.Min(lines.Count, scroll + Visible)} / {lines.Count}  滚轮翻页";
            Utility.drawTextWithShadow(b, page, Font,
                new Vector2(xPositionOnScreen + width - Padding - Font.MeasureString(page).X, yPositionOnScreen + height - Padding + 4), Game1.textShadowColor);
        }

        base.draw(b);
        drawMouse(b);
    }
}
