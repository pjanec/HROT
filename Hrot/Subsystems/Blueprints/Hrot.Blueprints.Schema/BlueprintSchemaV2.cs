using System.Text.Json.Nodes;

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

    /// <summary>
    /// ⭐⭐ <b>The v2 declaration tags — and since Q31-A this assembly is their AUTHORITY.</b>
    ///
    /// <para>
    /// ⚠ They are also <c>DeclarationKind</c>'s member names, and that type lives in
    /// <c>Hrot.Blueprints.Compiler</c>, which this assembly must not reference — referencing it is the
    /// cycle this project exists to avoid. ⛔ <b>So the strings are duplicated, deliberately.</b>
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Duplicated and PINNED, which is the only honest form of an unavoidable cross-boundary
    /// constant.</b> <c>DeclarationTagsMatchDeclarationKindTests</c> asserts
    /// <c>Enum.GetNames(typeof(DeclarationKind))</c> equals <see cref="DeclarationTags"/> <b>in
    /// order</b> — the same shape as <c>BlueprintDeclaration.MembersAParameterDoesNotCarry</c>, which
    /// is a hand-written list cross-checked by reflection so it cannot drift in either direction.
    /// ⚠ <b>Order matters as well as membership:</b> this array's order is the on-disk concatenation
    /// order, which is the struct layout order (Params @0, working state @8, State @16).
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> DeclarationTags { get; } = new[]
    {
        "Parameter", "WorkingState", "Variable",
    };

    // ⭐ Concatenation order IS DeclarationList.KindOrder — storage order, and the struct layout order.
    private static readonly (string Tag, string List, string Order)[] Lists =
    {
        ("Parameter",    "Parameters",   "ParameterOrder"),
        ("WorkingState", "WorkingState", "WorkingStateOrder"),
        ("Variable",     "Variables",    "VariableOrder"),
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

        RequireCanonicalV1(v1);

        var declarations = new JsonArray();
        foreach (var (tag, list, _) in Lists)
        {
            if (v1[list] is not JsonArray items) continue;
            foreach (var item in items)
            {
                if (item is not JsonObject decl) continue;
                var tagged = new JsonObject { [KindProperty] = tag };
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

        var byKind = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        foreach (var (tag, _, _) in Lists) byKind[tag] = new JsonArray();

        if (v2[DeclarationsProperty] is JsonArray declarations)
        {
            for (int i = 0; i < declarations.Count; i++)
            {
                if (declarations[i] is not JsonObject decl)
                    throw new InvalidDataException($"{DeclarationsProperty}[{i}] is not an object.");

                var tag = decl[KindProperty]?.GetValue<string>();
                if (tag is null || !IsKnownTag(tag))
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
                byKind[tag].Add(plain);
            }
        }

        var result = new JsonObject();
        foreach (var property in v2)
        {
            var name = property.Key;
            if (name == DeclarationsProperty)
            {
                foreach (var (tag, list, order) in Lists)
                {
                    result[list] = byKind[tag];
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

    /// <summary>
    /// ⭐⭐ <b><c>Up</c> takes CANONICAL v1 in, and says so instead of assuming it.</b>
    ///
    /// <para>
    /// ⛔ <b>Batch 54 measured four constructed shapes the transform mishandled — and the 58-file
    /// identity gate could see none of them,</b> because every shipped file is written by
    /// <c>BlueprintJsonServices</c> and is therefore canonical by construction. That is <c>BP-240</c>
    /// exactly: <i>a gate green because of what the corpus happens to do.</i>
    /// </para>
    ///
    /// <list type="number">
    ///   <item><b>A missing or <c>null</c> declaration list.</b> <c>Up</c> skipped it and <c>Down</c>
    ///   always emits all three ⇒ the round trip <b>invented</b> the property. ⚠ Not fixable in
    ///   <c>Down</c>: v2 has one array, so "absent" and "empty" are the same document.</item>
    ///   <item><b>The three lists out of model order.</b> <c>Up</c> puts the union where the FIRST of
    ///   them sat and <c>Down</c> restores all three in model order ⇒ the bytes move.</item>
    ///   <item>⛔⛔ <b>A declaration carrying its own <c>Kind</c> property.</b> The tag is written first
    ///   and the declaration's members are copied over it, so the file's value <b>overwrote the
    ///   tag</b> — and <c>Down</c> then partitions it into the wrong list. ⭐ <b>That moves a variable
    ///   between structs, which changes its offset: the blackboard-wipe failure Pass 3 exists to
    ///   prevent, reachable from one stray property.</b></item>
    /// </list>
    ///
    /// <para>
    /// ⚖️ <b>Refused rather than repaired.</b> Reconstructing an arbitrary property order would mean
    /// carrying a v1 layout artefact into v2 for a shape no writer emits; inventing a list would mean
    /// guessing. ⭐ A refusal names the file and the reason — the same ruling as <c>BP1672</c> and
    /// <see cref="Down"/>'s own throw on an untagged declaration. ⚠ Canonicalise first; the corpus
    /// already is, and <c>EveryManagedAssetIsAlreadyCanonical</c> keeps it that way.
    /// </para>
    /// </summary>
    private static void RequireCanonicalV1(JsonObject v1)
    {
        foreach (var (_, list, _) in Lists)
        {
            if (!v1.TryGetPropertyValue(list, out var value))
                throw new InvalidDataException(
                    $"v1 document has no '{list}' property. All three declaration lists must be "
                    + "present: v2 stores one array, so an absent list and an empty one are the same "
                    + "document and the down-migration would have to invent it.");

            if (value is not JsonArray)
                throw new InvalidDataException(
                    $"v1 property '{list}' is {(value is null ? "null" : value.GetType().Name)}, not an "
                    + "array. Down would write it back as [], so the round trip would not be the "
                    + "identity.");
        }

        // ⚠ RELATIVE order of the three, as they appear in the document — other properties may sit
        //   between them, which the corpus's *Order lists in fact do.
        var seen = -1;
        foreach (var property in v1)
        {
            var slot = Array.FindIndex(Lists, l => l.List == property.Key);
            if (slot < 0) continue;
            if (slot < seen)
                throw new InvalidDataException(
                    $"v1 declaration lists are not in model order: '{property.Key}' appears after "
                    + $"'{Lists[seen].List}'. Up collapses them at the position of the first, and Down "
                    + "restores them in model order, so a non-canonical order would move the bytes.");
            seen = slot;
        }

        foreach (var (_, list, _) in Lists)
        {
            var items = (JsonArray)v1[list]!;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is JsonObject decl && decl.ContainsKey(KindProperty))
                    throw new InvalidDataException(
                        $"v1 declaration {list}[{i}] already carries a '{KindProperty}' property. Up "
                        + $"writes the v2 tag under that name, so the file's value would overwrite it "
                        + "and Down would partition the declaration into the wrong list — moving the "
                        + "field between structs and changing its offset.");
            }
        }
    }

    private static bool IsKnownTag(string tag)
    {
        foreach (var (candidate, _, _) in Lists)
            if (string.Equals(candidate, tag, StringComparison.Ordinal)) return true;
        return false;
    }
}
