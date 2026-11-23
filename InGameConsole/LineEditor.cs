using System;
using System.Collections.Generic;

namespace InGameConsole;

/// <summary>输入行的文本、光标和 emacs 编辑操作；按键和界面不在这里，方便单独自检。词边界规则照搬 SMAPI 的 EmacsKeyBindingHandler。</summary>
internal class LineEditor
{
    public string Text { get; private set; } = "";
    public int Caret { get; private set; }

    /// <summary>只有一格的 kill ring，C-y 粘回最后一次删掉的内容。</summary>
    private string killed = "";

    // 翻历史时的位置；-1 表示正在编辑新行，draft 是开始翻之前的那一行
    private int historyIndex = -1;
    private string draft = "";

    public void Set(string text, int? caret = null)
    {
        Text = text;
        Caret = Math.Clamp(caret ?? text.Length, 0, text.Length);
        historyIndex = -1;
    }

    public void Insert(string s)
    {
        Text = Text.Insert(Caret, s);
        Caret += s.Length;
    }

    public void Home() => Caret = 0;
    public void End() => Caret = Text.Length;
    public void Left() => Caret = Math.Max(0, Caret - 1);
    public void Right() => Caret = Math.Min(Text.Length, Caret + 1);
    public void WordLeft() => Caret = PrevWordStart(Text, Caret);
    public void WordRight() => Caret = NextWordEnd(Text, Caret);

    public void Backspace()
    {
        if (Caret == 0)
            return;
        Text = Text.Remove(Caret - 1, 1);
        Caret--;
    }

    public void Delete()
    {
        if (Caret < Text.Length)
            Text = Text.Remove(Caret, 1);
    }

    public void KillToEnd() => Kill(Caret, Text.Length);
    public void KillToStart() => Kill(0, Caret);

    /// <summary>C-w：删到前一个空白处（readline 的 unix-word-rubout）。</summary>
    public void KillPrevWord()
    {
        int i = Caret;
        while (i > 0 && char.IsWhiteSpace(Text[i - 1]))
            i--;
        while (i > 0 && !char.IsWhiteSpace(Text[i - 1]))
            i--;
        Kill(i, Caret);
    }

    public void KillNextWord() => Kill(Caret, NextWordEnd(Text, Caret));

    public void Yank()
    {
        if (killed.Length > 0)
            Insert(killed);
    }

    /// <summary>C-p / 上：往更早的历史翻。history 从旧到新。</summary>
    public void HistoryPrev(IReadOnlyList<string> history)
    {
        if (history.Count == 0 || historyIndex == 0)
            return;
        if (historyIndex == -1)
        {
            draft = Text;
            historyIndex = history.Count;
        }
        historyIndex--;
        Text = history[historyIndex];
        Caret = Text.Length;
    }

    /// <summary>C-n / 下：往更新的历史翻，翻过最新一条就回到开始翻之前的那一行。</summary>
    public void HistoryNext(IReadOnlyList<string> history)
    {
        if (historyIndex == -1)
            return;
        historyIndex++;
        if (historyIndex >= history.Count)
        {
            historyIndex = -1;
            Text = draft;
        }
        else
            Text = history[historyIndex];
        Caret = Text.Length;
    }

    private void Kill(int start, int end)
    {
        if (end <= start)
            return;
        killed = Text[start..end];
        Text = Text.Remove(start, end - start);
        Caret = start;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int NextWordEnd(string text, int i)
    {
        while (i < text.Length && !IsWordChar(text[i]))
            i++;
        while (i < text.Length && IsWordChar(text[i]))
            i++;
        return i;
    }

    private static int PrevWordStart(string text, int i)
    {
        while (i > 0 && !IsWordChar(text[i - 1]))
            i--;
        while (i > 0 && IsWordChar(text[i - 1]))
            i--;
        return i;
    }

    /// <summary>启动时跑一遍，规则写错就直接抛异常。</summary>
    public static void SelfCheck()
    {
        static void Assert(bool ok, string what)
        {
            if (!ok)
                throw new InvalidOperationException("LineEditor 自检失败：" + what);
        }

        var e = new LineEditor();
        e.Set("debug warp Farm");
        e.WordLeft();
        Assert(e.Caret == 11, "M-b");
        e.KillToEnd();
        Assert(e.Text == "debug warp " && e.Caret == 11, "C-k");
        e.Yank();
        Assert(e.Text == "debug warp Farm", "C-y");
        e.KillPrevWord();
        Assert(e.Text == "debug warp " && e.Caret == 11, "C-w");
        e.Home();
        e.KillNextWord();
        Assert(e.Text == " warp " && e.Caret == 0, "M-d");
        e.End();
        e.KillToStart();
        Assert(e.Text == "" && e.Caret == 0, "C-u");

        var history = new List<string> { "a", "b" };
        e.Set("draft");
        e.HistoryPrev(history);
        Assert(e.Text == "b", "C-p 1");
        e.HistoryPrev(history);
        e.HistoryPrev(history);
        Assert(e.Text == "a", "C-p 到头");
        e.HistoryNext(history);
        e.HistoryNext(history);
        Assert(e.Text == "draft", "C-n 回到草稿");
    }
}
