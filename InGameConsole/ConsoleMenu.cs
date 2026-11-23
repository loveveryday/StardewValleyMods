using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace InGameConsole;

/// <summary>本次游戏期间 SMAPI 窗口输出过的日志，控制台关着时也照样记录。</summary>
internal static class Scrollback
{
    private const int Max = 2000;
    public static readonly List<LogLine> Lines = new();

    /// <summary>累计写入过多少行（不因裁剪或清屏减少），界面据此判断有几行新的。</summary>
    public static long Total { get; private set; }

    public static void Add(LogLine line)
    {
        Lines.Add(line);
        Total++;
        if (Lines.Count > Max)
            Lines.RemoveRange(0, Lines.Count - Max);
    }
}

/// <summary>
/// 游戏内控制台。打开时临时替换当前菜单，关闭时还原，所以标题界面和其他菜单上面也能打开；
/// 作为菜单存在，单人模式下打开时游戏暂停。
/// </summary>
internal class ConsoleMenu : IClickableMenu, IKeyboardSubscriber
{
    private const int Pad = 12;
    private const int TitleH = 40;
    private const int Grip = 20;
    private const int MinW = 480, MinH = 240;
    private const int PopupRows = 8;
    private const double RepeatDelay = 0.4, RepeatInterval = 0.035;
    private const string Prompt = "> ";
    private const float MinScale = 0.5f, MaxScale = 2.5f;

    private readonly IClickableMenu? previous;
    private readonly SmapiBridge bridge;
    private readonly ModConfig config;
    private readonly Action saveConfig;

    private readonly LineEditor editor = new();
    private List<string> history;

    private readonly List<(string Text, Color Color)> rows = new();
    private long seenTotal;
    private int wrapWidth;
    private int scroll; // 从底部往上滚了多少行

    private List<Completion>? completions;
    private int completionIndex;

    private HistorySearch? search;
    private string inputBeforeSearch = "";

    private KeyboardState oldKeys;
    private Keys? repeatKey;
    private double repeatAt;

    private enum Drag { None, Move, Resize }
    private Drag drag;
    private Point dragOffset;

    private SpriteFont? charsFont;
    private HashSet<char> fontChars = new();

    public bool Selected { get; set; }

    private static SpriteFont Font => Game1.smallFont;
    private float Scale => config.FontScale;
    private int LineH => (int)(Font.LineSpacing * Scale);

    public ConsoleMenu(IClickableMenu? previous, SmapiBridge bridge, ModConfig config, Action saveConfig)
    {
        this.previous = previous;
        this.bridge = bridge;
        this.config = config;
        this.saveConfig = saveConfig;
        history = bridge.LoadHistory();

        var vp = Game1.uiViewport;
        width = config.Width ?? vp.Width * 6 / 10;
        height = config.Height ?? vp.Height / 2;
        xPositionOnScreen = config.X ?? (vp.Width - width) / 2;
        yPositionOnScreen = config.Y ?? (vp.Height - height) / 2;
        ClampToScreen();

        oldKeys = Keyboard.GetState(); // 打开控制台的那次按键不算输入
        Game1.keyboardDispatcher.Subscriber = this;
    }

    public void Close()
    {
        if (Game1.keyboardDispatcher.Subscriber == this)
            Game1.keyboardDispatcher.Subscriber = null;
        Game1.activeClickableMenu = previous;
    }

    // ---- 布局 ----

    private Rectangle Window => new(xPositionOnScreen, yPositionOnScreen, width, height);
    private Rectangle TitleBar => new(xPositionOnScreen, yPositionOnScreen, width, TitleH);
    private Rectangle CloseRect => new(xPositionOnScreen + width - TitleH, yPositionOnScreen, TitleH, TitleH);
    private Rectangle GripRect => new(xPositionOnScreen + width - Grip, yPositionOnScreen + height - Grip, Grip, Grip);
    private int InputY => yPositionOnScreen + height - Pad - LineH;
    private int LogTop => yPositionOnScreen + TitleH + Pad / 2;
    private int VisibleRows => Math.Max(1, (InputY - Pad / 2 - LogTop) / LineH);

