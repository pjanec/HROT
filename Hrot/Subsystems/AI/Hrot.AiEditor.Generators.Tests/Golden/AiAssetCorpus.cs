using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.AiEditor.Generators.Tests.Golden;

/// <summary>
/// ⭐⭐⭐ <b><c>E0</c> — the AI golden-corpus harness. The prerequisite Track E has been missing.</b>
///
/// <para>
/// 🔴 <b>What was wrong.</b> 📄 Plan §4B: <i>"Track E has NO golden coverage."</i>
/// <c>persistence-shape.txt</c> is <b>43 assets, all <c>.bp.json</c></b> ⇒ <c>E1</c>, <c>E3</c> and
/// <c>E6</c> all change emitted output and <b>no golden gate would notice</b>. ⚠ <c>BP-240</c>'s shape
/// inverted: <b>green because the corpus does not contain the thing</b>.
/// </para>
///
/// <para>
/// ⭐⭐ <b>The instrument is copied, not reinvented</b> — <c>Hrot.Blueprints.Tests/Golden/</c>'s
/// <c>PersistenceShapeTests</c> + <c>GoldenCorpus</c>. ⛔ <b>Its reasoning is CITED, not restated:</b>
/// see <c>PersistenceShapeTests</c>'s own doc comment for why a <b>recorded baseline</b> beats
/// <c>Serialize(Deserialize(j)) == j</c> — <i>round-tripping is closed under a leak</i>, so a gate that
/// passes either way is not a gate. The same split applies here: a <b>hash</b> names the asset for the
/// shape tier, <b>stored text</b> names the LINE for the emitted tier.
/// </para>
///
/// <para>
/// ⭐ <b>Generic over ASSET KIND, and it cost nothing.</b> BTree's 26 ungated assets differ only in a
/// glob, a deserializer and which emit-core calls make up "the emitted output" — all three are
/// already static, DTO-in / string-out functions. ⇒ <see cref="AiAssetKind"/> is three delegates, and
/// seeding BTree later is a <b>registration, not a rewrite</b>. ⛔ Deliberately NOT seeded here.
/// </para>
/// </summary>
public sealed record AiAssetKind(
    /// <summary>Baseline file prefix, e.g. <c>hsm</c>.</summary>
    string Name,
    /// <summary>Corpus directory under <c>Hrot.AI.Behaviors/Assets/</c>, e.g. <c>HSMs</c>.</summary>
    string CorpusSubdirectory,
    /// <summary>File suffix the production <c>AdditionalFiles</c> glob matches, e.g. <c>.hsm.json</c>.</summary>
    string FileSuffix,
    /// <summary>Raw JSON → canonical JSON (deserialize + reserialize through the shipped services).</summary>
    Func<string, string> Canonicalize,
    /// <summary>
    /// Raw JSON → the emitted parts, keyed by the generator's own hint name. ⭐ These MUST be the same
    /// calls the production generator makes, in the same order — that is what makes the baseline a
    /// baseline of what ships rather than of a test's idea of it.
    /// </summary>
    Func<string, IReadOnlyList<(string HintName, string Source)>> Emit)
{
    /// <summary>
    /// ⭐ <b>HSM.</b> The emitted parts are exactly <c>HsmJsonGenerator.GenerateOneAsset</c>'s two
    /// <c>AddSource</c> calls: the topology core (<c>{Name}.g.cs</c>, ⛔ <b>no <c>[HsmLayout]</c></b> —
    /// layout lives in JSON) and the bridge registrar (<c>{Name}.Registrar.g.cs</c>).
    /// </summary>
    public static readonly AiAssetKind Hsm = new(
        Name:               "hsm",
        CorpusSubdirectory: "HSMs",
        FileSuffix:         ".hsm.json",
        Canonicalize:       json => HsmJsonServices.Serialize(
                                HsmJsonServices.Deserialize(json)
                                ?? throw new InvalidDataException("Deserialized null.")),
        Emit:               json =>
        {
            var dto = HsmJsonServices.Deserialize(json)
                      ?? throw new InvalidDataException("Deserialized null.");
            return new (string, string)[]
            {
                ("g.cs",           HsmEmitCore.EmitTopologyCore(dto)),
                ("Registrar.g.cs", HsmBridgeEmitCore.EmitBridge(dto)),
            };
        });

    /// <summary>
    /// ⭐ <b>BTree — the SHAPE tier only, and that limit is deliberate.</b>
    ///
    /// <para>
    /// ⚠⚠ <b>This corrects my own Batch 71 claim.</b> I measured <i>"three delegates ⇒ BTree is a
    /// registration, not a rewrite"</i>, and the coordinator made it a line item on that word. 📐
    /// Re-measured: the <b>canonicalize</b> half is genuinely pure and registers exactly as promised,
    /// but the <b>emit</b> half is not. <c>BTreeJsonGenerator</c> passes a
    /// <c>structSizeResolver</c> built from a Roslyn <c>Compilation</c> and a
    /// <c>BTreeDeactivatorScanner.Scan(compilation, …)</c> result into every emit call; the
    /// resolver-less overloads exist, but their output is <b>not what ships</b>.
    /// </para>
    ///
    /// <para>
    /// ⛔ Baselining the resolver-less form would repeat the mistake <c>GoldenCorpus.Options()</c>'s own
    /// doc comment records — recording output that production never produces. ⇒ ⭐ the emit tier waits
    /// for a <c>CSharpGeneratorDriver</c> harness, and
    /// <c>BTreeGoldenCorpusTests.TheBTreeEmitTierNeedsARoslynCompilation</c> pins the reason so it is a
    /// measurement rather than an omission.
    /// </para>
    /// </summary>
    public static readonly AiAssetKind BTree = new(
        Name:               "btree",
        CorpusSubdirectory: "BTrees",
        FileSuffix:         ".btree.json",
        Canonicalize:       json => BTreeJsonServices.Serialize(
                                BTreeJsonServices.Deserialize(json)
                                ?? throw new InvalidDataException("Deserialized null.")),
        Emit:               _ => Array.Empty<(string, string)>());
}

