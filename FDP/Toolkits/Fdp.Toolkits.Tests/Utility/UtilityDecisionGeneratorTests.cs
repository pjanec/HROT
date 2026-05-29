using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Behavior.Analyzers;
using Xunit;

namespace Fdp.Toolkit.Tests.Utility
{
    /// <summary>
    /// Roslyn compilation tests for UtilityDecisionGenerator.
    /// Covers success conditions SC-P2-02-1 through SC-P2-02-6.
    /// </summary>
    public class UtilityDecisionGeneratorTests
    {
        // Minimal stubs that mirror the production FQNs required by the generator.
        private const string CommonStubs = @"
using System;

namespace Fdp.Toolkit.Utility
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class UtilityDecisionAttribute : Attribute
    {
        public string AssetId { get; }
        public string DisplayName { get; }
        public DecisionKind Kind { get; }
        public string Category { get; }
        public float HysteresisBonus { get; }
        public UtilityDecisionAttribute(string assetId, string displayName, DecisionKind kind,
            string category = """", float hysteresisBonus = 0f)
        {
            AssetId = assetId; DisplayName = displayName; Kind = kind;
            Category = category; HysteresisBonus = hysteresisBonus;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class UtilityRegistrarAttribute : Attribute { }

    public enum DecisionKind : byte { ThreatRanking = 0, PostureSelect = 1, WeaponSelection = 2 }

    public interface IUtilityDecisionDefinition { }

    public interface IUtilityDecisionBuilder
    {
        IUtilityDecisionBuilder Option(ushort tag, ScoringMode mode, Action<IOptionBuilder> build);
        IUtilityDecisionBuilder CandidateOption(ScoringMode mode, Action<IOptionBuilder> build);
    }

    public interface IOptionBuilder
    {
        IOptionBuilder Consider(float v, float weight, Curve curve);
    }

    public enum ScoringMode : byte { WeightedProduct = 0, WeightedSum = 1 }
    public enum Curve : byte { Linear = 0, InverseLinear = 1, Step = 2, Logistic = 3, Bell = 4, Threshold = 5 }

    public sealed class UtilityDecisionBuilder : IUtilityDecisionBuilder
    {
        public UtilityDecisionDef Build(UtilityDecisionAttribute attr) => default;
        public static int ComputeId(string assetId) => 0;
        public IUtilityDecisionBuilder Option(ushort tag, ScoringMode mode, Action<IOptionBuilder> build) => this;
        public IUtilityDecisionBuilder CandidateOption(ScoringMode mode, Action<IOptionBuilder> build) => this;
    }

    public struct UtilityDecisionDef { }

    public sealed class UtilityRegistry
    {
        public void Register(int id, UtilityDecisionDef def, float hysteresisBonus = 0f) { }
        internal void MergeFrom(UtilityRegistry other) { }
    }

    public readonly struct UtilityDecisionManifestEntry
    {
        public int BlueprintId { get; }
        public string DisplayName { get; }
        public bool ManifestIsFull { get; }
        public int OptionCount { get; }
        public int ConsiderCount { get; }
        public UtilityDecisionManifestEntry(int blueprintId, string displayName,
            bool manifestIsFull, int optionCount, int considerCount)
        {
            BlueprintId = blueprintId; DisplayName = displayName;
            ManifestIsFull = manifestIsFull; OptionCount = optionCount; ConsiderCount = considerCount;
        }
    }
}
";

        private static (GeneratorDriverRunResult result, Compilation outputCompilation) RunGenerator(
            string source)
        {
            var runtimeDir      = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var netstandardPath = Path.Combine(runtimeDir, "netstandard.dll");

            MetadataReference[] references = File.Exists(netstandardPath)
                ? new[]
                  {
                      MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                      MetadataReference.CreateFromFile(netstandardPath)
                  }
                : new[]
                  {
                      MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                      MetadataReference.CreateFromFile(
                          typeof(System.Runtime.GCSettings).Assembly.Location)
                  };

            var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(CommonStubs),
                CSharpSyntaxTree.ParseText(source)
            };

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new UtilityDecisionGenerator();
            var driver    = CSharpGeneratorDriver.Create(generator);
            driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
                compilation, out var outputCompilation, out _);

            return (driver.GetRunResult(), outputCompilation);
        }

        // Reference implementation of FNV-1a-32 matching the generator.
        // basis=2166136261, prime=16777619, hash ^= (uint)c for each char.
        private static uint Fnv1a32Ref(string s)
        {
            uint hash = 2166136261u;
            foreach (char c in s)
            {
                hash ^= (uint)c;
                hash *= 16777619u;
            }
            return hash;
        }

        // ---- SC-P2-02-1 --------------------------------------------------------

        /// <summary>
        /// SC-P2-02-1: One [UtilityDecision] class produces exactly two generated sources
        /// (UtilityDecisionCatalog.g.cs and UtilityDecisionIds.g.cs), the catalog contains
        /// RegisterAll(out, and the IDs file contains const int Id.
        /// Output compilation has zero errors.
        /// </summary>
        [Fact]
        public void DecisionClass_EmitsCatalogAndIds()
        {
            const string source = @"
namespace TestNs
{
    [global::Fdp.Toolkit.Utility.UtilityDecision(
        ""asset-001"", ""Test Decision"", global::Fdp.Toolkit.Utility.DecisionKind.ThreatRanking)]
    public sealed partial class TestDecision : global::Fdp.Toolkit.Utility.IUtilityDecisionDefinition
    {
        public static void Build(global::Fdp.Toolkit.Utility.IUtilityDecisionBuilder b) { }
    }
}
";
            var (result, outputCompilation) = RunGenerator(source);

            Assert.Single(result.Results);
            var generatedSources = result.Results[0].GeneratedSources;
            Assert.Equal(2, generatedSources.Length);

            string catalogSource = generatedSources
                .Single(s => s.HintName == "UtilityDecisionCatalog.g.cs").SourceText.ToString();
            string idsSource = generatedSources
                .Single(s => s.HintName == "UtilityDecisionIds.g.cs").SourceText.ToString();

            Assert.Contains("RegisterAll(out ", catalogSource);
            Assert.Contains("const int Id", idsSource);

            var errors = outputCompilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.Empty(errors);
        }

        // ---- SC-P2-02-2 --------------------------------------------------------

        /// <summary>
        /// SC-P2-02-2: The hex literal emitted for Id and registry.Register matches
        /// the reference FNV-1a-32 hash of the AssetId string.
        /// </summary>
        [Fact]
        public void BlueprintId_MatchesFnv1a32OfAssetId()
        {
            const string assetId = "my-unique-asset-id";
            string expectedHex = "0x" + Fnv1a32Ref(assetId).ToString("X8");

            string source = @"
namespace TestNs
{
    [global::Fdp.Toolkit.Utility.UtilityDecision(
        """ + assetId + @""", ""My Decision"", global::Fdp.Toolkit.Utility.DecisionKind.PostureSelect)]
    public sealed partial class MyDecision : global::Fdp.Toolkit.Utility.IUtilityDecisionDefinition
    {
        public static void Build(global::Fdp.Toolkit.Utility.IUtilityDecisionBuilder b) { }
    }
}
";
            var (result, _) = RunGenerator(source);
            var sources      = result.Results[0].GeneratedSources;

            string idsSource = sources.Single(s => s.HintName == "UtilityDecisionIds.g.cs").SourceText.ToString();
            Assert.Contains(expectedHex, idsSource);

            string catalogSource = sources.Single(s => s.HintName == "UtilityDecisionCatalog.g.cs").SourceText.ToString();
            Assert.Contains(expectedHex, catalogSource);
        }

        // ---- SC-P2-02-3 --------------------------------------------------------

        /// <summary>
        /// SC-P2-02-3: A class with [UtilityDecision] that does NOT implement
        /// IUtilityDecisionDefinition triggers diagnostic UT0140 and no catalog is emitted.
        /// </summary>
        [Fact]
        public void MissingInterface_EmitsUT0140()
        {
            const string source = @"
namespace TestNs
{
    [global::Fdp.Toolkit.Utility.UtilityDecision(
        ""bad-001"", ""Bad"", global::Fdp.Toolkit.Utility.DecisionKind.ThreatRanking)]
    public sealed partial class BadDecision
    {
        public static void Build(global::Fdp.Toolkit.Utility.IUtilityDecisionBuilder b) { }
    }
}
";
            var (result, _) = RunGenerator(source);

            var diagnostics = result.Results[0].Diagnostics;
            Assert.Contains(diagnostics, d => d.Id == "UT0140");

            // No output files when all decisions are invalid
            Assert.Empty(result.Results[0].GeneratedSources);
        }

        // ---- SC-P2-02-4 --------------------------------------------------------

        /// <summary>
        /// SC-P2-02-4: A class with [UtilityDecision] and the interface but without a
        /// static Build(IUtilityDecisionBuilder) method triggers diagnostic UT0141.
        /// </summary>
        [Fact]
        public void MissingBuildMethod_EmitsUT0141()
        {
            const string source = @"
namespace TestNs
{
    [global::Fdp.Toolkit.Utility.UtilityDecision(
        ""bad-002"", ""NoBuild"", global::Fdp.Toolkit.Utility.DecisionKind.ThreatRanking)]
    public sealed partial class NoBuildDecision : global::Fdp.Toolkit.Utility.IUtilityDecisionDefinition
    {
        // no Build method
    }
}
";
            var (result, _) = RunGenerator(source);

            var diagnostics = result.Results[0].Diagnostics;
            Assert.Contains(diagnostics, d => d.Id == "UT0141");

            Assert.Empty(result.Results[0].GeneratedSources);
        }

        // ---- SC-P2-02-5 --------------------------------------------------------

        /// <summary>
        /// SC-P2-02-5: Two classes sharing the same AssetId trigger diagnostic UT0150
        /// on the second occurrence; only the first is registered.
        /// </summary>
        [Fact]
        public void DuplicateAssetId_EmitsUT0150()
        {
            const string source = @"
namespace TestNs
{
    [global::Fdp.Toolkit.Utility.UtilityDecision(
        ""dup-001"", ""First"", global::Fdp.Toolkit.Utility.DecisionKind.ThreatRanking)]
    public sealed partial class FirstDecision : global::Fdp.Toolkit.Utility.IUtilityDecisionDefinition
    {
        public static void Build(global::Fdp.Toolkit.Utility.IUtilityDecisionBuilder b) { }
    }

    [global::Fdp.Toolkit.Utility.UtilityDecision(
        ""dup-001"", ""Second"", global::Fdp.Toolkit.Utility.DecisionKind.PostureSelect)]
    public sealed partial class SecondDecision : global::Fdp.Toolkit.Utility.IUtilityDecisionDefinition
    {
        public static void Build(global::Fdp.Toolkit.Utility.IUtilityDecisionBuilder b) { }
    }
}
";
            var (result, _) = RunGenerator(source);

            var diagnostics = result.Results[0].Diagnostics;
            Assert.Contains(diagnostics, d => d.Id == "UT0150");

            // Catalog still emitted for the first (non-duplicate) decision
            Assert.NotEmpty(result.Results[0].GeneratedSources);
            string catalogSource = result.Results[0].GeneratedSources
                .Single(s => s.HintName == "UtilityDecisionCatalog.g.cs").SourceText.ToString();
            Assert.Contains("FirstDecision", catalogSource);
            Assert.DoesNotContain("SecondDecision", catalogSource);
        }

        // ---- SC-P2-02-4 (design) -----------------------------------------------

        /// <summary>
        /// SC-P2-02-4: A Build body that contains a foreach loop produces a partial
        /// manifest entry (ManifestIsFull = false, counts are 0).
        /// </summary>
        [Fact]
        public void ForeachBuild_EmitsPartialManifest()
        {
            const string source = @"
namespace TestNs
{
    [global::Fdp.Toolkit.Utility.UtilityDecision(
        ""loop-001"", ""LoopDecision"", global::Fdp.Toolkit.Utility.DecisionKind.ThreatRanking)]
    public sealed partial class LoopDecision : global::Fdp.Toolkit.Utility.IUtilityDecisionDefinition
    {
        private static readonly ushort[] s_tags = new ushort[] { 0, 1, 2 };
        public static void Build(global::Fdp.Toolkit.Utility.IUtilityDecisionBuilder b)
        {
            foreach (var tag in s_tags)
                b.Option(tag, global::Fdp.Toolkit.Utility.ScoringMode.WeightedProduct, o => { });
        }
    }
}
";
            var (result, _) = RunGenerator(source);
            Assert.Empty(result.Results[0].Diagnostics);

            string catalogSource = result.Results[0].GeneratedSources
                .Single(s => s.HintName == "UtilityDecisionCatalog.g.cs").SourceText.ToString();

            // Manifest entry must have ManifestIsFull = false and both counts = 0
            Assert.Contains("false,", catalogSource);
            Assert.Contains(", 0, 0)", catalogSource);
        }

        // ---- SC-P2-02-6 --------------------------------------------------------

        /// <summary>
        /// SC-P2-02-6: The Manifest array entry correctly counts Option/CandidateOption calls
        /// and Consider calls in a simple, static Build method body.
        /// </summary>
        [Fact]
        public void ManifestEntry_CountsOptionsAndConsiders()
        {
            const string source = @"
namespace TestNs
{
    [global::Fdp.Toolkit.Utility.UtilityDecision(
        ""count-001"", ""Counter"", global::Fdp.Toolkit.Utility.DecisionKind.ThreatRanking)]
    public sealed partial class CountDecision : global::Fdp.Toolkit.Utility.IUtilityDecisionDefinition
    {
        public static void Build(global::Fdp.Toolkit.Utility.IUtilityDecisionBuilder b) => b
            .CandidateOption(global::Fdp.Toolkit.Utility.ScoringMode.WeightedProduct, o => o
                .Consider(0f, 1.0f, global::Fdp.Toolkit.Utility.Curve.Linear)
                .Consider(0f, 0.8f, global::Fdp.Toolkit.Utility.Curve.Step));
    }
}
";
            var (result, _) = RunGenerator(source);
            Assert.Empty(result.Results[0].Diagnostics);

            string catalogSource = result.Results[0].GeneratedSources
                .Single(s => s.HintName == "UtilityDecisionCatalog.g.cs").SourceText.ToString();

            // Manifest: ManifestIsFull = true, OptionCount = 1, ConsiderCount = 2
            Assert.Contains("true,", catalogSource);
            Assert.Contains(", 1,", catalogSource);
            Assert.Contains(", 2),", catalogSource);
        }
    }
}
