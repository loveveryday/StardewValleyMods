using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;

namespace InGameConsole;

/// <summary>一条日志行和它的 SMAPI 日志级别名（Trace、Debug、Info、Warn、Error、Alert、Critical、Success）。</summary>
internal record LogLine(string Text, string Level);

/// <summary>补全候选：替换文本、显示文本和说明。</summary>
internal record Completion(string Replacement, string Display, string? Description);

/// <summary>
/// 通过反射调用本地 SMAPI fork 的 internal 类型（见 docs/adr/0001）。
/// 构造时就把用到的成员全部解析出来，缺任何一个都直接抛异常，由调用方停用 MOD。
/// </summary>
internal class SmapiBridge
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private readonly object commandManager;
    private readonly MethodInfo getCompletions;
    private readonly MethodInfo getCompletionSpan;
    private readonly object commandQueue;
    private readonly MethodInfo enqueue;
    private readonly MethodInfo loadHistory;
    private readonly MethodInfo saveHistory;
    private readonly MethodInfo recordHistory;
    private readonly ConstructorInfo historySearchCtor;
    private readonly MethodInfo consoleWriteLine;

    /// <summary>SMAPI 写到 SMAPI 窗口的日志行，任何线程都可能写入，由界面在主线程取走。</summary>
    public static readonly ConcurrentQueue<LogLine> PendingLogs = new();

    /// <summary>和 SMAPI 窗口共用的命令历史文件。</summary>
    public string HistoryPath { get; } = Path.Combine(Constants.DataPath, "SMAPI-console-history.txt");

    public SmapiBridge()
    {
        Assembly smapi = typeof(IMod).Assembly;
        Type T(string name) => smapi.GetType(name, throwOnError: true)!;
        MethodInfo M(Type type, string name) => type.GetMethod(name, Any) ?? throw new MissingMethodException(type.FullName, name);
        object Get(object? target, Type type, string name) =>
            (type.GetProperty(name, Any)?.GetValue(target) ?? type.GetField(name, Any)?.GetValue(target))
            ?? throw new MissingMemberException(type.FullName, name);

        Type score = T("StardewModdingAPI.Framework.SCore");
        object core = Get(null, score, "Instance");
        commandManager = Get(core, score, "CommandManager");
        commandQueue = Get(core, score, "RawCommandQueue");
        getCompletions = M(commandManager.GetType(), "GetCompletions");
        getCompletionSpan = M(commandManager.GetType(), "GetCompletionSpan");
        enqueue = commandQueue.GetType().GetMethod("Add", new[] { typeof(string) }) ?? throw new MissingMethodException("CommandQueue", "Add");

        Type reader = T("StardewModdingAPI.Framework.Logging.InteractiveConsoleReader");
        loadHistory = M(reader, "LoadHistoryEntries");
        saveHistory = M(reader, "SaveHistoryEntries");
        recordHistory = M(reader, "RecordHistoryEntry");

        historySearchCtor = T("StardewModdingAPI.Framework.Logging.HistorySearchState").GetConstructors(Any).Single();
        consoleWriteLine = M(T("StardewModdingAPI.Internal.ConsoleWriting.ColorfulConsoleWriter"), "WriteLine");
    }

    /// <summary>拦截 SMAPI 窗口的输出，复制一份到 <see cref="PendingLogs"/>。</summary>
    public void HookConsoleOutput(Harmony harmony)
    {
        harmony.Patch(consoleWriteLine, postfix: new HarmonyMethod(typeof(SmapiBridge), nameof(OnConsoleWriteLine)));
    }

    // 参数类型 ConsoleLogLevel 是 internal 的，所以按 __args 取
    private static void OnConsoleWriteLine(object[] __args)
    {
        if (__args[0] is string message)
            PendingLogs.Enqueue(new LogLine(message, __args[1]?.ToString() ?? "Info"));
    }

    /// <summary>把一行命令交给 SMAPI 执行，和在 SMAPI 窗口里输入一样。</summary>
    public void Execute(string line) => enqueue.Invoke(commandQueue, new object[] { line });

    public List<Completion> GetCompletions(string text, int caret)
    {
        var items = (IEnumerable)getCompletions.Invoke(commandManager, new object[] { text, caret, true })!;
        return items.Cast<object>().Select(item => new Completion(
            (string)Prop(item, "ReplacementText")!,
            (string)Prop(item, "DisplayText")!,
            (string?)Prop(item, "Description")
        )).ToList();
    }

    /// <summary>选中补全后要替换的范围（光标所在的那个词）。</summary>
    public (int Start, int Length) GetCompletionSpan(string text, int caret) =>
        ((int, int))getCompletionSpan.Invoke(commandManager, new object[] { text, caret })!;

    /// <summary>从文件读命令历史，从旧到新。</summary>
    public List<string> LoadHistory() => (List<string>)loadHistory.Invoke(null, new object?[] { HistoryPath })!;

    /// <summary>记一条命令（去掉旧的重复项）并立即写回文件。</summary>
    public void AppendHistory(List<string> history, string line)
    {
        recordHistory.Invoke(null, new object[] { history, line });
        saveHistory.Invoke(null, new object?[] { HistoryPath, history });
    }

    public HistorySearch StartHistorySearch(IReadOnlyList<string> history, string query, int visibleRows) =>
        new(historySearchCtor.Invoke(new object[] { history, query, visibleRows }));

    internal static object? Prop(object target, string name) => target.GetType().GetProperty(name, Any)!.GetValue(target);
}

/// <summary>包装 SMAPI 的 Atuin 式历史搜索状态机 <c>HistorySearchState</c>。</summary>
internal class HistorySearch
{
    private readonly object state;
    private readonly MethodInfo handleKey;

    public HistorySearch(object state)
    {
        this.state = state;
        handleKey = state.GetType().GetMethod("HandleKey")!;
    }

    public string Query => (string)SmapiBridge.Prop(state, "Query")!;
    public IReadOnlyList<string> Matches => (IReadOnlyList<string>)SmapiBridge.Prop(state, "Matches")!;
    public int SelectedIndex => (int)SmapiBridge.Prop(state, "SelectedIndex")!;
    public int ScrollOffset => (int)SmapiBridge.Prop(state, "ScrollOffset")!;

    /// <summary>处理一个按键；返回 null 表示搜索继续，否则返回结束动作（Cancel、Execute、Edit）和选中的命令。</summary>
    public (string Action, string? Command)? HandleKey(ConsoleKeyInfo key)
    {
        object? result = handleKey.Invoke(state, new object[] { key });
        if (result == null)
            return null;
        return (SmapiBridge.Prop(result, "Action")!.ToString()!, (string?)SmapiBridge.Prop(result, "Command"));
    }
}