/// <summary>The corpus itself: where it lives, what is in it, and how it is ordered.</summary>
public static class AiAssetCorpus
{
    /// <summary>
    /// ⭐ <b>The corpus is the GENERATOR'S INPUTS</b> — <c>Hrot.AI.Behaviors.csproj</c> carries
    /// <c>&lt;AdditionalFiles Include="Assets\HSMs\**\*.hsm.json" /&gt;</c>, and
    /// <see cref="HsmGoldenCorpusTests.TheCsprojStillGlobsTheCorpus"/> asserts it still says so.
    /// ⛔ Taking a hardcoded file list instead would make the baseline silently stop covering a new
    /// asset — which is precisely the hole this harness exists to close.
    /// </summary>
    public static string GlobInProject(AiAssetKind kind)
        => $@"Assets\{kind.CorpusSubdirectory}\**\*{kind.FileSuffix}";

    public static string ResolveCorpusDir(AiAssetKind kind)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", kind.CorpusSubdirectory);
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            $"AI corpus not found — expected Hrot/Subsystems/Hrot.AI.Behaviors/Assets/"
            + $"{kind.CorpusSubdirectory} on an ancestor of the test output directory.");
    }

    /// <summary>Every corpus file, ordered so the sweep and its baseline are deterministic.</summary>
    public static IReadOnlyList<string> EnumerateFiles(AiAssetKind kind)
        => Directory.GetFiles(ResolveCorpusDir(kind), "*" + kind.FileSuffix, SearchOption.AllDirectories)
                    .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                    .ToList();

    /// <summary>The corpus as xunit theory data — one test per asset, so a failure names it.</summary>
    public static IEnumerable<object[]> AssetNames(AiAssetKind kind)
        => EnumerateFiles(kind).Select(f => new object[] { StripSuffix(kind, Path.GetFileName(f)) });

    public static string StripSuffix(AiAssetKind kind, string fileName)
        => fileName.EndsWith(kind.FileSuffix, StringComparison.Ordinal)
            ? fileName[..^kind.FileSuffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);

    public static string ReadAsset(AiAssetKind kind, string assetName)
    {
        var path = EnumerateFiles(kind).FirstOrDefault(
                f => string.Equals(StripSuffix(kind, Path.GetFileName(f)), assetName, StringComparison.Ordinal))
            ?? throw new FileNotFoundException($"No {kind.Name} corpus asset named '{assetName}'.");
        return File.ReadAllText(path);
    }
}
