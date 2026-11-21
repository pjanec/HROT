using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace StructEdit.Core.Attributes;

/// <summary>
/// ⭐⭐ <c>U-6</c> (docs/UX/DESIGN_Condition_Editor.md §8.4, R-4149) — THE one resolver of what a field, a property, a type or an enum member is
/// CALLED on screen. 🔒 User: <i>"they should come from some kind of C# attribute and using some default parsers or humanizers. Only if the
/// display name is not explicitly defined by C# attribute."</i>
/// <para>① <see cref="EditDisplayNameAttribute"/> on the member (a field, a property, an enum member, a class, a struct) wins; ② else
/// <see cref="Humanize"/> of its C# name — PascalCase split into words, sentence case, a type suffix (<c>PredicateDto</c>, <c>Dto</c>) dropped
/// from a TYPE's name, and a trailing unit word moved to <see cref="UnitOf(MemberInfo)"/> (<c>DeadlineSeconds</c> → <i>Deadline</i> · <c>s</c>).
/// ⭐ Resolved once per member and cached: 0 bytes per frame after the first ask (R-2084).</para>
/// ⛔ No drawer, combo or catalogue spells a name its own way — they ask here.
/// </summary>
public static class DisplayNames
{
    private readonly record struct Resolved(string Name, string? Unit, string? Icon);

    private static readonly ConcurrentDictionary<MemberInfo, Resolved> Cache = new();
    private static readonly ConcurrentDictionary<(Type, string), Resolved> EnumCache = new();

    /// <summary>The trailing name words that are a UNIT, and the unit shown for each (the longest match wins).</summary>
    private static readonly (string Word, string Unit)[] UnitWords =
    {
        ("Milliseconds", "ms"), ("Seconds", "s"), ("Minutes", "min"), ("Hours", "h"),
        ("Metres", "m"), ("Meters", "m"), ("Kilometres", "km"), ("Kilometers", "km"),
        ("Degrees", "°"), ("Deg", "°"), ("Radians", "rad"), ("Percent", "%"), ("Kmh", "km/h"), ("Mps", "m/s"),
    };

    /// <summary>The suffixes a TYPE's name loses (the most specific first).</summary>
    private static readonly string[] TypeSuffixes = { "PredicateDto", "ParamsDto", "Dto", "Params" };

    /// <summary>The name of <paramref name="member"/> (a field, a property, an enum member's field, a <see cref="Type"/>).</summary>
    public static string Of(MemberInfo member) => Resolve(member).Name;

    /// <summary>The name of an enum value (its member's attribute, else the humanized member name).</summary>
    public static string Of(Enum value)
    {
        var t = value.GetType();
        var key = (t, value.ToString());
        if (EnumCache.TryGetValue(key, out var hit)) return hit.Name;
        var field = t.GetField(value.ToString(), BindingFlags.Public | BindingFlags.Static);
        var r = field is null ? new Resolved(Humanize(value.ToString(), out _), null, null) : Resolve(field);
        EnumCache[key] = r;
        return r.Name;
    }

    /// <summary>The unit of <paramref name="member"/>: <see cref="EditUnitAttribute"/> first, else a unit word the name ended with; null for none.</summary>
    public static string? UnitOf(MemberInfo member) => Resolve(member).Unit;

    /// <summary>The icon key of <paramref name="member"/> (<see cref="EditIconAttribute"/>), a key into the ONE glyph → colour table; null for none.</summary>
    public static string? IconOf(MemberInfo member) => Resolve(member).Icon;

    private static Resolved Resolve(MemberInfo member)
    {
        if (Cache.TryGetValue(member, out var hit)) return hit;
        string raw = member.Name;
        bool isType = member is Type;
        if (isType) raw = StripGenericArity(raw);
        string? unitFromName = null;
        string name;
        var attr = member.GetCustomAttribute<EditDisplayNameAttribute>(inherit: false);
        if (attr is not null) name = attr.Name;
        else
        {
            if (isType) raw = StripSuffix(raw, TypeSuffixes);
            name = Humanize(raw, out unitFromName);
        }
        string? unit = member.GetCustomAttribute<EditUnitAttribute>(inherit: false)?.Unit ?? unitFromName;
        string? icon = member.GetCustomAttribute<EditIconAttribute>(inherit: false)?.Key;
        var r = new Resolved(name, unit, icon);
        Cache[member] = r;
        return r;
    }

