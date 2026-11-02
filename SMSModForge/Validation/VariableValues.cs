using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Model;
using SMSModForge.Services;

namespace SMSModForge.Validation;

/// <summary>
/// The values a pack's own variables can be given by the pack itself: each
/// one's default, and every value an action writes into it, wherever the action
/// is (<see cref="PackWalk"/>). For the checks that ask "can this variable ever
/// be that?" - a comparison nothing can make true, an outfit bound to a variable
/// that can name no outfit (found testing a pack in the game, 2026-11-01).
/// <para/>
/// A variable is OPEN when the pack can give it values not written down: one
/// counted, incremented or rolled, one copied from another variable or built
/// from one (<c>$Name</c> anywhere in the value), one picked from a list that
/// is itself open, one set by an action whose variable name is a rule's
/// <c>{placeholder}</c>. Nothing is said about an open variable.
/// <para/>
/// Another mod may write a pack's variables too (the plugin lets it); the
/// checks say "nothing in this pack".
/// </summary>
internal sealed class VariableValues
{
    private readonly Dictionary<string, PackVariableDef> _declared = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);

    /// <summary>A variable a pick takes its value from: the list it picks from.</summary>
    private readonly List<(string Target, string List)> _picks = new();

    public static VariableValues Of(ModPack pack)
    {
        var v = new VariableValues();
        foreach (var d in pack.Variables)
        {
            if (string.IsNullOrEmpty(d.Name) || v._declared.ContainsKey(d.Name)) continue;
            v._declared[d.Name] = d;
            if (d.RefreshMode == PackVariableRefreshMode.DailyRandom || d.RefreshMode == PackVariableRefreshMode.LevelRandom)
                v._open.Add(d.Name);
            if (d.Type == PackVariableType.List) foreach (var item in ListItems(d.DefaultValue)) v.Add(d.Name, item);
            else v.Add(d.Name, d.DefaultValue ?? "");
        }
        foreach (var (a, _) in PackWalk.Actions(pack)) v.Read(a);
        // A pick takes whatever its list can hold - once the lists are known.
        foreach (var (target, list) in v._picks)
        {
            if (v.IsOpen(list) || !v._declared.ContainsKey(list)) v.Open(target);
            else foreach (var item in v.ValuesOf(list)) v.Add(target, item);
        }
        return v;
    }

    /// <summary>A pack variable of this name is declared.</summary>
    public bool Declared(string name) => _declared.ContainsKey(name);

    public bool IsOpen(string name) => _open.Contains(name);

    /// <summary>The values it can hold, as written, first seen first: for a
    /// List, the items it can hold.</summary>
    public IReadOnlyList<string> ValuesOf(string name)
        => _values.TryGetValue(name, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>Whether the pack can give <paramref name="name"/> the value
    /// <paramref name="value"/> - compared as the plugin compares, by the
    /// variable's type. Always true for a variable that is open or not the
    /// pack's.</summary>
    public bool CanBe(string name, string value)
    {
        if (!_declared.TryGetValue(name, out var d) || IsOpen(name)) return true;
        string wanted = Normal(d.Type, value);
        return ValuesOf(name).Any(v => Normal(d.Type, v) == wanted);
    }

    // ── Reading the actions ───────────────────────────────────────────────

    private void Read(NodeActionDef a)
    {
        var p = a.Params;
        if (p == null || IsGameVariable(p, "source")) return;
        switch (a.Type)
        {
            case NodeActionTypes.SetVariable:
                Write(Get(p, "name"), Get(p, "value"), IsGameVariable(p, "valueSource"));
                break;
            case NodeActionTypes.IncrementVariable:
            case NodeActionTypes.CountList:
                OpenNamed(Get(p, "name"));
                break;
            case NodeActionTypes.PickRandomFromList:
            {
                string target = Get(p, "target");
                if (target.Length == 0) target = Get(p, "name");
                if (IsBuilt(target)) { OpenNamed(target); break; }
                string fromList = Get(p, "fromList"), source = Get(p, "source");
                if (fromList.Length > 0) _picks.Add((target, fromList));
                else if (source.StartsWith("$", StringComparison.Ordinal) && !source.StartsWith("$$", StringComparison.Ordinal))
                    _picks.Add((target, source.Substring(1)));
                else if (IsBuilt(source)) Open(target);
                else foreach (var item in source.Split(',')) Add(target, item.Trim());
                // What it writes when nothing is left to pick.
                Write(target, Get(p, "fallback"), IsGameVariable(p, "valueSource"));
                break;
            }
            case NodeActionTypes.AddToList:
                Write(Get(p, "list"), Get(p, "value"), IsGameVariable(p, "valueSource"));
                break;
            case NodeActionTypes.RemoveFromList:
            case NodeActionTypes.ClearList:
                break;
            case NodeActionTypes.PlayDialogue:
                // What it sets once the dialogue has ended.
                Write(Get(p, "afterName"), Get(p, "afterValue"), false);
                break;
            default:
                // An action this does not know that names a pack variable to
                // write: whatever it writes is not written down here.
                foreach (var s in ActionSchemas.For(a.Type))
                    if (s.Type == ParamType.PackVarRef && p.TryGetValue(s.Key, out var named)) OpenNamed(named);
                break;
        }
    }

    private void Write(string name, string value, bool fromGame)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (IsBuilt(name)) { OpenNamed(name); return; }
        if (fromGame || IsBuilt(value)) { Open(name); return; }
        Add(name, Literal(value));
    }

    private void Add(string name, string value)
    {
        if (!_values.TryGetValue(name, out var list)) _values[name] = list = new List<string>();
        if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value);
    }

    private void Open(string name) => _open.Add(name);

    /// <summary>Opens the variable a name stands for - every declared one it
    /// can stand for, when it is built from a placeholder or a variable.</summary>
    private void OpenNamed(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!IsBuilt(name)) { Open(name); return; }
        var pattern = new Regex("^" + Regex.Replace(Regex.Escape(name), @"\\\{[^}]*}|\\\$[A-Za-z_][\w-]*", ".*") + "$");
        foreach (var declared in _declared.Keys)
            if (pattern.IsMatch(declared)) Open(declared);
    }

    // ── Telling written values from built ones ────────────────────────────

    /// <summary>Built when it is read: it names a variable (the plugin's own
    /// rule, <see cref="Shared.VarText"/>) or holds a rule's placeholder.</summary>
    internal static bool IsBuilt(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (Regex.IsMatch(text, @"\{[^{}]+\}")) return true;
        return Shared.VarText.Resolve(text, _ => "\u0001") != Shared.VarText.Resolve(text, _ => "\u0002");
    }

    /// <summary>A written value as the plugin reads it: <c>$$</c> is a dollar.</summary>
    internal static string Literal(string text) => Shared.VarText.Resolve(text ?? "", _ => "");

    private static bool IsGameVariable(Dictionary<string, string> p, string key)
        => p.TryGetValue(key, out var v) && string.Equals(v, "vanilla", StringComparison.OrdinalIgnoreCase);

    private static string Get(Dictionary<string, string> p, string key)
        => p.TryGetValue(key, out var v) && v != null ? v : "";

    private static IEnumerable<string> ListItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;
        Newtonsoft.Json.Linq.JArray? items = null;
        try { items = Newtonsoft.Json.Linq.JArray.Parse(json); }
        catch (Newtonsoft.Json.JsonException) { }
        if (items == null) yield break;
        foreach (var item in items) yield return item.Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)item! : item.ToString();
    }

    /// <summary>A value as the plugin compares it, by the variable's type:
    /// true/false any case, numbers by value, words exactly.</summary>
    private static string Normal(PackVariableType type, string value)
    {
        value = (value ?? "").Trim();
        switch (type)
        {
            case PackVariableType.Bool:
                return bool.TryParse(value, out var b) ? (b ? "true" : "false") : value;
            case PackVariableType.Int:
            case PackVariableType.Float:
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                    ? n.ToString("R", CultureInfo.InvariantCulture) : value;
            default:
                return value;
        }
    }
}
