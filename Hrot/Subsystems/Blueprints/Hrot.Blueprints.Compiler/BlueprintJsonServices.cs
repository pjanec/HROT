using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hrot.Blueprints.Core.Assets;
#if NET8_0_OR_GREATER
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario;
#endif

namespace Hrot.Blueprints.Core;

public static class BlueprintJsonServices
{
    private static readonly JsonSerializerOptions _options;

    static BlueprintJsonServices()
    {
        var opts = new JsonSerializerOptions
        {
            IncludeFields               = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas         = true,
            ReadCommentHandling         = JsonCommentHandling.Skip,
            // ⭐ U-15: the canonical on-disk form is INDENTED. See Serialize().
            WriteIndented               = true,
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        opts.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        _options = opts;
    }

    /// <summary>
    /// <b>U-15 — this defines the CANONICAL on-disk form of a blueprint asset.</b>
    ///
    /// <para>
    /// ⭐⭐ <b>Indented, and that is a decision rather than a default.</b> The alternative — the
    /// compact form this wrote until <c>U-15</c> — turns each of the 58 managed assets into a single
    /// 3–12 KB line. ⛔ <b>Three measured reasons not to:</b>
    /// <list type="number">
    ///   <item><b>57 of the 58 shipped files are already indented</b>, so compact is the minority
    ///   spelling, not the incumbent one.</item>
    ///   <item>⭐ <b>The corpus is this programme's baseline.</b> Every future asset change would land
    ///   as a whole-file diff, which is the reviewability the golden harness depends on.</item>
    ///   <item>⛔ <b>It was already a live defect:</b> <c>SaveActiveBlueprintCommand</c> writes through
    ///   here, so opening a hand-authored asset in the editor and saving it collapsed the file. The
    ///   one corpus asset that was compact — <c>Loco1.bp.json</c> — is what that looks like.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// ⚠⚠ <b><c>ToJsonString()</c> takes its own options and ignored <c>_options.WriteIndented</c>
    /// entirely.</b> The flag has been set on <c>_options</c> since the envelope landed and has had
    /// <b>no effect on net8</b> — the only target that writes files in production. Both halves are set
    /// here so the two spellings cannot drift apart again.
    /// </para>
    ///
    /// <para>
    /// 📌 <b>Known, pre-existing divergence (see the tracker):</b> the <c>netstandard2.0</c> branch
    /// writes <b>no <c>$meta</c> envelope</b> — the migration types are a net8-only project reference.
    /// Harmless today because nothing on that target writes assets (the generator only reads), but it
    /// means one method name produces two different documents.
    /// </para>
    /// </summary>
    public static string Serialize(BlueprintAsset asset)
    {
        // ⭐⭐ U-10 step 3 — the canonical on-disk form is v2: one tagged `Declarations` array in place
        // of the three lists, which is the shape the model has actually had since U-12's store flip.
        //
        // ⚠ The model still serializes as v1 — `Parameters`/`WorkingState`/`Variables` are the
        // serializer-facing windows onto the store — so the v1 DOM below is produced first and lifted.
        // ⭐ That is deliberate rather than lazy: `Up` is the transform the migration registry runs, so
        // the writer and `--mode migrate` cannot disagree about what v2 means.
        //
        // ⭐ `Up` requires CANONICAL v1 (all three lists present, as arrays, in model order, no
        // declaration carrying its own `Kind`). A DOM straight from the serializer is canonical by
        // construction, which is why this is safe here and refuses elsewhere.
        var v1  = JsonSerializer.SerializeToNode(asset, _options)!.AsObject();

        // ⛔⛔ Batch 86 — THE ALIAS WRITES TWICE, and this is the one place that can know it.
        //
        // 📌 R-01 collapses WorkingState and Variables into ONE run; the model keeps both property
        // names, and both getters return that whole run. ⇒ the STJ DOM above carries every state
        // declaration in BOTH lists, and Up would tag each of them twice — once "WorkingState", once
        // "Variable". ⚠ MEASURED before this line: 2× every state field, in every asset.
        //
        // ⭐ Emptying the retired name writes each declaration exactly once, under "Variable" — which
        // is the tag the corpus was rewritten to in this same batch. ⛔ NOT done by [JsonIgnore]-ing
        // the property: the READ path needs it, because Down still emits all three v1 lists and a
        // legacy file's WorkingState entries must reach the model's setter to keep their ORDER (R-24).
        //
        // ⚠ The v1 SHAPE is untouched — three lists, so Up/Down stay each other's inverse.
        v1[BlueprintSchemaV2.LegacyWorkingStateList] = new System.Text.Json.Nodes.JsonArray();

        var dom = BlueprintSchemaV2.Up(v1);

#if NET8_0_OR_GREATER
        // Stamp $meta LAST so it lands first in the document. ⛔ The version must equal
        // BlueprintMigrationModule.CurrentVersion — a disk version AHEAD of the registry reaches
        // PersistentMigrationAdapter's Case D, which throws. V2ReaderTests pins the two together.
        JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.Blueprint, BlueprintSchemaV2.V2));
        return dom.ToJsonString(_domWriteOptions);
#else
        // 📌 The netstandard2.0 branch still writes no envelope (see the tracker). ⚠ It now writes the
        // v2 BODY without the v2 stamp — but nothing on this target writes assets; the generator only
        // reads. Kept in step with the net8 branch so the two shapes cannot diverge further.
        return dom.ToJsonString(_ns20WriteOptions);
#endif
    }