    private void ClampToScreen()
    {
        var vp = Game1.uiViewport;
        width = Math.Clamp(width, MinW, Math.Max(MinW, vp.Width));
        height = Math.Clamp(height, MinH, Math.Max(MinH, vp.Height));
        xPositionOnScreen = Math.Clamp(xPositionOnScreen, 0, Math.Max(0, vp.Width - width));
        yPositionOnScreen = Math.Clamp(yPositionOnScreen, 0, Math.Max(0, vp.Height - height));
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => ClampToScreen();

    // ---- 鼠标：拖标题栏移动，拖右下角调大小，滚轮翻日志 ----

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (CloseRect.Contains(x, y))
            Close();
        else if (GripRect.Contains(x, y))
        {
            drag = Drag.Resize;
            dragOffset = new Point(xPositionOnScreen + width - x, yPositionOnScreen + height - y);
        }
        else if (TitleBar.Contains(x, y))
        {
            drag = Drag.Move;
            dragOffset = new Point(x - xPositionOnScreen, y - yPositionOnScreen);
        }
    }

    public override void leftClickHeld(int x, int y)
    {
        if (drag == Drag.Move)
        {
            xPositionOnScreen = x - dragOffset.X;
            yPositionOnScreen = y - dragOffset.Y;
        }
        else if (drag == Drag.Resize)
        {
            width = x + dragOffset.X - xPositionOnScreen;
            height = y + dragOffset.Y - yPositionOnScreen;
        }
        if (drag != Drag.None)
            ClampToScreen();
    }

    public override void releaseLeftClick(int x, int y)
    {
        if (drag == Drag.None)
            return;
        drag = Drag.None;
        config.X = xPositionOnScreen;
        config.Y = yPositionOnScreen;
        config.Width = width;
        config.Height = height;
        saveConfig();
    }

    public override void receiveScrollWheelAction(int direction) => ScrollBy(Math.Sign(direction) * 3);

    private void ScrollBy(int delta) => scroll = Math.Clamp(scroll + delta, 0, Math.Max(0, rows.Count - VisibleRows));

    // ---- 键盘 ----

    // 菜单默认按 Esc / E 关闭，这里全部自己处理
    public override void receiveKeyPress(Keys key) { }
    public override void receiveGamePadButton(Buttons b) { }

    public void RecieveTextInput(char c)
    {
        if (char.IsControl(c) || CtrlOrAlt(Keyboard.GetState()))
            return;
        if (search != null)
            SearchKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        else
            Edited(() => editor.Insert(c.ToString()));
    }

    // Ctrl+V 粘贴走这里
    public void RecieveTextInput(string text)
    {
        text = text.Replace("\r", "").Replace('\n', ' ');
        if (search != null)
            foreach (char c in text)
                SearchKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        else
            Edited(() => editor.Insert(text));
    }

    // 退格、回车、Tab 在 update 里按键盘状态处理，这里忽略，免得处理两次
    public void RecieveCommandInput(char command) { }
    public void RecieveSpecialInput(Keys key) { }

    public override void update(GameTime time)
    {
        if (Game1.keyboardDispatcher.Subscriber != this)
            Game1.keyboardDispatcher.Subscriber = this;

        PullLogs();

        // 按住按键时自动重复；游戏只给菜单发按下的那一下，所以自己轮询键盘状态
        // 直接读原始键盘：控制台打开时 ModEntry 会屏蔽键盘按键，SMAPI 给游戏的输入状态里就看不到它们了
        KeyboardState keys = Keyboard.GetState();
        double now = time.TotalGameTime.TotalSeconds;
        foreach (Keys key in keys.GetPressedKeys())
        {
            if (oldKeys.IsKeyDown(key) || IsModifier(key))
                continue;
            repeatKey = key;
            repeatAt = now + RepeatDelay;
            HandleKey(key, keys);
        }
        if (repeatKey is Keys held)
        {
            if (!keys.IsKeyDown(held))
                repeatKey = null;
            else if (now >= repeatAt)
            {
                repeatAt = now + RepeatInterval;
                HandleKey(held, keys);
            }
        }
        oldKeys = keys;
    }

