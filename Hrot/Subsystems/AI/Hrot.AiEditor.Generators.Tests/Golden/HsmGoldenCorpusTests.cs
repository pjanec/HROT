using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Golden;

/// <summary>
/// ⭐⭐⭐ <b><c>E0</c> — the HSM golden floor.</b>
///
/// <para>
/// 🔴 <b>Why Track E needed this before anything else.</b> 📄 Plan §4B: <c>persistence-shape.txt</c>
/// covers <b>43 assets, every one a <c>.bp.json</c></b>. <c>E1</c>, <c>E3</c> and <c>E6</c> all change
/// <b>emitted HSM output</b>, so every one of them could have landed and no golden gate would have
/// noticed. ⚠ <c>BP-240</c>'s shape inverted: <b>green because the corpus does not contain the
/// thing</b>.
/// </para>
///
/// <para>
/// ⭐⭐ <b>Two tiers, and the split is the design</b> — copied from
/// <c>Hrot.Blueprints.Tests/Golden/</c>, whose reasoning is <b>cited rather than restated</b>:
/// <list type="bullet">
///   <item><b>Shape</b> — one line per asset, <c>name  SHA256(canonical)  length</c>. A <b>hash</b>,
///   because there is one thing that can move it and the message names it.</item>
///   <item>⭐⭐ <b>Emitted output</b> — <b>stored text</b>, on <c>U-1</c>'s rule <i>"a hash names the
///   asset; a stored file names the LINE"</i>. ⛔ <b>This is the half that matters for <c>E6</c></b>:
///   the shape file cannot see an id change, because the id is not in the asset.</item>
/// </list>
/// </para>
///
/// <para>
/// ⚠ <b>Backfilling <c>E1</c>/<c>E2</c> is what the seeded assets are for</b> — 📄 plan §4B: <i>"they
/// shipped under unit tests only, and this line is where that is written down."</i> ⛔ Not "add a
/// test": their emitted output is now IN the baseline, so a future change to slot manifests or
/// provisioning <b>moves a golden file</b> instead of passing quietly.
/// </para>
/// </summary>
public sealed class HsmGoldenCorpusTests
{
    private static readonly AiAssetKind Kind = AiAssetKind.Hsm;

    private const string ShapeBaseline = "Golden/hsm-persistence-shape.txt";