#if !NET8_0_OR_GREATER
    private static readonly JsonSerializerOptions _ns20WriteOptions = new() { WriteIndented = true };
#endif

#if NET8_0_OR_GREATER
    /// <summary>⚠ <c>JsonNode.ToJsonString</c> reads formatting from ITS options, not the ones the DOM was built with.</summary>
    private static readonly JsonSerializerOptions _domWriteOptions = new() { WriteIndented = true };
#endif

    // Note (JM-P2-004): the $meta envelope needs no handling here.
    // System.Text.Json silently ignores unknown properties (no JsonUnmappedMemberHandling.Disallow
    // in _options), so both legacy JSON (no $meta) and Phase 2 JSON ($meta first) deserialize fine.

    /// <summary>
    /// <b>U-10 — reads v1 <i>and</i> v2. ⭐ The reader ships before the writer, deliberately.</b>
    ///
    /// <para>
    /// ⭐⭐ <b>That ordering is the whole reason Batch 54's stop point is where it is.</b> A v2 file is
    /// unreadable by a build that predates this method, so every reader has to understand v2 <b>before</b>
    /// anything writes it. ⇒ this half is reversible by <c>git revert</c>; flipping
    /// <see cref="Serialize"/> is not, because a migrated file stays migrated.
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>Nothing writes v2 yet, and that is a measured blocker rather than a preference</b> — see
    /// <c>BP-235</c>: bumping <c>$meta.schemaVersion</c> requires
    /// <c>BlueprintMigrationModule.CurrentVersion</c> to move with it, a real 1→2 migrator has to be
    /// registered alongside it (a <b>passthrough</b> at 2 would silently treat a genuine v1 file as v2),
    /// and the registration lives in <c>Hrot.Common</c> — which <b>this assembly already references</b>,
    /// so the reverse edge is a project-reference cycle.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>The pre-filter is a fast NEGATIVE only, and the distinction matters.</b> Batch 49's lesson
    /// was that asserting on a spelling re-couples a test to the serializer; this asserts nothing. A v2
    /// document must contain that exact quoted property name, so its absence is conclusive, while its
    /// presence only buys a DOM parse — <see cref="BlueprintSchemaV2.IsV2"/> remains the authority.
    /// </para>
    /// </summary>
    public static BlueprintAsset? Deserialize(string json)
    {
        if (json is null) return null;

        if (json.IndexOf("\"" + BlueprintSchemaV2.DeclarationsProperty + "\"", StringComparison.Ordinal) >= 0)
        {
            var dom = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
            if (dom is not null && BlueprintSchemaV2.IsV2(dom))
                return JsonSerializer.Deserialize<BlueprintAsset>(
                    BlueprintSchemaV2.Down(dom).ToJsonString(), _options);
        }

        return JsonSerializer.Deserialize<BlueprintAsset>(json, _options);
    }
}