    private static bool IsModifier(Keys k) => k is Keys.LeftControl or Keys.RightControl or Keys.LeftAlt or Keys.RightAlt or Keys.LeftShift or Keys.RightShift;
    private static bool Ctrl(KeyboardState s) => s.IsKeyDown(Keys.LeftControl) || s.IsKeyDown(Keys.RightControl);
    private static bool Alt(KeyboardState s) => s.IsKeyDown(Keys.LeftAlt) || s.IsKeyDown(Keys.RightAlt);
    private static bool CtrlOrAlt(KeyboardState s) => Ctrl(s) || Alt(s);

    private void HandleKey(Keys key, KeyboardState state)
    {
        bool ctrl = Ctrl(state), alt = Alt(state);
        if (Game1.activeClickableMenu != this)
            return;
        if (ctrl && key is Keys.OemPlus or Keys.OemMinus)
        {
            ChangeFontScale(key == Keys.OemPlus ? 1 : -1);
            return;
        }

        if (search != null)
        {
            ConsoleKey? ck = key switch
            {
                Keys.Escape => ConsoleKey.Escape,
                Keys.Enter => ConsoleKey.Enter,
                Keys.Tab => ConsoleKey.Tab,
                Keys.Right => ConsoleKey.RightArrow,
                Keys.Up => ConsoleKey.UpArrow,
                Keys.Down => ConsoleKey.DownArrow,
                Keys.Back => ConsoleKey.Backspace,
                _ when ctrl && key is >= Keys.A and <= Keys.Z => (ConsoleKey)(int)key, // Keys.A..Z 和 ConsoleKey.A..Z 的数值相同
                _ => null
            };
            if (ck != null)
                SearchKey(new ConsoleKeyInfo('\0', ck.Value, false, false, ctrl));
            return;
        }

        if (completions != null && HandleCompletionKey(key, ctrl))
            return;
        EditKey(key, ctrl, alt);
        if (completions != null)
            RefreshCompletions();
    }

    private void EditKey(Keys key, bool ctrl, bool alt)
    {
        if (ctrl)
        {
            switch (key)
            {
                case Keys.A: editor.Home(); break;
                case Keys.E: editor.End(); break;
                case Keys.B: editor.Left(); break;
                case Keys.F: editor.Right(); break;
                case Keys.Left: editor.WordLeft(); break;
                case Keys.Right: editor.WordRight(); break;
                case Keys.P: editor.HistoryPrev(history); break;
                case Keys.N: editor.HistoryNext(history); break;
                case Keys.D: Edited(editor.Delete); break;
                case Keys.H: Edited(editor.Backspace); break;
                case Keys.K: Edited(editor.KillToEnd); break;
                case Keys.U: Edited(editor.KillToStart); break;
                case Keys.W: Edited(editor.KillPrevWord); break;
                case Keys.Y: Edited(editor.Yank); break;
                case Keys.R: StartSearch(); break;
                case Keys.L: Scrollback.Lines.Clear(); rows.Clear(); scroll = 0; break;
            }
            return;
        }
        if (alt)
        {
            switch (key)
            {
                case Keys.B: editor.WordLeft(); break;
                case Keys.F: editor.WordRight(); break;
                case Keys.D: Edited(editor.KillNextWord); break;
            }
            return;
        }

        switch (key)
        {
            case Keys.Escape: Close(); break;
            case Keys.Enter: Submit(editor.Text); break;
            case Keys.Tab: OpenCompletions(); break;
            case Keys.Left: editor.Left(); break;
            case Keys.Right: editor.Right(); break;
            case Keys.Home: editor.Home(); break;
            case Keys.End: editor.End(); break;
            case Keys.Up: editor.HistoryPrev(history); break;
            case Keys.Down: editor.HistoryNext(history); break;
            case Keys.Back: Edited(editor.Backspace); break;
            case Keys.Delete: Edited(editor.Delete); break;
            case Keys.PageUp: ScrollBy(VisibleRows - 1); break;
            case Keys.PageDown: ScrollBy(-(VisibleRows - 1)); break;
        }
    }