    /// <summary>
    /// ⭐ THE humanizer: <c>EntryOrExit</c> → <i>Entry or exit</i> · <c>TankGun</c> → <i>Tank gun</i> · <c>DeadlineSeconds</c> → <i>Deadline</i>
    /// (unit <c>s</c>) · an acronym stays upper case (<c>DisType</c> → <i>Dis type</i>, <c>IFVCount</c> → <i>IFV count</i>) · <c>_</c> splits too.
    /// A name that is ONLY a unit word keeps it (<c>Seconds</c> → <i>Seconds</i>).
    /// </summary>
    public static string Humanize(string pascal) => Humanize(pascal, out _);

    /// <inheritdoc cref="Humanize(string)"/>
    public static string Humanize(string pascal, out string? unit)
    {
        unit = null;
        if (string.IsNullOrEmpty(pascal)) return string.Empty;
        string body = pascal;
        foreach (var (word, u) in UnitWords)
            if (body.Length > word.Length && body.EndsWith(word, StringComparison.Ordinal)
                && (char.IsLower(body[body.Length - word.Length - 1]) || char.IsDigit(body[body.Length - word.Length - 1])
                    || char.IsUpper(body[body.Length - word.Length - 1])))
            {
                body = body.Substring(0, body.Length - word.Length).TrimEnd('_');
                unit = u;
                break;
            }

        var sb = new StringBuilder(body.Length + 4);
        int wordIndex = 0;
        int i = 0;
        while (i < body.Length)
        {
            if (body[i] == '_' || body[i] == ' ') { i++; continue; }
            int start = i;
            if (char.IsUpper(body[i]))
            {
                // an acronym run (two or more capitals, ending before a Capital+lower pair) or one Capitalised word
                int j = i + 1;
                while (j < body.Length && char.IsUpper(body[j])) j++;
                if (j - i >= 2)
                {
                    if (j < body.Length && char.IsLower(body[j])) j--;   // "IFVCount": the C starts the next word
                    if (j - i >= 2) { AppendWord(sb, body.Substring(start, j - start), acronym: true, ref wordIndex); i = j; continue; }
                }
                i++;
                while (i < body.Length && (char.IsLower(body[i]) || char.IsDigit(body[i]))) i++;
            }
            else
            {
                while (i < body.Length && body[i] != '_' && body[i] != ' ' && !char.IsUpper(body[i])) i++;
            }
            AppendWord(sb, body.Substring(start, i - start), acronym: false, ref wordIndex);
        }
        return sb.ToString();
    }

    private static void AppendWord(StringBuilder sb, string word, bool acronym, ref int wordIndex)
    {
        if (word.Length == 0) return;
        if (wordIndex > 0) sb.Append(' ');
        if (acronym) sb.Append(word);
        else if (wordIndex == 0) sb.Append(char.ToUpperInvariant(word[0])).Append(word, 1, word.Length - 1);
        else sb.Append(char.ToLowerInvariant(word[0])).Append(word, 1, word.Length - 1);
        wordIndex++;
    }

    private static string StripSuffix(string name, string[] suffixes)
    {
        foreach (var s in suffixes)
            if (name.Length > s.Length && name.EndsWith(s, StringComparison.Ordinal)) return name.Substring(0, name.Length - s.Length);
        return name;
    }

    private static string StripGenericArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick > 0 ? name.Substring(0, tick) : name;
    }
}

/// <summary>
/// ⭐ <c>U-6</c> (Condition editor §8.4) — the glyph of a member or a type: a KEY into Colour Icons' ONE key → (glyph, colour) table.
/// ⛔ Never a colour here — the palette stays one table.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
public sealed class EditIconAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}
