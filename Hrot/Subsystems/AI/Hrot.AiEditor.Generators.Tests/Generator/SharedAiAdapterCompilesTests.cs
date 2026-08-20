using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Fdp.Toolkit.Behavior.Analyzers;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Generator
{
    /// <summary>
    /// <b><c>BP-306</c> — the SharedAi adapter <c>BTreeActionGenerator</c> emits must COMPILE.</b>
    ///
    /// <para>It did not. The moment an assembly had both a <c>[BTreeAction]</c> (which is what creates
    /// a group for the adapter to live in) and a <c>[SharedAiAction]</c>, the generated
    /// <c>FbtActionRegistrar</c> failed to build — with <b>two</b> independent errors, the first
    /// masking the second:</para>
    ///
    /// <list type="number">
    ///   <item><c>CS1666</c> — the projection said <c>ref bb.BehaviorParameters</c> without the
    ///         <c>[0]</c>, and <c>BehaviorParameters</c> is a <c>fixed byte[100]</c>.</item>
    ///   <item><c>CS0214</c> — and the emitted class was not <c>unsafe</c>, which a fixed buffer needs
    ///         regardless of the indexing.</item>
    /// </list>
    ///
    /// <para>Both are the same shape: <c>BTreeActionGenerator</c>'s copy of the projection idiom had
    /// drifted from <c>BTreeBridgeEmitCore</c>'s. That is why the second test here compares the two
    /// emitters' <b>output</b> against each other rather than restating the expected text twice —
    /// restating it in two places is how they drifted in the first place.</para>
    ///
    /// <para>The probe lives in a synthetic compilation, not in the repo: a committed
    /// <c>[SharedAiAction]</c> would trip <c>HsmDtoBoundActionTripwireTests</c> by design, and
    /// weakening that tripwire to make room for a probe would be the wrong trade.</para>
    /// </summary>
    public sealed class SharedAiAdapterCompilesTests
    {
        /// <summary>
        /// A 4-param <c>[BTreeAction]</c> — without one there is no group, and the generator emits
        /// nothing at all — plus the <c>[SharedAiAction]</c> whose adapter is the subject.
        /// </summary>
        private const string ProbeSource = @"
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;

namespace Probe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ProbeParams { public float Value; }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProbeSlot { public ProbeParams Params; }

    public static class ProbeNodes
    {
        [BTreeAction]
        public static NodeStatus PlainAction(
            ref BrainBlackboard bb, ref BehaviorTreeState st, ref BTreeContext ctx, int pi)
            => NodeStatus.Success;

        [SharedAiAction(typeof(ProbeSlot), nameof(ProbeSlot.Params))]
        public static NodeStatus SharedAction(ref ProbeParams p, Entity self, EntityRepository world)
            => NodeStatus.Success;
    }
}";

        /// <summary>
        /// RED before the fix, twice over: <c>CS1666</c> first, then <c>CS0214</c> once the <c>[0]</c>
        /// was restored. Compiling the generated source is the only assertion that catches both — a
        /// text rail would have been written against whichever error was in front at the time.
        /// </summary>
        [Fact]
        public void TheGeneratedRegistrar_Compiles_WhenASharedAiActionIsPresent()
        {
            var (compilation, generated) = RunGenerator(ProbeSource);

            generated.Should().NotBeEmpty("the generator must emit a registrar for this input");
            generated.Should().Contain(t => t.ToString().Contains("Probe.ProbeNodes.SharedAction@"),
                "the SharedAi adapter is the subject — without it the compile below proves nothing");

            var errors = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            errors.Should().BeEmpty(
                "the emitted SharedAi adapter must compile: " + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        }

        /// <summary>
        /// The two emitters that write this expression — the analyzer and the persistence bridge —
        /// must produce the SAME text. Asserted against each other, not against a literal restated in
        /// this file, because a literal here would simply become a third home.
        /// </summary>
        [Fact]
        public void TheAnalyzerAndTheBridge_EmitTheSameParamsProjection()
        {
            var (_, generated) = RunGenerator(ProbeSource);
            var fromAnalyzer = ParamsProjections(string.Join("\n", generated.Select(t => t.ToString())));

            var fromBridge = BridgeProjections();

            fromAnalyzer.Should().NotBeEmpty("the analyzer must emit at least one params projection");
            fromBridge.Should().NotBeEmpty("the bridge must emit at least one params projection");

            // Normalised on the offset so the comparison is about the SHAPE, not the number.
            fromAnalyzer.Select(Normalise).Distinct()
                .Should().BeEquivalentTo(fromBridge.Select(Normalise).Distinct(),
                    "one expression, one spelling — BP-306 was two spellings of it, and one was wrong");
        }

        // ---- helpers -----------------------------------------------------------

        private static readonly Regex ProjectionRegex = new(
            @"ref Unsafe\.AddByteOffset\(ref \w+\.BehaviorParameters(\[0\])?, \((nint|IntPtr)\)\d+\)",
            RegexOptions.Compiled);

        private static IReadOnlyList<string> ParamsProjections(string source) =>
            ProjectionRegex.Matches(source).Cast<Match>().Select(m => m.Value).ToList();

        private static string Normalise(string projection) =>
            Regex.Replace(projection, @"\)\d+\)$", ")N)");

        /// <summary>
        /// The bridge side, over the REAL corpus — every BTree asset, so the comparison is against
        /// the emitter's own production output rather than a hand-built DTO that would be a third
        /// author of the expected shape.
        /// </summary>
        private static IReadOnlyList<string> BridgeProjections()
        {
            var found = new List<string>();
            foreach (var file in Golden.AiAssetCorpus.EnumerateFiles(Golden.AiAssetKind.BTree))
            {
                var dto = BTreeJsonServices.Deserialize(File.ReadAllText(file));
                if (dto == null) continue;
                // A constant size resolver: production takes this from the semantic model, but the
                // comparison below is normalised on the offset, so only the SHAPE matters here and
                // a real resolver would add a compilation this test does not otherwise need.
                found.AddRange(ParamsProjections(BTreeBridgeEmitCore.EmitBridge(dto, _ => 8)));
            }
            return found;
        }

        private static (Compilation Compilation, IReadOnlyList<SyntaxTree> Generated) RunGenerator(string source)
        {
            var input = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "Bp306Probe_" + Guid.NewGuid().ToString("N"),
                new[] { input },
                ReferenceSet(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

            var driver = CSharpGeneratorDriver.Create(new BTreeActionGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

            var generated = outputCompilation.SyntaxTrees.Where(t => t != input).ToList();
            return (outputCompilation, generated);
        }

        /// <summary>Every assembly loaded in this test process, plus the framework facades.</summary>
        private static IReadOnlyList<MetadataReference> ReferenceSet()
        {
            var refs = new List<MetadataReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic || string.IsNullOrEmpty(a.Location)) continue;
                if (!seen.Add(a.Location)) continue;
                refs.Add(MetadataReference.CreateFromFile(a.Location));
            }

            foreach (var t in new[]
                     {
                         typeof(Fdp.Toolkit.Behavior.Components.BrainBlackboard),
                         typeof(Fdp.Toolkit.Behavior.BTreeContext),
                         typeof(Fdp.Core.Entity),
                         typeof(Fbt.NodeStatus),
                         typeof(Fbt.Kernel.SharedAiActionAttribute),
                     })
                if (seen.Add(t.Assembly.Location))
                    refs.Add(MetadataReference.CreateFromFile(t.Assembly.Location));

            string dir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            foreach (var name in new[] { "System.Runtime.dll", "netstandard.dll" })
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p) && seen.Add(p))
                    refs.Add(MetadataReference.CreateFromFile(p));
            }
            return refs;
        }
    }
}
