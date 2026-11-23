using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace HotkeyFinder;

/// <summary>一个组合。Key 是排序后的按键，用来判断冲突；Display 保留原始顺序给人看。</summary>
internal record Hotkey(string Owner, string Name, string Display, string Key, string Source, bool Suspicious);

internal class HotkeyScanner
{
    private const string GmcmId = "spacechase0.GenericModConfigMenu";
    private const string VanillaOwner = "原版游戏";
    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Regex KeyLikeName = new("key|button|hotkey|toggle|bind|shortcut|快捷|按键", RegexOptions.IgnoreCase);

    private readonly IModHelper helper;
    private readonly IMonitor monitor;

    public HotkeyScanner(IModHelper helper, IMonitor monitor)
    {
        this.helper = helper;
        this.monitor = monitor;
    }

    /// <summary>冲突放前面，再按归属方列出全部快捷键。Warn 的行在控制台用警告级别、在菜单里标红。</summary>
    public List<(string Text, bool Warn)> Report()
    {
        var notes = new List<string>();
        var hotkeys = Collect(notes);
        var lines = new List<(string, bool)>();

        var conflicts = FindConflicts(hotkeys);
        lines.Add(($"冲突：{conflicts.Count} 处", conflicts.Count > 0));
        foreach (var group in conflicts)
            lines.Add(($"  {group[0].Display}：{string.Join("；", group.Select(h => $"{h.Owner} › {h.Name}"))}", true));

        foreach (var owner in hotkeys.GroupBy(h => h.Owner))
        {
            lines.Add(("", false));
            lines.Add(($"{owner.Key}（来源：{owner.First().Source}）", false));
            foreach (var h in owner)
                lines.Add(($"  {h.Name} = {h.Display}{(h.Suspicious ? "    ?名称不像快捷键" : "")}", false));
        }

        lines.Add(("", false));
        foreach (var note in notes.Append("键位写死在代码里的 MOD 查不到。"))
            lines.Add(($"注：{note}", false));
        return lines;
    }

    /// <summary>同一组合出现在两个以上不同归属方时算冲突，每组按出现顺序返回。</summary>
    internal static List<List<Hotkey>> FindConflicts(IEnumerable<Hotkey> hotkeys) =>
        hotkeys.GroupBy(h => h.Key)
            .Where(g => g.Select(h => h.Owner).Distinct().Count() > 1)
            .Select(g => g.ToList())
            .ToList();

    private List<Hotkey> Collect(List<string> notes)
    {
        var result = new List<Hotkey>();
        var registered = ScanGmcm(result, notes);
        ScanConfigFiles(result, registered);
        ScanVanilla(result);
        return result;
    }

    /// <summary>读 GMCM 里注册的 SButton / KeybindList 选项。返回在 GMCM 注册过的 MOD ID，这些 MOD 不再扫 config.json。</summary>
    private HashSet<string> ScanGmcm(List<Hotkey> result, List<string> notes)
    {
        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var info = helper.ModRegistry.Get(GmcmId);
        if (info == null)
            return registered;

        var found = new List<Hotkey>();
        try
        {
            // GMCM 没有公开枚举接口，只能读它的内部结构：Mod.ConfigManager.GetAll() → ModConfig.GetAllOptions()
            object gmcm = Get(info, "Mod");
            object manager = Get(gmcm, "ConfigManager");
            foreach (object config in (System.Collections.IEnumerable)Call(manager, "GetAll"))
            {
                var manifest = (IManifest)Get(config, "ModManifest");
                registered.Add(manifest.UniqueID);
                foreach (object option in (System.Collections.IEnumerable)Call(config, "GetAllOptions"))
                {
                    // 读 GetValue 委托拿实时值；Value 属性只是打开菜单时的缓存
                    var getValue = option.GetType().GetField("GetValue", AnyInstance)?.GetValue(option) as Delegate;
                    var keys = getValue?.DynamicInvoke() switch
                    {
                        SButton b => new KeybindList(b),
                        KeybindList k => k,
                        _ => null
                    };
                    if (keys != null)
                    {
                        string name = (Get(option, "Name") as Func<string>)?.Invoke() ?? "";
                        Add(found, manifest.Name, name, keys, "GMCM", suspicious: false);
                    }
                }
            }
            result.AddRange(found);
        }
        catch (Exception ex)
        {
            monitor.Log($"读取 GMCM 失败，改为只扫 config.json：{ex}", LogLevel.Trace);
            notes.Add("读取 GMCM 失败（可能是 GMCM 版本变了），所有 MOD 都改为扫 config.json。");
            registered.Clear();
        }
        return registered;
    }