    private static string Sha256(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    public static IEnumerable<object[]> Assets() => AiAssetCorpus.AssetNames(Kind);

    // ── tier 1: the persisted shape ─────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The whole HSM corpus's canonical bytes, as one baseline file.</b> Same format as
    /// <c>persistence-shape.txt</c> — <c>name  SHA256(canonical)  length</c> — so the two read alike.
    /// </summary>
    [Fact]
    public void TheCanonicalJsonOfEveryCorpusAssetIsUnchanged()
    {
        var sb = new StringBuilder();
        foreach (var file in AiAssetCorpus.EnumerateFiles(Kind))
        {
            var canonical = Kind.Canonicalize(File.ReadAllText(file));
            sb.Append(Path.GetFileName(file))
              .Append("  ").Append(Sha256(canonical))
              .Append("  ").Append(canonical.Length)
              .Append('\n');
        }

        AiGoldenSnapshot.ReadOrRegenerate(ShapeBaseline, sb.ToString());
    }

    /// <summary>
    /// The canonical form is a FIXED POINT. ⚠ Same reasoning as the blueprint side's
    /// <c>RoundTripIsStable</c>: not against the hand-authored file (which loses on indentation
    /// alone), but against the canonical form.
    /// </summary>
    [Fact]
    public void RoundTripIsStable()
    {
        var unstable = new List<string>();
        foreach (var file in AiAssetCorpus.EnumerateFiles(Kind))
        {
            var once  = Kind.Canonicalize(File.ReadAllText(file));
            var twice = Kind.Canonicalize(once);
            if (!string.Equals(once, twice, StringComparison.Ordinal))
                unstable.Add(Path.GetFileName(file));
        }

        Assert.True(unstable.Count == 0,
            "canonical serialization is not a fixed point for:\n  " + string.Join("\n  ", unstable));
    }

    // ── tier 2: the emitted output ──────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The emitted output, stored as TEXT, per asset per generated part.</b>
    ///
    /// <para>
    /// ⭐ The parts are exactly <c>HsmJsonGenerator</c>'s two <c>AddSource</c> calls — the topology
    /// core and the bridge registrar — so this baselines <b>what ships</b>, not a test's idea of it.
    /// <see cref="TheHarnessEmitsTheSamePartsTheGeneratorDoes"/> pins that claim.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Assets))]
    public void TheEmittedSourceOfEveryCorpusAssetIsUnchanged(string assetName)
    {
        var json = AiAssetCorpus.ReadAsset(Kind, assetName);
        foreach (var (hint, source) in Kind.Emit(json))
            AiGoldenSnapshot.ReadOrRegenerate($"Golden/Emit/{assetName}.{hint}.txt", source);
    }

    /// <summary>
    /// ⭐⭐ <b>The emitter is DETERMINISTIC</b> — emitting twice yields identical text.
    /// ⛔ A golden gate over non-deterministic output is <b>worse than none</b>: it trains everyone to
    /// regenerate, and then the gate is a ritual. ⚠ See §5 of the batch report for the one ordering
    /// this found that is deterministic <b>by implementation detail rather than by construction</b>.
    /// </summary>
    [Theory]
    [MemberData(nameof(Assets))]
    public void TheEmitterIsDeterministic(string assetName)
    {
        var json = AiAssetCorpus.ReadAsset(Kind, assetName);
        var first  = Kind.Emit(json);
        var second = Kind.Emit(json);

        Assert.Equal(first.Select(p => p.HintName), second.Select(p => p.HintName));
        for (int i = 0; i < first.Count; i++)
            Assert.Equal(first[i].Source, second[i].Source);
    }

    // ── the harness is honest about what it measures ────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The harness knows every part the production generator can emit.</b>
    /// ⚠ The parts list is a denormalised copy of <c>HsmJsonGenerator.GenerateOneAsset</c> — the shape
    /// this programme keeps filing defects about — so it is <b>pinned rather than trusted</b>: if the
    /// generator gains or loses an <c>AddSource</c>, this reddens instead of the baseline quietly
    /// measuring the wrong thing.
    ///
    /// <para>⭐⭐⭐ <b>Batch 92 widened this from "the parts it DID emit" to "the parts it CAN emit."</b>
    /// ⛔ The old form compared the scraped <c>AddSource</c> list against <c>SampleGuard</c>'s emitted
    /// parts, which silently assumed <b>every part is unconditional</b>. 🔴 The orchestrator arm
    /// (<c>92b</c>) is emitted only for an asset with an alias — no corpus asset has one — so the old
    /// assertion reddened on a correctly-omitted file. ⇒ ⭐ the drift guard now runs against
    /// <see cref="AiAssetKind.AllHintNames"/> at full strength, and the harness is separately held to
    /// emitting only parts from that list.</para>
    /// </summary>
    [Fact]
    public void TheHarnessEmitsTheSamePartsTheGeneratorDoes()
    {
        var generatorSource = File.ReadAllText(FindGeneratorSource());
        var addSourceHints  = System.Text.RegularExpressions.Regex
            .Matches(generatorSource, @"AddSource\(baseName \+ ""\.([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.Equal(addSourceHints, Kind.AllHintNames);
    }

    /// <summary>
    /// ⭐⭐ The other half: ⛔ the harness may not INVENT a part, and may not reorder them.
    /// ⚠ <c>SampleGuard</c> has no alias, so its emitted set is a strict subset — that is the point.
    /// </summary>
    [Fact]
    public void TheHarnessEmitsOnlyPartsTheGeneratorCanEmitInOrder()
    {
        var harnessHints = Kind.Emit(
            AiAssetCorpus.ReadAsset(Kind, "SampleGuard")).Select(p => p.HintName).ToList();

        Assert.Equal(harnessHints, Kind.AllHintNames.Where(harnessHints.Contains).ToList());
    }

    /// <summary>
    /// ⭐ <b>The corpus is the GENERATOR'S INPUTS.</b> If the <c>AdditionalFiles</c> glob moves, the
    /// baseline is measuring a different set than production compiles.
    /// </summary>
    [Fact]
    public void TheCsprojStillGlobsTheCorpus()
    {
        var csproj = File.ReadAllText(FindBehaviorsCsproj());
        Assert.Contains($@"<AdditionalFiles Include=""{AiAssetCorpus.GlobInProject(Kind)}"" />", csproj);
    }

    // ── 🔴 the rail the handoff asked for: THE GATE CAN FAIL ─────────────────

    /// <summary>
    /// 🔴🔴 <b>A new green gate proves nothing — so this MUTATES the corpus and shows both tiers
    /// redden.</b>
    ///
    /// <para>
    /// ⭐ This is *"ask the artefact, not the thing that produced it"* applied to a gate: the question
    /// is not <i>"did I write a baseline"</i> but <i>"would a change move it"</i>. ⚠ Three batches
    /// running, a probe caught a rail of mine that could not fail; a gate is the same risk, one level
    /// up.
    /// </para>
    ///
    /// <para>
    /// ⛔ The mutation is in MEMORY — no corpus file is touched.
    /// </para>
    /// </summary>
    [Fact]
    public void BothTiersRedden_WhenAnAssetChanges()
    {
        var json = AiAssetCorpus.ReadAsset(Kind, "HsmVariableShowcase");

        // ── shape tier: one renamed variable moves the canonical bytes.
        var mutatedShape = json.Replace("\"Cursor\"", "\"CursorRenamed\"");
        Assert.NotEqual(json, mutatedShape);
        Assert.NotEqual(Sha256(Kind.Canonicalize(json)), Sha256(Kind.Canonicalize(mutatedShape)));

        // ── emitted tier: the SAME rename moves the emitted registrar, and the shape tier could not
        //    have told us WHICH line. That asymmetry is why the second tier exists.
        var before = Kind.Emit(json);
        var after  = Kind.Emit(mutatedShape);
        Assert.Equal(before.Count, after.Count);
        Assert.Contains(Enumerable.Range(0, before.Count),
            i => !string.Equals(before[i].Source, after[i].Source, StringComparison.Ordinal));

        // ── ⭐⭐ and a change that the SHAPE tier cannot see at all: a state's emitted name.
        //    This is E6's shape -- an identity that lives only in emitted output.
        var mutatedEmitOnly = json.Replace("\"Name\": \"Working\"", "\"Name\": \"Labouring\"");
        var emitOnly = Kind.Emit(mutatedEmitOnly);
        Assert.Contains(Enumerable.Range(0, before.Count),
            i => !string.Equals(before[i].Source, emitOnly[i].Source, StringComparison.Ordinal));
    }

    // ── the E1/E2 backfill, made explicit ───────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The backfill, asserted rather than assumed.</b> <c>E1</c>/<c>E2</c> shipped under unit
    /// tests only; the claim <i>"they are in the baseline now"</i> is only true if the corpus actually
    /// contains an asset whose emitted output carries a <b>stateful slot manifest</b>. ⛔ Both shipped
    /// assets carry no managed blackboard at all, which is exactly why the seeds exist.
    /// </summary>
    [Fact]
    public void TheSeededCorpusPutsE1sSlotManifestInTheBaseline()
    {
        var registrar = Kind.Emit(AiAssetCorpus.ReadAsset(Kind, "HsmVariableShowcase"))
            .Single(p => p.HintName == "Registrar.g.cs").Source;

        Assert.Contains("StatefulWorkingSlots", registrar);
        Assert.Contains("\"Cursor\"", registrar);   // Role=State, Behavior scope
        Assert.Contains("\"Ticks\"",  registrar);   // Role=State, Entity scope
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-281</c> — INVERTED in Batch 74. An HSM <c>Role=Input</c> variable now reaches
    /// emitted output.</b>
    ///
    /// <para>
    /// 🔴 <b>What this used to assert, and why it existed.</b> Batch 71 measured that <c>Threshold</c>
    /// (a <c>Role=Input</c> variable with a <c>DefaultValueJson</c>) appeared in <b>neither</b> emitted
    /// part — <c>HsmBridgeEmitCore</c> emitted a stateful-slot manifest and <b>no params handling of
    /// any kind</b>. ⇒ <c>DEBT-AIB-021</c>'s fix had no HSM counterpart to fix. The test asserted that
    /// gap deliberately, named for what it was, with the standing instruction to <b>invert it, not
    /// delete it</b>, when the path was built. 📌 This is that inversion.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>It asserts the variable is NAMED in the registrar</b> — the overlay arm is keyed by
    /// variable name, so the name appearing is the observable difference between "authored" and
    /// "reachable". ⭐ The bytes themselves are held by
    /// <c>HsmParseParamsEmissionTests</c>, which compiles and runs what this emits.
    /// </para>
    /// </summary>
    [Fact]
    public void AnHsmRoleInputVariable_ReachesTheEmittedRegistrar()
    {
        var parts = Kind.Emit(AiAssetCorpus.ReadAsset(Kind, "HsmVariableShowcase"));

        string registrar = parts.Single(p => p.HintName.Contains("Registrar", StringComparison.Ordinal)).Source;

        Assert.Contains("ParseParams   = __parseParams,", registrar);
        Assert.Contains("case \"Threshold\":", registrar);   // the overlay arm, keyed by variable name
        Assert.Contains("\"1.5\"", registrar);               // the authored default, baked

        // ⛔ The topology core stays out of it: params are the bridge's job, not the blob's.
        string core = parts.Single(p => !p.HintName.Contains("Registrar", StringComparison.Ordinal)).Source;
        Assert.DoesNotContain("Threshold", core);
    }

    /// <summary>
    /// ⭐ <b>The seeds cover the features the two shipped assets cannot.</b> Stated as a rail so that
    /// deleting a seed is a failure rather than a silent narrowing of the floor.
    /// </summary>
    [Fact]
    public void TheCorpusCoversTheFeaturesTrackEChanges()
    {
        var names = AiAssetCorpus.AssetNames(Kind).Select(o => (string)o[0]).ToList();

        Assert.Contains("SampleGuard",          names);   // shipped: the minimal machine
        Assert.Contains("HsmShowcase",          names);   // shipped: parallel + history + deferral
        Assert.Contains("HsmVariableShowcase",  names);   // seeded: Role=Input + Role=State (E1/E2)
        Assert.Contains("HsmOrthogonalRegions", names);   // seeded: two regions on one shared slot (E3)
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static string FindGeneratorSource()
        => FindUp(Path.Combine(
            "Hrot", "Subsystems", "AI", "Hrot.AiEditor.Generators", "HsmJsonGenerator.cs"));

    private static string FindBehaviorsCsproj()
        => FindUp(Path.Combine(
            "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj"));

    private static string FindUp(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"Not found on any ancestor: {relative}");
    }
}
