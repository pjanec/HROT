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
#if NET8_0_OR_GREATER
        // Serialize the asset to a DOM, then stamp $meta as the first property.
        var dom = JsonSerializer.SerializeToNode(asset, _options)!.AsObject();
        JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.Blueprint, 1));
        return dom.ToJsonString(_domWriteOptions);
#else
        return JsonSerializer.Serialize(asset, _options);
#endif
    }

#if NET8_0_OR_GREATER
    /// <summary>⚠ <c>JsonNode.ToJsonString</c> reads formatting from ITS options, not the ones the DOM was built with.</summary>
    private static readonly JsonSerializerOptions _domWriteOptions = new() { WriteIndented = true };
#endif

    // Note (JM-P2-004): Deserialize needs no change.
    // System.Text.Json silently ignores unknown properties (no JsonUnmappedMemberHandling.Disallow
    // in _options), so a Phase 2 envelope with $meta is already handled transparently.
    // Both legacy JSON (no $meta) and Phase 2 JSON ($meta first) are deserialized correctly.
    public static BlueprintAsset? Deserialize(string json)
        => JsonSerializer.Deserialize<BlueprintAsset>(json, _options);
}
