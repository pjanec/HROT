using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Behavior.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Golden;

/// <summary>
/// ⭐⭐⭐ <b><c>E0</c>'s THIRD tier — the GENERATED code, baselined.</b> The coverage limit that let
/// <c>E6</c> ship.
///
/// <para>
/// 🔴 <b>The hole, measured in Batch 72.</b> <c>E0</c>'s emit tier covers
/// <c>HsmEmitCore</c>/<c>HsmBridgeEmitCore</c> output — and that output carries action <b>STRINGS</b>.
/// The <b>ids</b> are produced by <c>HsmFlattener</c> at runtime and by the <b>analyzer's</b>
/// <c>HsmActionRegistrar</c>, ⛔ <b>neither of which the baseline could see</b>. ⇒ <c>E6</c>'s defect —
/// the registrar keying on the simple name while the blob addressed the FQN — was invisible to the
/// floor built to catch exactly that class of thing. ⚠ <c>E3</c> would have been too.
/// </para>
///
/// <para>
/// ⭐⭐ <b>ONE harness, not two</b> (ruling 9). Batch 72's item 4 hit the same wall from the other
/// side: BTree's emit tier needs a Roslyn <c>Compilation</c>. A <c>CSharpGeneratorDriver</c> answers
/// both, so there is one.
/// </para>
///
/// <para>
/// ⭐⭐⭐ <b>The acceptance test is <see cref="TheRegistrarBaseline_CarriesTheActionIds"/> plus the
/// revert probe recorded in the batch report:</b> reverting <c>E6</c>'s FQN key <b>moves this
/// baseline</b>. ⛔ A tier that stays green under that revert does not reach what it was built for.
/// </para>
/// </summary>
public sealed class GeneratedEmitGoldenTests
{
    /// <summary>
    /// ⚠ Minimal stubs for the kernel surface the analyzer binds against. ⭐ Only the shapes
    /// <c>HsmActionGenerator</c> reads — the attributes and the writer type — so the fixture cannot
    /// drift into re-implementing the kernel.
    /// </summary>
    private const string KernelStubs = @"
namespace Fhsm.Kernel.Data
{
    public struct HsmCommandWriter { }
    public enum CommandLane { None = 0 }
}
namespace Fhsm.Kernel.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class HsmActionAttribute : System.Attribute
    {
        public string Name { get; set; }
        public Fhsm.Kernel.Data.CommandLane Lane { get; set; }
    }
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class HsmGuardAttribute : System.Attribute
    {
        public string Name { get; set; }
    }
}
namespace Fhsm.Kernel
{
    public static unsafe class HsmActionDispatcher
    {
        public static void RegisterAction(ushort id, System.IntPtr action) { }
        public static void RegisterGuard(ushort id, System.IntPtr guard) { }
    }
}";

    /// <summary>
    /// ⭐ The REAL <c>CgfHsmNodes.cs</c>, read from the tree. ⛔ Not a hand-written stand-in: the whole
    /// point is that the baseline moves when the shipped methods or the key rule change.
    /// </summary>
    private static string RealHsmActionSource()
        => File.ReadAllText(FindUp(Path.Combine(
            "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Brains", "CgfHsmNodes.cs")));

    private static IReadOnlyList<(string HintName, string Source)> RunHsmActionGenerator()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "Hrot.AI.Behaviors",   // ⭐ non-kernel ⇒ the analyzer emits the REGISTRAR
            syntaxTrees: new[] { KernelStubs, RealHsmActionSource() }
                .Select(s => CSharpSyntaxTree.ParseText(s)),
            references: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(
                    Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
            },
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var driver = CSharpGeneratorDriver
            .Create(new HsmActionGenerator())
            .RunGenerators(compilation);

        return driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .OrderBy(g => g.HintName, StringComparer.Ordinal)
            .Select(g => (g.HintName, g.SourceText.ToString()))
            .ToList();
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The generated <c>HsmActionRegistrar</c>, baselined as stored TEXT.</b> This is the file
    /// the ids live in, and therefore the file <c>E6</c> moved.
    /// </summary>
    [Fact]
    public void TheGeneratedRegistrarIsUnchanged()
    {
        var parts = RunHsmActionGenerator();
        Assert.NotEmpty(parts);   // ⛔ an empty run would make the baseline vacuous

        foreach (var (hint, source) in parts)
            AiGoldenSnapshot.ReadOrRegenerate($"Golden/Generated/{hint}.txt", source);
    }

    /// <summary>
    /// 🔴🔴 <b>The acceptance test, in the form a test can carry: the baseline really does contain the
    /// ids, and they are the FQN's.</b> ⛔ If the registrar carried only names, reverting <c>E6</c>
    /// would leave the baseline untouched and this whole tier would be decoration.
    ///
    /// <para>
    /// ⭐ The expected id is <b>computed</b> from the FQN here and compared against the GENERATED text —
    /// two independent derivations. ⚠ The revert probe in the batch report is the other half: with the
    /// simple-name key restored, <see cref="TheGeneratedRegistrarIsUnchanged"/> reddens.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRegistrarBaseline_CarriesTheActionIds()
    {
        var registrar = RunHsmActionGenerator()
            .Single(p => p.HintName.Contains("Registrar", StringComparison.Ordinal)).Source;

        ushort fqnId    = Fnv1a16("Hrot.AI.Behaviors.CgfHsmNodes.StubIdle");
        ushort simpleId = Fnv1a16("StubIdle");
        Assert.NotEqual(fqnId, simpleId);   // ⭐ the two keys really are distinguishable

        Assert.Contains($"RegisterAction({fqnId},", registrar);
        Assert.DoesNotContain($"RegisterAction({simpleId},", registrar);
    }

    /// <summary>⭐ Deterministic in-process; the report records the cross-process check.</summary>
    [Fact]
    public void TheGeneratorIsDeterministic()
    {
        var first  = RunHsmActionGenerator();
        var second = RunHsmActionGenerator();

        Assert.Equal(first.Select(p => p.HintName), second.Select(p => p.HintName));
        for (int i = 0; i < first.Count; i++)
            Assert.Equal(first[i].Source, second[i].Source);
    }

    /// <summary>
    /// ⚠⚠ <b>The boundary of a SYNTHESIZED compilation, named rather than discovered later.</b>
    ///
    /// <para>
    /// ⭐ The HSM analyzer needs only the attribute shapes, so a synthesized compilation reaches it
    /// fully — which is why the acceptance test lands here. ⛔ <b>BTree's generator does not:</b> it
    /// builds a <c>structSizeResolver</c> from the compilation's SEMANTIC MODEL (real struct layouts)
    /// and runs <c>BTreeDeactivatorScanner.Scan(compilation, …)</c> over real method bodies. A
    /// synthesized compilation carrying neither would emit fallback output — ⛔ <b>a baseline of
    /// something production never produces</b>, the same trap <c>GoldenCorpus.Options()</c> records.
    /// </para>
    ///
    /// <para>
    /// ⇒ ⭐ BTree's emit tier needs the REAL solution compilation, not a driver over stubs. ⚠ Stated as
    /// a measurement so the next batch can size it; ⭐ <b>invert this when it lands.</b>
    /// </para>
    /// </summary>
    [Fact]
    public void TheSynthesizedCompilationReachesHsm_ButNotBTree()
    {
        // ⭐ HSM: fully reached — the registrar is generated and carries real ids.
        Assert.Contains(RunHsmActionGenerator(),
            p => p.HintName.Contains("Registrar", StringComparison.Ordinal));

        // ⛔ BTree: the two compilation-derived inputs, read from the generator's own source.
        var btree = File.ReadAllText(FindUp(Path.Combine(
            "Hrot", "Subsystems", "AI", "Hrot.AiEditor.Generators", "BTreeJsonGenerator.cs")));
        Assert.Contains("structSizeResolver", btree);
        Assert.Contains("BTreeDeactivatorScanner.Scan(compilation", btree);

        // ⭐ …and the shape tier is what BTree has until then (Batch 72, item 4).
        Assert.Empty(AiAssetKind.BTree.Emit(
            File.ReadAllText(AiAssetCorpus.EnumerateFiles(AiAssetKind.BTree).First())));
    }

    private static ushort Fnv1a16(string s)
    {
        uint hash = 2166136261;
        foreach (char c in s) { hash ^= c; hash *= 16777619; }
        return (ushort)(hash & 0xFFFF);
    }

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
