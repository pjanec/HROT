using System.Collections.Immutable;
using System.Linq;
using Fdp.Toolkit.Behavior.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Fdp.Toolkit.Tests.Utility
{
    /// <summary>
    /// Unit tests for UtilityAuthoringAnalyzer (TASK-UAI-P2-03).
    /// Uses in-memory Roslyn compilation and runs the analyzer via WithAnalyzers.
    /// Covers success conditions SC-P2-03-1, SC-P2-03-2, SC-P2-03-3.
    /// </summary>
    public sealed class UtilityAuthoringAnalyzerTests
    {
        // ---- Stubs for in-memory compilation ------------------------------------

        private const string CommonStubs = @"
namespace Fdp.Toolkit.Utility
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class UtilityDecisionAttribute : System.Attribute
    {
        public string AssetId { get; }
        public string DisplayName { get; }
        public DecisionKind Kind { get; }
        public UtilityDecisionAttribute(string assetId, string displayName, DecisionKind kind,
            string category = """", float hysteresisBonus = 0f)
        { AssetId = assetId; DisplayName = displayName; Kind = kind; }
    }
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class UtilityInputAttribute : System.Attribute
    {
        public string Name { get; }
        public UtilityInputAttribute(string name) { Name = name; }
    }
    public enum DecisionKind : byte { ThreatRanking = 0, PostureSelect = 1, WeaponSelection = 2 }
    public enum InputContext : byte { Self = 0, Target = 1, Leader = 2, Candidate = 3 }
    public interface IUtilityDecisionBuilder
    {
        IUtilityDecisionBuilder Option(ushort optionId, ScoringMode mode,
            System.Action<IUtilityOptionBuilder> configure);
        IUtilityDecisionBuilder CandidateOption(ScoringMode mode,
            System.Action<IUtilityOptionBuilder> configure);
    }
    public interface IUtilityOptionBuilder
    {
        IUtilityOptionBuilder Consider(InputRef input, float weight, ResponseCurve curve);
    }
    public enum ScoringMode : byte { WeightedProduct = 0, WeightedSum = 1 }
    public readonly struct InputRef
    {
        public readonly ushort InputId;
        public readonly InputContext Context;
        public InputRef(ushort inputId, InputContext context = default) { InputId = inputId; Context = context; }
    }
    public readonly struct ResponseCurve { }
    public static class Curve
    {
        public static readonly ResponseCurve Linear = default;
    }
    public static partial class In
    {
        [UtilityInput(""AmmoFraction"")]
        public static InputRef AmmoFraction(InputContext ctx = InputContext.Self) => default;
    }
}
";

        // Helper: create standard platform references for in-memory compilation.
        private static System.Collections.Generic.List<MetadataReference> GetBaseReferences()
        {
            var platformPaths = ((string)(System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? string.Empty))
                .Split(System.IO.Path.PathSeparator,
                    System.StringSplitOptions.RemoveEmptyEntries);

            var refs = new System.Collections.Generic.List<MetadataReference>();
            foreach (var p in platformPaths)
            {
                var name = System.IO.Path.GetFileName(p);
                if (name == "System.Private.CoreLib.dll"
                    || name == "System.Runtime.dll"
                    || name == "System.Collections.dll"
                    || name == "netstandard.dll")
                {
                    refs.Add(MetadataReference.CreateFromFile(p));
                }
            }
            return refs;
        }

        private static ImmutableArray<Diagnostic> RunAnalyzer(string userSource)
        {
            string combined = userSource + "\n" + CommonStubs;
            var syntaxTree = CSharpSyntaxTree.ParseText(combined);

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                syntaxTrees: new[] { syntaxTree },
                references: GetBaseReferences(),
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Disable));

            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
                new UtilityAuthoringAnalyzer());

            return compilation.WithAnalyzers(analyzers)
                .GetAnalyzerDiagnosticsAsync().Result;
        }

        // ---- SC-P2-03-2a: clean Build body -> 0 diagnostics from UT0130 ---------

        [Fact]
        public void PureBuild_ProducesNoDiagnostics()
        {
            const string source = @"
using Fdp.Toolkit.Utility;
namespace My.Decisions
{
    [UtilityDecision(""guid-1"", ""Hold"", DecisionKind.PostureSelect)]
    public class HoldDecision
    {
        public static void Build(IUtilityDecisionBuilder b)
        {
            b.Option(0, ScoringMode.WeightedSum, o =>
                o.Consider(In.AmmoFraction(), 0.8f, Curve.Linear));
        }
    }
}
";
            var diagnostics = RunAnalyzer(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "UT0130");
        }

        // ---- SC-P2-03-2b: Build reads static mutable field -> UT0130 -------------

        [Fact]
        public void ImpureBuild_StaticField_EmitsUT0130()
        {
            const string source = @"
using Fdp.Toolkit.Utility;
namespace My.Decisions
{
    [UtilityDecision(""guid-2"", ""Impure"", DecisionKind.ThreatRanking)]
    public class ImpureDecision
    {
        private static float _bias = 0.5f;
        public static void Build(IUtilityDecisionBuilder b)
        {
            float v = _bias;
            b.CandidateOption(ScoringMode.WeightedProduct, o =>
                o.Consider(In.AmmoFraction(), v, Curve.Linear));
        }
    }
}
";
            var diagnostics = RunAnalyzer(source);
            Assert.Contains(diagnostics, d => d.Id == "UT0130");
        }

        // ---- SC-P2-03-2c: Build reads DateTime.Now -> UT0130 --------------------

        [Fact]
        public void ImpureBuild_DateTime_EmitsUT0130()
        {
            const string source = @"
using Fdp.Toolkit.Utility;
namespace My.Decisions
{
    [UtilityDecision(""guid-3"", ""DateImpure"", DecisionKind.ThreatRanking)]
    public class DateImpureDecision
    {
        public static void Build(IUtilityDecisionBuilder b)
        {
            var t = System.DateTime.Now;
            b.CandidateOption(ScoringMode.WeightedProduct, o =>
                o.Consider(In.AmmoFraction(), 0.5f, Curve.Linear));
        }
    }
}
";
            var diagnostics = RunAnalyzer(source);
            Assert.Contains(diagnostics, d => d.Id == "UT0130");
        }

        // ---- SC-P2-03-1 / UT0131a: weight=0.5f -> no UT0131 ---------------------

        [Fact]
        public void WeightInRange_ProducesNoDiagnostics()
        {
            const string source = @"
using Fdp.Toolkit.Utility;
namespace My.Decisions
{
    [UtilityDecision(""guid-4"", ""GoodWeight"", DecisionKind.ThreatRanking)]
    public class GoodWeightDecision
    {
        public static void Build(IUtilityDecisionBuilder b)
        {
            b.CandidateOption(ScoringMode.WeightedProduct, o =>
                o.Consider(In.AmmoFraction(), 0.5f, Curve.Linear));
        }
    }
}
";
            var diagnostics = RunAnalyzer(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "UT0131");
        }

        // ---- SC-P2-03-1 / UT0131b: weight=1.5f -> UT0131 -----------------------

        [Fact]
        public void WeightOutOfRange_EmitsUT0131()
        {
            const string source = @"
using Fdp.Toolkit.Utility;
namespace My.Decisions
{
    [UtilityDecision(""guid-5"", ""BadWeight"", DecisionKind.ThreatRanking)]
    public class BadWeightDecision
    {
        public static void Build(IUtilityDecisionBuilder b)
        {
            b.CandidateOption(ScoringMode.WeightedProduct, o =>
                o.Consider(In.AmmoFraction(), 1.5f, Curve.Linear));
        }
    }
}
";
            var diagnostics = RunAnalyzer(source);
            Assert.Contains(diagnostics, d => d.Id == "UT0131");
        }

        // ---- SC-P2-03-3: In.NonExistentInput(...) -> UT0120 ---------------------

        [Fact]
        public void UnknownInput_EmitsUT0120()
        {
            const string source = @"
using Fdp.Toolkit.Utility;
namespace My.Decisions
{
    [UtilityDecision(""guid-6"", ""BadInput"", DecisionKind.ThreatRanking)]
    public class BadInputDecision
    {
        public static void Build(IUtilityDecisionBuilder b)
        {
            b.CandidateOption(ScoringMode.WeightedProduct, o =>
                o.Consider(In.NonExistentInput(), 0.5f, Curve.Linear));
        }
    }
    public static partial class In
    {
        public static Fdp.Toolkit.Utility.InputRef NonExistentInput() => default;
    }
}
";
            var diagnostics = RunAnalyzer(source);
            Assert.Contains(diagnostics, d => d.Id == "UT0120");
        }

        // ---- SC-P2-03-3: In.AmmoFraction(...) (catalog) -> no UT0120 ------------

        [Fact]
        public void KnownInput_ProducesNoDiagnostics()
        {
            const string source = @"
using Fdp.Toolkit.Utility;
namespace My.Decisions
{
    [UtilityDecision(""guid-7"", ""GoodInput"", DecisionKind.ThreatRanking)]
    public class GoodInputDecision
    {
        public static void Build(IUtilityDecisionBuilder b)
        {
            b.CandidateOption(ScoringMode.WeightedProduct, o =>
                o.Consider(In.AmmoFraction(), 0.5f, Curve.Linear));
        }
    }
}
";
            var diagnostics = RunAnalyzer(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "UT0120");
        }

        // ---- SC-P2-03-1 / UT0143: PostureSelect + empty Build -> UT0143 ---------

        [Fact]
        public void PostureSelectZeroOptions_EmitsUT0143()
        {
            const string source = @"
using Fdp.Toolkit.Utility;
namespace My.Decisions
{
    [UtilityDecision(""guid-8"", ""EmptyPosture"", DecisionKind.PostureSelect)]
    public class EmptyPostureDecision
    {
        public static void Build(IUtilityDecisionBuilder b)
        {
            // no Option or CandidateOption calls
        }
    }
}
";
            var diagnostics = RunAnalyzer(source);
            Assert.Contains(diagnostics, d => d.Id == "UT0143");
        }

        // ---- SC-P2-03-3 cross-assembly: input defined in upstream assembly ------
        // Verifies the analyzer finds [UtilityInput] methods from referenced assemblies
        // and does NOT fire UT0120 for them.

        [Fact]
        public void CrossAssembly_KnownInput_ProducesNoDiagnostics()
        {
            // Upstream assembly: defines a [UtilityInput] method.
            const string upstreamSource = @"
using System;
namespace Upstream.Inputs
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UtilityInputAttribute : Attribute
    {
        public string Name { get; }
        public UtilityInputAttribute(string name) { Name = name; }
    }
    public readonly struct InputRef { }
    public static class In
    {
        [UtilityInputAttribute(""UpstreamHealth"")]
        public static InputRef UpstreamHealth() => default;
    }
}
";
            var upstreamTree = CSharpSyntaxTree.ParseText(upstreamSource);
            var baseRefs = GetBaseReferences();

            var upstreamCompilation = CSharpCompilation.Create(
                "UpstreamAssembly",
                syntaxTrees: new[] { upstreamTree },
                references: baseRefs,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Disable));

            var upstreamRef = upstreamCompilation.ToMetadataReference();

            // Downstream assembly: references upstream and uses In.UpstreamHealth.
            const string downstreamSource = @"
using System;
namespace My.Decisions
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class UtilityDecisionAttribute : Attribute
    {
        public string AssetId { get; }
        public string DisplayName { get; }
        public int Kind { get; }
        public UtilityDecisionAttribute(string assetId, string displayName, int kind) { }
    }
    public interface IUtilityOptionBuilder
    {
        IUtilityOptionBuilder Consider(Upstream.Inputs.InputRef input, float weight, object curve);
    }
    public interface IUtilityDecisionBuilder
    {
        IUtilityDecisionBuilder CandidateOption(int mode, System.Action<IUtilityOptionBuilder> cfg);
    }
    [UtilityDecision(""guid-9"", ""Upstream"", 0)]
    public class UpstreamConsumer
    {
        public static void Build(IUtilityDecisionBuilder b)
        {
            b.CandidateOption(0, o => o.Consider(Upstream.Inputs.In.UpstreamHealth(), 0.5f, null));
        }
    }
}
";
            var downstreamTree = CSharpSyntaxTree.ParseText(downstreamSource);
            var downstreamRefs = new System.Collections.Generic.List<MetadataReference>(baseRefs)
            {
                upstreamRef
            };

            var downstreamCompilation = CSharpCompilation.Create(
                "DownstreamAssembly",
                syntaxTrees: new[] { downstreamTree },
                references: downstreamRefs,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Disable));

            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
                new UtilityAuthoringAnalyzer());

            var diagnostics = downstreamCompilation
                .WithAnalyzers(analyzers)
                .GetAnalyzerDiagnosticsAsync().Result;

            // The upstream [UtilityInput("UpstreamHealth")] must be found in the catalog,
            // so UT0120 must NOT fire for In.UpstreamHealth().
            Assert.DoesNotContain(diagnostics, d => d.Id == "UT0120");
        }
    }
}