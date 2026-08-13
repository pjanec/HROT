using System.Text;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;

namespace Hrot.Blueprints.Tests.Golden;

/// <summary>
/// <b>U-1 — the golden-corpus harness.</b>
///
/// <para>
/// ⭐⭐ <b>Why it exists, and why it exists FIRST.</b> Every later <c>U-</c> task's success condition is
/// <i>"the output did not change"</i>, and that sentence is <b>unfalsifiable without a recorded
/// baseline</b>. This task ships no product change at all — it is the instrument the rest of the
/// programme is measured with.
/// </para>
///
/// <para>
/// ⭐ <b>Two tiers, and the split is the whole design</b> (the reviewed invariant):
/// <list type="bullet">
///   <item><b>Tier 1 — never moves undeclared.</b> <c>StructureHash</c>, every emitted struct field
///   (name · type · offset · size), and the diagnostic multiset (code × count). A change here is a
///   <b>failure</b>, not a rebase: offsets are what the running blackboard is addressed by.</item>
///   <item><b>Tier 2 — moves with a regenerated baseline.</b> The full generated source, stored as
///   FILES and ⛔ <b>not hashed</b> — <i>"a hash names the asset; a stored file names the LINE."</i>
///   ⚠ This programme's recurring defect shape is a denormalised copy no test compares against its
///   source; a hash would be that shape again.</item>
/// </list>
/// </para>
/// </summary>
public static class GoldenCorpus
{
    /// <summary>
    /// ⭐⭐ <b>The corpus is the GENERATOR'S INPUTS — take the glob, not the count.</b>
    ///
    /// <para>
    /// <c>Hrot.AI.Behaviors.csproj</c>: <c>&lt;AdditionalFiles Include="Assets\Blueprints\**\*.bp.json" /&gt;</c>.
    /// ⛔ <b>Not "all shipped <c>.bp.json</c>".</b> <c>Recipes/Blueprints</c> is <c>Content</c> —
    /// production never compiles it — and globbing <b>both</b> roots <b>throws</b>, because assets
    /// exist in each sharing an <c>AssetId</c>. It happens to be 42 files today; the definition is the
    /// glob, and <c>GoldenCorpusTests</c> asserts the <c>.csproj</c> still says so.
    /// </para>
    /// </summary>
    public const string CorpusGlobInProject = @"Assets\Blueprints\**\*.bp.json";

    /// <summary>
    /// ⚠ <b>The preload.</b> Three <c>HillAssault2I_*</c> assets fail <c>BP1602</c> under a bare
    /// compile: a null <see cref="IClrSignatureResolver"/> makes Stage 0 reflect over <b>loaded</b>
    /// assemblies, and nothing has loaded <c>Hrot.AI.Behaviors</c> yet. ⭐ One type touch ⇒ 42/42.
    /// </summary>
    public static void EnsureBehaviorAssemblyLoaded()
        => _ = typeof(Hrot.AI.Behaviors.BpComponentDemo).Assembly;