    private void ScanConfigFiles(List<Hotkey> result, HashSet<string> registered)
    {
        foreach (var info in helper.ModRegistry.GetAll())
        {
            if (info.IsContentPack || registered.Contains(info.Manifest.UniqueID))
                continue;
            if (info.GetType().GetProperty("DirectoryPath")?.GetValue(info) is not string dir)
                continue;

            string path = Path.Combine(dir, "config.json");
            if (!File.Exists(path))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                foreach (var (name, text) in Strings(doc.RootElement, ""))
                {
                    if (LooksLikeKeybind(text) && KeybindList.TryParse(text, out var keys, out _))
                        Add(result, info.Manifest.Name, name, keys, "config.json", suspicious: !KeyLikeName.IsMatch(name));
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"读取 {path} 失败：{ex.Message}", LogLevel.Trace);
            }
        }
    }

    /// <summary>原版的按键设置：Game1.options 里所有 InputButton[] 字段。</summary>
    private static void ScanVanilla(List<Hotkey> result)
    {
        foreach (var field in typeof(Options).GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => f.FieldType == typeof(InputButton[])))
        {
            var buttons = ((InputButton[]?)field.GetValue(Game1.options) ?? Array.Empty<InputButton>())
                .Select(b => b.mouseLeft ? SButton.MouseLeft : b.mouseRight ? SButton.MouseRight : b.key.ToSButton())
                .Where(b => b != SButton.None)
                .Select(b => new Keybind(b))
                .ToArray();
            Add(result, VanillaOwner, field.Name, new KeybindList(buttons), "游戏选项", suspicious: false);
        }
    }

    private static void Add(List<Hotkey> result, string owner, string name, KeybindList keys, string source, bool suspicious)
    {
        foreach (var combo in keys.Keybinds.Where(k => k.IsBound))
        {
            string key = string.Join(" + ", combo.Buttons.Select(b => b.ToString()).OrderBy(b => b, StringComparer.Ordinal));
            result.Add(new Hotkey(owner, name, combo.ToString(), key, source, suspicious));
        }
    }

    /// <summary>JSON 里所有字符串值及其路径，例如 Hotkeys.OpenMenu、List[0]。</summary>
    private static IEnumerable<(string Path, string Text)> Strings(JsonElement e, string path) => e.ValueKind switch
    {
        JsonValueKind.String => new[] { (path, e.GetString()!) },
        JsonValueKind.Object => e.EnumerateObject().SelectMany(p => Strings(p.Value, path == "" ? p.Name : $"{path}.{p.Name}")),
        JsonValueKind.Array => e.EnumerateArray().SelectMany((v, i) => Strings(v, $"{path}[{i}]")),
        _ => Array.Empty<(string, string)>()
    };

    // 排除 "1"、"0.5" 这类数字：枚举解析会把数字当成按键编号
    private static bool LooksLikeKeybind(string text) => text.Trim() is { Length: > 0 } t && char.IsLetter(t[0]);

    private static object Get(object target, string name) =>
        target.GetType().GetProperty(name, AnyInstance)?.GetValue(target)
        ?? target.GetType().GetField(name, AnyInstance)?.GetValue(target)
        ?? throw new MissingMemberException(target.GetType().FullName, name);

    private static object Call(object target, string name) =>
        target.GetType().GetMethod(name, AnyInstance, Type.EmptyTypes)?.Invoke(target, null)
        ?? throw new MissingMethodException(target.GetType().FullName, name);

    /// <summary>冲突判定的自检，Debug 构建启动时跑一次。</summary>
    [Conditional("DEBUG")]
    internal static void SelfCheck()
    {
        Hotkey H(string owner, string key) => new(owner, "x", key, key, "test", false);
        var conflicts = FindConflicts(new[]
        {
            H("A", "F5"), H("B", "F5"),                                         // 不同归属方同一组合：冲突
            H("A", "F6"), H("A", "F6"),                                         // 同一归属方重复：不算
            H("A", "F7"), H("B", "F7 + LeftControl"),                           // 组合不同：不算
        });
        if (conflicts.Count != 1 || conflicts[0].Count != 2 || conflicts[0][0].Key != "F5")
            throw new InvalidOperationException("FindConflicts 自检失败");
    }
}