    /// <summary>改动了输入内容；补全列表开着时按新内容重新查，查不到就关掉。</summary>
    private void Edited(Action edit)
    {
        edit();
        if (completions != null)
            RefreshCompletions();
    }

    private void Submit(string line)
    {
        editor.Set("");
        scroll = 0;
        if (string.IsNullOrWhiteSpace(line))
            return;
        Scrollback.Add(new LogLine(Prompt + line, "Input"));
        history = bridge.LoadHistory(); // SMAPI 窗口里可能也执行过命令，先重新读再追加
        bridge.AppendHistory(history, line);
        bridge.Execute(line);
    }

    // ---- 补全：和 SMAPI 窗口一样，Tab 打开列表，再按 Tab 或 Enter 选中 ----

    private void OpenCompletions()
    {
        RefreshCompletions();
        if (completions?.Count == 1)
            AcceptCompletion();
    }

    private void RefreshCompletions()
    {
        completions = bridge.GetCompletions(editor.Text, editor.Caret);
        completionIndex = 0;
        if (completions.Count == 0)
            completions = null;
    }

    private bool HandleCompletionKey(Keys key, bool ctrl)
    {
        switch (key)
        {
            case Keys.Tab or Keys.Enter:
                AcceptCompletion();
                return true;
            case Keys.Up or Keys.P when key == Keys.Up || ctrl:
                completionIndex = (completionIndex - 1 + completions!.Count) % completions.Count;
                return true;
            case Keys.Down or Keys.N when key == Keys.Down || ctrl:
                completionIndex = (completionIndex + 1) % completions!.Count;
                return true;
            case Keys.Escape:
            case Keys.G when ctrl:
                completions = null;
                return true;
            default:
                return false; // 其他按键照常处理，处理完 HandleKey 会按新的光标位置刷新列表
        }
    }

    private void AcceptCompletion()
    {
        string replacement = completions![completionIndex].Replacement;
        var (start, length) = bridge.GetCompletionSpan(editor.Text, editor.Caret);
        editor.Set(editor.Text.Remove(start, length).Insert(start, replacement), start + replacement.Length);
        completions = null;
    }

    // ---- Ctrl+R：SMAPI 的 Atuin 式历史搜索 ----

    private void StartSearch()
    {
        completions = null;
        history = bridge.LoadHistory();
        inputBeforeSearch = editor.Text;
        search = bridge.StartHistorySearch(history, editor.Text, PopupRows);
    }

    private void SearchKey(ConsoleKeyInfo key)
    {
        if (search!.HandleKey(key) is not var (action, command))
            return;
        search = null;
        switch (action)
        {
            case "Execute": Submit(command!); break;
            case "Edit": editor.Set(command!); break;
            default: editor.Set(inputBeforeSearch); break;
        }
    }

    // ---- 日志 ----

