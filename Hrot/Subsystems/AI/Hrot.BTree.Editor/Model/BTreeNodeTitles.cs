using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Hrot.BTree.Editor.Model;

/// <summary>
/// ⭐ <c>CE-1360</c> (docs/blueprints/BTree_Editor_NodeEditor_Host_Design.md §3.4) — the TITLE a C#-built tree's leaf shows: the method's
/// name in words, not its registry key. <c>Hrot.AI.Behaviors.Brains.PostureNodes.AdvanceAndAttack@24@936</c> ⇒ <c>Advance and attack</c>;
/// <c>…UtilityNodes.IsOption@5@4</c> bound at <c>IsAdvance</c> ⇒ <c>Is option: Advance</c>; a condition ends with <c>?</c>. The key
/// itself stays the node's binding (<c>MethodFqn</c>, shown by the inspector) and its id source — this is display only.
/// 🔒 User, 2026-10-26: <i>"What the user needs to see is basically the name of the C# method, but without too many extra stuff around."</i>
/// </summary>
public static class BTreeNodeTitles
{
    /// <summary>Method names that say nothing on their own: the declaring type names the node instead (<c>SquadMarchBehavior.Run</c> ⇒ "Squad march").</summary>
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal) { "Run", "Execute", "Tick", "Update", "Invoke", "Do" };

    /// <summary>Parameter fields that add nothing (the whole block, not an instance).</summary>
    private static readonly HashSet<string> GenericFields = new(StringComparer.OrdinalIgnoreCase)
        { "P", "Param", "Params", "Parameters", "Args", "Data", "State", "Block", "Order", "Mission", "Config", "Settings" };

    /// <summary>A field named for its TYPE, not an instance (<c>MoveConfig</c>, <c>EqsConfig</c>, <c>SpawnConfig</c>): adds nothing either.</summary>
    private static bool IsGenericField(string field)
        => GenericFields.Contains(field) || field.EndsWith("Config", StringComparison.Ordinal) || field.EndsWith("Params", StringComparison.Ordinal)
        || field.EndsWith("Settings", StringComparison.Ordinal);

    private static readonly string[] TypeSuffixes = { "Behavior", "Behaviour", "Nodes", "Node" };

    /// <summary>True when <paramref name="label"/> is a registry key (a dotted method name, maybe with <c>@offsets</c>), not a title.</summary>
    public static bool IsKey(string? label) => !string.IsNullOrEmpty(label) && label.IndexOf('.') > 0 && label.IndexOf(' ') < 0;

    /// <summary>True when <paramref name="label"/> is generated from <paramref name="key"/> — the key itself, or its bare method name (what
    /// <c>BTreeBuilder</c> labels a delegate leaf with) — so it may be replaced by the title; an authored label is not.</summary>
    public static bool IsGenerated(string? label, string key)
    {
        if (string.IsNullOrEmpty(label) || IsKey(label)) return true;
        string bare = key.Split('@')[0];
        return string.Equals(label, bare[(bare.LastIndexOf('.') + 1)..], StringComparison.Ordinal);
    }

    /// <summary>The title for the leaf bound to <paramref name="key"/>; <paramref name="paramField"/> is the parameter field it was bound at.</summary>
    public static string For(string key, bool condition, string? paramField = null)
    {
        string bare = key.Split('@')[0];
        int dot = bare.LastIndexOf('.');
        string method = dot >= 0 ? bare[(dot + 1)..] : bare;
        string type = dot > 0 ? bare[..dot] : string.Empty;
        type = type[(type.LastIndexOf('.') + 1)..];
        int plus = type.LastIndexOf('+');   // a nested type
        if (plus >= 0) type = type[(plus + 1)..];
        if (method.StartsWith("Condition_", StringComparison.Ordinal)) condition = true;   // a condition compiled as an action still asks
        foreach (var prefix in new[] { "Action_", "Condition_" })
            if (method.StartsWith(prefix, StringComparison.Ordinal) && method.Length > prefix.Length) method = method[prefix.Length..];
        if (Generic.Contains(method) && type.Length > 0)
        {
            foreach (var suffix in TypeSuffixes)
                if (type.EndsWith(suffix, StringComparison.Ordinal) && type.Length > suffix.Length) { type = type[..^suffix.Length]; break; }
            method = type;
        }

        var words = Words(method);
        string title = Sentence(words);
        if (paramField != null && !IsGenericField(paramField))
        {
            // the field's own words, less the ones it shares with the method's start (IsAdvance on IsOption ⇒ "Advance")
            var field = Words(paramField);
            int shared = 0;
            while (shared < field.Count && shared < words.Count && string.Equals(field[shared], words[shared], StringComparison.OrdinalIgnoreCase)) shared++;
            var rest = field.Skip(shared).ToList();
            string flatMethod = string.Concat(words).ToLowerInvariant();   // "Fallback" on FallBack says nothing new
            if (rest.Count > 0 && !rest.All(w => words.Any(m => string.Equals(m, w, StringComparison.OrdinalIgnoreCase)))
                && !flatMethod.Contains(string.Concat(rest).ToLowerInvariant(), StringComparison.Ordinal))
                title += ": " + Sentence(rest);
        }
        return condition && !title.EndsWith("?", StringComparison.Ordinal) ? title + "?" : title;
    }

    /// <summary><c>AdvanceAndAttack</c> ⇒ [Advance, And, Attack]; an acronym stays one word (<c>EQSQuery</c> ⇒ [EQS, Query]); underscores split.</summary>
    internal static List<string> Words(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        void Flush() { if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); } }
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_' || c == ' ') { Flush(); continue; }
            bool upper = char.IsUpper(c);
            bool prevLower = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
            bool acronymEnd = i > 0 && char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]);
            if (upper && (prevLower || acronymEnd)) Flush();
            current.Append(c);
        }
        Flush();
        return words;
    }

    /// <summary>The first word as it is, the rest lower-case (an acronym kept): "Advance and attack", "Spot EQS target".</summary>
    private static string Sentence(IReadOnlyList<string> words)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < words.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            string w = words[i];
            bool acronym = w.Length > 1 && w.All(char.IsUpper);
            sb.Append(i == 0 ? char.ToUpperInvariant(w[0]) + w[1..] : acronym ? w : w.ToLowerInvariant());
        }
        return sb.ToString();
    }
}
