using System.Text.Json.Nodes;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core;

/// <summary>
/// <b>U-10 (D2) — the schema <c>1 ⇄ 2</c> transform pair.</b>
///
/// <para>
/// v1 stores declarations as <b>three lists</b>; v2 stores them as <b>one tagged array</b>, the same
/// shape <c>BlueprintAsset.Declarations</c> presents in memory since <c>U-9</c>.
/// </para>
///
/// <para>
/// ⭐⭐ <b>The down transform is the revert, so it ships with the up transform and is tested with
/// it.</b> <c>git revert</c> cannot undo a migration — a migrated file stays migrated — which makes
/// <see cref="Down"/> the only thing that can put the corpus back.
/// </para>
///
/// <para>
/// ⚠⚠ <b>Why this is a DOM transform and not a serializer feature.</b> The one production reader that
/// must not break is <c>BlueprintIncrementalGenerator</c>, and it targets <b><c>netstandard2.0</c></b>
/// — where <c>Hrot.Blueprints.Compiler</c>'s <c>Fdp.Core</c> / <c>Hrot.Common</c> project references
/// do not apply. ⛔ <b>So the migration framework (<c>IJsonDocumentMigrator</c>, <c>JsonEnvelope</c>,
/// <c>MigrationRegistry</c>) is unreachable from the code that has to read v2.</b> This file therefore
/// depends on <c>System.Text.Json</c> and nothing else, so both targets share <b>one</b>
/// implementation rather than a copy each — the denormalisation this programme keeps filing defects
/// about.
/// </para>
///
/// <para>
/// 📌 <b>Nothing calls this yet, on purpose.</b> See the batch 49 report and
/// <c>PLAN_Variable_Unification_Tasks.md</c> §<c>U-10</c>: with <c>U-9</c> built inverse — the three
/// lists are still the storage — writing v2 today would mean translating into a shape <b>no code in
/// the process uses</b>, on both the read and the write path, while carrying the highest-blast-radius
/// gate in the programme. ⭐ The transform is the half that is provable now, and
/// <c>BlueprintSchemaV2Tests</c> proves it against all 58 canonical assets.
/// </para>
///
/// <para>
/// 📌 <b>The three <c>*Order</c> lists stay per-kind in v2.</b> They are a display concern, and
/// merging them into one would need each id's kind to reconstruct — recoverable, but only while no id
/// is stale, which is a property no shipped file guarantees. ⭐ Unifying them belongs with
/// <c>U-12</c>, when one storage list makes one order list mean something.
/// </para>
/// </summary>
public static class BlueprintSchemaV2
{
    public const int V1 = 1;
    public const int V2 = 2;

    /// <summary>The v2 property that replaces the three v1 lists.</summary>
    public const string DeclarationsProperty = "Declarations";

    /// <summary>The tag each v2 declaration carries.</summary>
    public const string KindProperty = "Kind";

    // ⭐ Concatenation order IS DeclarationList.KindOrder — storage order, and the struct layout order
    //   (Params @0, working state @8, State @16). Reading it from there rather than restating it keeps
    //   the on-disk order and the in-memory view from drifting apart.
    private static readonly (DeclarationKind Kind, string List, string Order)[] Lists =
    {
        (DeclarationKind.Parameter,    "Parameters",   "ParameterOrder"),
        (DeclarationKind.WorkingState, "WorkingState", "WorkingStateOrder"),
        (DeclarationKind.Variable,     "Variables",    "VariableOrder"),
    };

    /// <summary>True when <paramref name="root"/> is already in the v2 shape.</summary>
    public static bool IsV2(JsonObject root) => root.ContainsKey(DeclarationsProperty);