    private void PullLogs()
    {
        int width = this.width - Pad * 2;
        if (width != wrapWidth && drag != Drag.Resize) // 拖动大小时不重排，松手再排，免得每帧重排 2000 行
        {
            wrapWidth = width;
            rows.Clear();
            seenTotal = Scrollback.Total - Scrollback.Lines.Count;
        }
        long fresh = Scrollback.Total - seenTotal;
        if (fresh <= 0)
            return;
        int before = rows.Count;
        foreach (LogLine line in Scrollback.Lines.Skip(Math.Max(0, Scrollback.Lines.Count - (int)Math.Min(fresh, Scrollback.Lines.Count))))
        {
            Color color = ColorFor(line.Level);
            foreach (string text in line.Text.Replace("\r", "").Split('\n'))
                foreach (string row in Wrap(Safe(text.Replace("\t", "    ")), wrapWidth))
                    rows.Add((row, color));
        }
        seenTotal = Scrollback.Total;
        if (scroll > 0)
            scroll += rows.Count - before; // 正在往回翻时，新日志不把画面顶走
        if (rows.Count > 5000)
            rows.RemoveRange(0, rows.Count - 5000);
        scroll = Math.Clamp(scroll, 0, Math.Max(0, rows.Count - VisibleRows));
    }

    private readonly Dictionary<char, float> charWidth = new();

    /// <summary>
    /// 按宽度折行，尽量在空格处断开；中文这种没有空格的长串按字断开。
    /// 不用 Game1.parseText，因为游戏语言是中文时它会把英文里的空格吞掉。
    /// </summary>
    private IEnumerable<string> Wrap(string s, int maxWidth)
    {
        int start = 0;
        while (start < s.Length)
        {
            float w = 0;
            int end = start, lastSpace = -1;
            for (; end < s.Length; end++)
            {
                if (!charWidth.TryGetValue(s[end], out float cw))
                    charWidth[s[end]] = cw = TextWidth(s[end].ToString());
                if (w + cw > maxWidth && end > start)
                    break;
                w += cw;
                if (s[end] == ' ')
                    lastSpace = end;
            }
            if (end < s.Length && lastSpace > start)
                end = lastSpace + 1;
            yield return s[start..end].TrimEnd();
            start = end;
        }
        if (s.Length == 0)
            yield return "";
    }

    private static Color ColorFor(string level) => level switch
    {
        "Trace" or "Debug" => Color.Gray,
        "Warn" => Color.Orange,
        "Error" or "Critical" => new Color(255, 90, 90),
        "Alert" => Color.Magenta,
        "Success" => Color.LightGreen,
        "Input" => Color.Cyan,
        _ => Color.White
    };

    /// <summary>当前字体没有的字（比如英文模式下的中文、制表符号）换成问号，否则 SpriteFont 会抛异常。</summary>
    private string Safe(string s)
    {
        if (charsFont != Font)
        {
            charsFont = Font;
            fontChars = new HashSet<char>(Font.Characters);
            charWidth.Clear();
        }
        if (Font.DefaultCharacter != null || s.All(fontChars.Contains))
            return s;
        return new string(s.Select(c => fontChars.Contains(c) ? c : '?').ToArray());
    }

    // 中文字体的空格几乎没有宽度，英文日志会挤成一团，所以空格按固定宽度单独留出
    private static float SpaceWidth => Math.Max(Font.MeasureString(" ").X, Font.MeasureString("n").X * 0.6f);

    private float TextWidth(string s, float? scale = null) =>
        (s.Split(' ').Sum(part => Font.MeasureString(part).X) + s.Count(c => c == ' ') * SpaceWidth) * (scale ?? Scale);

    private void DrawText(SpriteBatch b, string s, Vector2 pos, Color color, float? scale = null)
    {
        float k = scale ?? Scale;
        foreach (string part in s.Split(' '))
        {
            if (part.Length > 0)
                b.DrawString(Font, part, pos, color, 0f, Vector2.Zero, k, SpriteEffects.None, 1f);
            pos.X += (Font.MeasureString(part).X + SpaceWidth) * k;
        }
    }

    /// <summary>Ctrl + = / Ctrl + -：调整控制台字号并保存。</summary>
    private void ChangeFontScale(int direction)
    {
        config.FontScale = MathF.Round(Math.Clamp(config.FontScale + direction * 0.1f, MinScale, MaxScale), 1);
        saveConfig();
        charWidth.Clear();
        wrapWidth = -1; // 下一帧按新字号重新折行
    }

    // ---- 绘制 ----