    /// <summary>The source directory the glob is rooted at.</summary>
    public static string ResolveCorpusDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "Blueprints");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            "Golden corpus not found — expected Hrot/Subsystems/Hrot.AI.Behaviors/Assets/Blueprints "
            + "on an ancestor of the test output directory.");
    }

    /// <summary>Every corpus file, ordered so the sweep and its baseline are deterministic.</summary>
    public static IReadOnlyList<string> EnumerateFiles()
        => Directory.GetFiles(ResolveCorpusDir(), "*.bp.json", SearchOption.AllDirectories)
                    .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                    .ToList();

    /// <summary>The corpus as xunit theory data — one test per asset, so a failure names it.</summary>
    public static IEnumerable<object[]> AssetNames()
        => EnumerateFiles().Select(f => new object[] { StripSuffix(Path.GetFileName(f)) });

    private static string StripSuffix(string fileName)
        => fileName.EndsWith(".bp.json", StringComparison.Ordinal)
            ? fileName[..^".bp.json".Length]
            : Path.GetFileNameWithoutExtension(fileName);

    public static BlueprintAsset Load(string assetName)
    {
        var path = EnumerateFiles().FirstOrDefault(
            f => string.Equals(StripSuffix(Path.GetFileName(f)), assetName, StringComparison.Ordinal))
            ?? throw new FileNotFoundException($"No corpus asset named '{assetName}'.");
        return BlueprintJsonServices.Deserialize(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Deserialized null from '{path}'.");
    }

    private static IReadOnlyList<BlueprintSignature>? _siblings;

    /// <summary>
    /// ⭐⭐ <b>The corpus compiles as a SET, not asset by asset — and that was not in the plan.</b>
    ///
    /// <para>
    /// ⛔ <b>Measured while building this harness:</b> with <c>SiblingSignatures</c> empty,
    /// <c>SmokeGuard</c> and <c>SmokePatrol</c> fail <c>BP1301</c> — <i>"CallablePeer {id} not found
    /// among compiled assets. Add as &lt;AdditionalFiles&gt;"</i> — because they call each other. ⚠ So
    /// the *"42/42 with one <c>typeof(...).Assembly</c> touch"* figure the plan and the handoff both
    /// carry is <b>40/42</b>: the preload fixes Stage 0's CLR reflection, and a second, unrelated
    /// cross-asset dependency was never accounted for.
    /// </para>
    ///
    /// <para>
    /// ⭐ Production has always done this: <c>BlueprintIncrementalGenerator</c> parses <b>every</b>
    /// <c>AdditionalFiles</c> entry into a sibling catalog and hands the whole array to every compile.
    /// The harness mirrors it with the same parser, so the corpus is compiled the way it ships.
    /// </para>
    /// </summary>
    public static IReadOnlyList<BlueprintSignature> SiblingCatalog()
        => _siblings ??= EnumerateFiles()
            .Select(f => BlueprintSignatureParser.Parse(f, File.ReadAllText(f)))
            .ToList();

    /// <summary>
    /// ⭐⭐ <b><c>Release</c>, because that is the only mode production ever emits.</b>
    ///
    /// <para>
    /// ⛔ <b>Measured, and it was wrong in the first draft of this harness.</b>
    /// <c>BlueprintIncrementalGenerator.CompileOneAsset</c> hardcodes
    /// <c>Mode: CompilerMode.Release</c> — ⚠ <b>not</b> derived from the MSBuild configuration, so a
    /// Debug build of the solution still emits Release blueprint code. A harness on
    /// <c>CompilerMode.Debug</c> records ~40 extra <c>DebugProbe.NodeEnter</c> lines per asset and
    /// would have made the golden set a baseline for output that <b>never ships</b>. (The three
    /// pre-existing <c>*EmitGoldenTests</c> use Debug; they are hand-picked illustrations rather than
    /// a production baseline, so they are left alone.)
    /// </para>
    ///
    /// <para>
    /// 📌 <b>Known gap, stated rather than papered over:</b> Debug-mode emit — the <c>DebugProbe</c>
    /// calls and the <c>DebugMap</c> the breakpoint surface depends on — is <b>not</b> covered by this
    /// baseline.
    /// </para>
    /// </summary>
    public static CompileOptions Options() => new(
        Mode:              CompilerMode.Release,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: SiblingCatalog());

    /// <summary>What one corpus asset contributes to the baseline.</summary>
    public sealed record Record(string Tier1, string Tier2, IrAsset? Lowered, IReadOnlyList<Diagnostic> Diagnostics);

    /// <summary>
    /// Runs the pipeline and produces both tiers.
    ///
    /// <para>
    /// ⚠⚠ <b>This mirrors <c>BlueprintCompiler.Compile</c>'s stage sequence rather than calling it,
    /// and that is a denormalised copy — the very shape this programme keeps filing defects about.</b>
    /// It is unavoidable here: Tier 1 needs the <b>laid-out <see cref="IrAsset"/></b> (offsets and
    /// sizes live on <see cref="IrField"/>, computed in Stage 6) and <see cref="CompileResult"/>
    /// exposes only the hash, the source and the diagnostics.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>So the copy is pinned rather than trusted:</b>
    /// <c>GoldenCorpusTests.HarnessPipelineMatchesTheRealCompiler</c> asserts this sequence produces
    /// byte-identical source and an identical hash to <see cref="BlueprintCompiler.Compile"/> for
    /// <b>every</b> corpus asset. If the real sequence gains or loses a stage, that test reddens
    /// immediately instead of the baseline quietly measuring the wrong pipeline.
    /// </para>
    /// </summary>
    public static Record Compile(BlueprintAsset asset)
    {
        EnsureBehaviorAssemblyLoaded();

        var opts = Options();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        // Compile() works on a shallow copy so caller graphs are not mutated (U-2). The harness
        // deserializes a fresh asset per call, so it needs no equivalent.
        Stage0_Rehydrate.Run(asset, opts);
        Stage2_Validate.Run(asset, ctx);

        IrAsset? lowered = null;
        string   source  = "";

        if (!sink.HasErrors)
        {
            asset = Stage2_5_ExpandMacros.Run(asset, ctx);
            if (!sink.HasErrors)
            {
                asset = Stage3_Normalize.Run(asset, ctx);
                if (!sink.HasErrors)
                {
                    var typed = Stage4_TypeResolve.Run(asset, ctx);
                    if (!sink.HasErrors)
                    {
                        var ir = Stage5_Schedule.Run(typed, ctx);
                        if (!sink.HasErrors)
                        {
                            lowered = Stage6_Lower.Run(ir, opts.Mode, sink);
                            if (!sink.HasErrors)
                                (source, _) = Stage7_Emit.Run(
                                    lowered, opts.Mode, sink, opts.SiblingSignatures);
                        }
                    }
                }
            }
        }

        return new Record(
            Tier1:       RenderTier1(lowered, sink.All),
            Tier2:       source,
            Lowered:     lowered,
            Diagnostics: sink.All);
    }

    /// <summary>
    /// ⭐ <b>Tier 1, rendered as text on purpose.</b> A structured baseline that a human cannot read in
    /// a diff is only marginally better than a hash. Every line is one fact.
    /// </summary>
    public static string RenderTier1(IrAsset? lowered, IReadOnlyList<Diagnostic> diagnostics)
    {
        var sb = new StringBuilder();

        if (lowered is null)
        {
            sb.Append("StructureHash: <not reached>\n");
        }
        else
        {
            sb.Append("StructureHash: 0x").Append(lowered.StructureHash.ToString("X16")).Append('\n');
            AppendFields(sb, "Parameters",      lowered.Parameters);
            AppendFields(sb, "WorkingState",    lowered.WorkingState);
            AppendFields(sb, "Variables",       lowered.Variables);
            // BP-57/Q27-A3: laid out after the asset's own storage and part of the hash, so a change
            // here is exactly as load-bearing as a change to the three lists above.
            AppendFields(sb, "GraphLocalSlots", lowered.GraphLocalSlots);
        }

        // ⭐ The MULTISET, not the list: code × count. Diagnostic order is a scheduling detail and
        // would make this tier fail for reasons that are not about the output.
        sb.Append("Diagnostics:\n");
        var multiset = diagnostics
            .GroupBy(d => d.Code, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        var any = false;
        foreach (var g in multiset)
        {
            sb.Append("  ").Append(g.Key).Append(" x").Append(g.Count()).Append('\n');
            any = true;
        }
        if (!any) sb.Append("  (none)\n");

        return sb.ToString();
    }

    private static void AppendFields(StringBuilder sb, string list, IReadOnlyList<IrField> fields)
    {
        sb.Append(list).Append(":\n");
        if (fields.Count == 0) { sb.Append("  (empty)\n"); return; }
        foreach (var f in fields)
            sb.Append("  ").Append(f.Name)
              .Append(" : ").Append(f.Type.FullName)
              .Append(" @").Append(f.Offset)
              .Append(" size=").Append(f.Size)
              .Append('\n');
    }
}