    /// <summary>
    /// <b>v1 → v2.</b> The three lists become one tagged array, in place of the first of them, so
    /// every other property keeps its position.
    ///
    /// <para>
    /// ⚠ Positional rather than index-based: the transform never needs to know where in the document
    /// the declarations sit, only that they are replaced where they were. That is what makes
    /// <see cref="Down"/> able to restore byte-identical property order without a hardcoded schema.
    /// </para>
    /// </summary>
    public static JsonObject Up(JsonObject v1)
    {
        if (v1 is null) throw new ArgumentNullException(nameof(v1));
        if (IsV2(v1)) throw new InvalidOperationException("Document is already v2.");

        var declarations = new JsonArray();
        foreach (var (kind, list, _) in Lists)
        {
            if (v1[list] is not JsonArray items) continue;
            foreach (var item in items)
            {
                if (item is not JsonObject decl) continue;
                var tagged = new JsonObject { [KindProperty] = kind.ToString() };
                // ⚠ .Key/.Value, not deconstruction — netstandard2.0's KeyValuePair has no Deconstruct.
                foreach (var property in decl) tagged[property.Key] = property.Value?.DeepClone();
                declarations.Add(tagged);
            }
        }

        var result  = new JsonObject();
        var emitted = false;
        foreach (var property in v1)
        {
            var name = property.Key;
            var slot = Array.FindIndex(Lists, l => l.List == name);
            if (slot >= 0)
            {
                // The first of the three lists is where the union lands; the other two vanish.
                if (!emitted) { result[DeclarationsProperty] = declarations; emitted = true; }
                continue;
            }
            result[name] = property.Value?.DeepClone();
        }
        if (!emitted) result[DeclarationsProperty] = declarations;

        return result;
    }

    /// <summary>
    /// <b>v2 → v1 — and this is the revert.</b> The tagged array is partitioned back into the three
    /// lists, in their model order, at the position the array occupied.
    ///
    /// <para>
    /// ⛔ <b>An untagged or unknown-tagged declaration THROWS.</b> Dropping it would lose a variable
    /// silently — and a lost variable is a field that vanishes from the emitted struct, which is the
    /// blackboard-wipe failure this task's Pass 3 exists to prevent.
    /// </para>
    /// </summary>
    public static JsonObject Down(JsonObject v2)
    {
        if (v2 is null) throw new ArgumentNullException(nameof(v2));
        if (!IsV2(v2)) throw new InvalidOperationException("Document is not v2.");

        var byKind = new Dictionary<DeclarationKind, JsonArray>();
        foreach (var (kind, _, _) in Lists) byKind[kind] = new JsonArray();

        if (v2[DeclarationsProperty] is JsonArray declarations)
        {
            for (int i = 0; i < declarations.Count; i++)
            {
                if (declarations[i] is not JsonObject decl)
                    throw new InvalidDataException($"{DeclarationsProperty}[{i}] is not an object.");

                var tag = decl[KindProperty]?.GetValue<string>();
                if (tag is null || !TryParseKind(tag, out var kind))
                    throw new InvalidDataException(
                        $"{DeclarationsProperty}[{i}] carries {KindProperty}='{tag ?? "<absent>"}', which "
                        + "names no declaration list. Dropping it would silently delete a variable — and a "
                        + "deleted variable is a field missing from the emitted struct.");

                var plain = new JsonObject();
                foreach (var property in decl)
                {
                    if (property.Key == KindProperty) continue;
                    plain[property.Key] = property.Value?.DeepClone();
                }
                byKind[kind].Add(plain);
            }
        }

        var result = new JsonObject();
        foreach (var property in v2)
        {
            var name = property.Key;
            if (name == DeclarationsProperty)
            {
                foreach (var (kind, list, order) in Lists)
                {
                    result[list] = byKind[kind];
                    // ⚠ The order lists survive v2 untouched, so they are re-emitted in model order —
                    //   List then Order — rather than wherever they happened to sit.
                    if (v2.TryGetPropertyValue(order, out var orderValue))
                        result[order] = orderValue?.DeepClone();
                }
                continue;
            }
            if (Array.Exists(Lists, l => l.Order == name)) continue;   // placed above, next to its list
            result[name] = property.Value?.DeepClone();
        }

        return result;
    }

    private static bool TryParseKind(string tag, out DeclarationKind kind)
    {
        foreach (var (candidate, _, _) in Lists)
        {
            if (string.Equals(candidate.ToString(), tag, StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }
        kind = default;
        return false;
    }
}