    public override void draw(SpriteBatch b)
    {
        previous?.draw(b);

        Rectangle win = Window;
        b.Draw(Game1.staminaRect, win, Color.Black * 0.8f);
        b.Draw(Game1.staminaRect, TitleBar, new Color(60, 60, 80) * 0.9f);
        DrawText(b, Safe("游戏内控制台"), new Vector2(win.X + Pad, win.Y + (TitleH - Font.LineSpacing) / 2), Color.White, 1f);
        Rectangle close = CloseRect;
        if (close.Contains(Game1.getMouseX(), Game1.getMouseY()))
            b.Draw(Game1.staminaRect, close, new Color(200, 60, 60));
        DrawText(b, "X", new Vector2(close.Center.X - TextWidth("X", 1f) / 2, win.Y + (TitleH - Font.LineSpacing) / 2), Color.White, 1f);
        b.Draw(Game1.staminaRect, GripRect, Color.Gray * 0.8f);

        // 日志，从下往上排
        int visible = VisibleRows;
        int first = Math.Max(0, rows.Count - scroll - visible);
        int count = Math.Min(visible, rows.Count - scroll - first);
        int y = InputY - Pad / 2 - count * LineH;
        for (int i = first; i < first + count; i++, y += LineH)
            DrawText(b, rows[i].Text, new Vector2(win.X + Pad, y), rows[i].Color);

        // 输入行和光标
        string text = search != null ? $"(历史搜索) {search.Query}" : Prompt + editor.Text;
        string beforeCaret = search != null ? text : Prompt + editor.Text[..editor.Caret];
        var inputPos = new Vector2(win.X + Pad, InputY);
        DrawText(b, Safe(text), inputPos, Color.White);
        if (Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 1000 < 600)
            b.Draw(Game1.staminaRect, new Rectangle((int)(inputPos.X + TextWidth(Safe(beforeCaret))), InputY, 2, LineH), Color.White);

        if (search != null)
            DrawPopup(b, search.Matches.Skip(search.ScrollOffset).Take(PopupRows).Select(m => (m, (string?)null)).ToList(),
                search.SelectedIndex - search.ScrollOffset, search.Matches.Count == 0 ? "没有匹配的历史" : null);
        else if (completions != null)
        {
            int top = Math.Max(0, completionIndex - PopupRows + 1);
            DrawPopup(b, completions.Skip(top).Take(PopupRows).Select(c => (c.Display, c.Description)).ToList(), completionIndex - top, null);
        }

        drawMouse(b);
    }

    /// <summary>在输入行正上方画候选列表。</summary>
    private void DrawPopup(SpriteBatch b, List<(string Text, string? Note)> items, int selected, string? empty)
    {
        int n = Math.Max(1, items.Count);
        var box = new Rectangle(xPositionOnScreen + Pad, InputY - Pad / 2 - n * LineH - 8, width - Pad * 2, n * LineH + 8);
        b.Draw(Game1.staminaRect, box, new Color(30, 30, 45));
        if (items.Count == 0 && empty != null)
            DrawText(b, Safe(empty), new Vector2(box.X + 8, box.Y + 4), Color.Gray);
        for (int i = 0; i < items.Count; i++)
        {
            var pos = new Vector2(box.X + 8, box.Y + 4 + i * LineH);
            if (i == selected)
                b.Draw(Game1.staminaRect, new Rectangle(box.X, (int)pos.Y, box.Width, LineH), new Color(70, 90, 140));
            string main = Safe(items[i].Text);
            DrawText(b, main, pos, Color.White);
            if (!string.IsNullOrEmpty(items[i].Note))
            {
                float x = pos.X + TextWidth(main) + 24;
                string note = Safe(items[i].Note!);
                // 说明太长就截断，只占一行
                while (note.Length > 0 && x + TextWidth(note) > box.Right - 8)
                    note = note[..^1];
                DrawText(b, note, new Vector2(x, pos.Y), Color.Gray);
            }
        }
    }
}
