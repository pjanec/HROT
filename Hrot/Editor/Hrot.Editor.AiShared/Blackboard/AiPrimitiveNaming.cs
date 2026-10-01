using System;
using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// ⭐⭐⭐ <b>The ONE place that knows the generated-AiPrimitive naming convention, and the one way to
/// get from a picked BLUEPRINT NAME to its generated <c>Params</c> type.</b>
/// 📄 <c>DESIGN_Parameter_Model.md</c> §5.3 · <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.6c.
///
/// <para>🔒 <b>Why this is shared rather than copied</b> (ruling 9). The BTree editor has composed a
/// blueprint onto a node since <c>E2</c> — <c>BTreeCommandSink.ComposeAiPrimitiveAction</c> — and the
/// HSM editor now does the same for a state's activity and a transition's guard (<c>CE-414</c>). ⛔ Two
/// spellings of <i>"{Blueprint}_{Id:X8}_Bp.TickCore"</i> is how one of them quietly stops matching after
/// a rename of the emitter's class-name format.</para>
///
/// <para>⚠ <b>The format is OWNED BY <c>AiPrimitiveEmitter.EmitClass</c></b>
/// (<c>"{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp"</c>). This type only PARSES it; the generator's
/// own <c>GeneratedBlueprintSchemaCatalog.TryParseGeneratedClassRef</c> is the compile-time twin, and it
/// lives in a netstandard2.0 assembly this one cannot reference.</para>
/// </summary>
public static class AiPrimitiveNaming
{
    /// <summary>
    /// ⭐ The blueprint's authored NAME, recovered from its generated <c>TickCore</c> FQN.
    ///
    /// <para>Strips the member (<c>.TickCore</c>), the namespace, the <c>_Bp</c> suffix and the
    /// 8-hex-digit blueprint id. ⛔ Falls back to the declaring type's short name when the string does
    /// not match the convention, so a caller always gets something printable rather than an empty
    /// label.</para>
    /// </summary>
    public static string DisplayNameFromTickCoreFqn(string tickCoreFqn)
    {
        if (string.IsNullOrEmpty(tickCoreFqn)) return string.Empty;

        int lastDot = tickCoreFqn.LastIndexOf('.');
        string declFqn = lastDot > 0 ? tickCoreFqn.Substring(0, lastDot) : tickCoreFqn;
        int declDot = declFqn.LastIndexOf('.');
        string declShort = declDot >= 0 ? declFqn.Substring(declDot + 1) : declFqn;

        string name = declShort;
        if (name.EndsWith("_Bp", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - 3);

        int us = name.LastIndexOf('_');
        if (us > 0 && name.Length - us - 1 == 8 && IsHex(name.AsSpan(us + 1)))
            name = name.Substring(0, us);

        return string.IsNullOrEmpty(name) ? declShort : name;

        static bool IsHex(ReadOnlySpan<char> s)
        {
            foreach (var c in s)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
    }

    /// <summary>The technology label of a blueprint-authored action / condition (<c>CE-462</c>).</summary>
    public const string BlueprintTechnology = "Blueprint";

    /// <summary>The technology label of a hand-written action / condition (<c>CE-462</c>).</summary>
    public const string CSharpTechnology = "C#";

    /// <summary>
    /// ⭐⭐ <b><c>CE-462</c> (E4 ④) — how a picker SHOWS an action / condition / guard it stores by FQN:
    /// the name plus its technology.</b> 🔒 User, <c>2026-09-30</c>: <i>"when picking conditions i need to see
    /// all available ones no matter what technology they are based on"</i> ⇒ the technology is a LABEL,
    /// never a filter. ⭐ Read off <see cref="ActionSchemaEntry.IsAiPrimitive"/> — the exporter already
    /// knows, so ⛔ no second lookup. A blueprint shows its authored name (its FQN is the generated
    /// <c>{Name}_{id:X8}_Bp.TickCore</c>); a hand-written one keeps its FQN, which is its identity; a name
    /// the exporter does not know (a dangling binding) is shown bare, unlabelled.
    /// </summary>
    public static string PickerLabel(string fqn, IActionSchemaExporter? exporter)
    {
        if (string.IsNullOrEmpty(fqn)) return fqn ?? string.Empty;
        var entry = exporter?.Lookup(fqn);
        if (entry is null) return fqn;
        return entry.IsAiPrimitive
            ? $"{DisplayNameFromTickCoreFqn(fqn)}  [{BlueprintTechnology}]"
            : $"{fqn}  [{CSharpTechnology}]";
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The picked blueprint NAME → its schema entry, whose <c>DtoType</c> IS the generated
    /// <c>Params</c> struct.</b>
    ///
    /// <para>🔒 <b>This is the whole point of the compose step.</b> A hosting site binds ONE blackboard
    /// variable whose TYPE is that struct, so the seed's byte offset is that one variable's offset and
    /// the field offsets inside it come from the struct. ⛔ The alternative — several scalar variables
    /// whose packed layout has to coincide with the struct's — makes declaration ORDER load-bearing with
    /// nothing checking it, which is the defect <c>CE-414</c> was filed for.</para>
    ///
    /// <para>⚠ <b>Only <c>IsAiPrimitive</c> entries are considered.</b> A hand-written
    /// <c>[HsmAction]</c>/<c>[BTreeAction]</c> of the same name is a different thing with a different
    /// DTO, and matching one here would bind a variable of the wrong type.</para>
    ///
    /// <para>⛔ Returns <c>false</c> rather than guessing when the exporter is absent (a headless
    /// fixture), the name is blank, or nothing matches — the caller then leaves the site unbound, which
    /// is the pre-<c>CE-414</c> behaviour and not a corruption.</para>
    /// </summary>
    public static bool TryFindAiPrimitiveByName(
        IActionSchemaExporter? exporter, string? blueprintName, out ActionSchemaEntry entry)
    {
        entry = null!;
        if (exporter is null || string.IsNullOrWhiteSpace(blueprintName)) return false;

        foreach (KeyValuePair<string, ActionSchemaEntry> kv in exporter.All)
        {
            if (!kv.Value.IsAiPrimitive) continue;
            if (!string.Equals(DisplayNameFromTickCoreFqn(kv.Key), blueprintName, StringComparison.Ordinal))
                continue;

            entry = kv.Value;
            return true;
        }

        return false;
    }
}
