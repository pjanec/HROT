using System;
using System.Collections.Immutable;
using System.Linq;
using Fdp.Toolkit.Behavior.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for EqsTemplateGenerator (TASK-EQS-020).
    /// Follow the same in-memory Roslyn test pattern as GizmoRegistrarGeneratorTests.
    /// </summary>
    public sealed class EqsTemplateGeneratorTests
    {
        // ---- Stubs required for in-memory compilation ----

        private const string CommonStubs = @"
namespace Fdp.Toolkit.Spatial.Eqs
{
    public sealed class EqsTemplateAttribute : System.Attribute
    {
        public EqsTemplateAttribute(string assetId) { }
    }
    public struct EqsQueryTemplate { public ulong ComputeStructureHash() => 0; }
    public interface IEqsTemplateBuilder { }
    public sealed class EqsTemplateBuilder : IEqsTemplateBuilder { }
}
namespace Fdp.Toolkit.Blueprints
{
    public sealed class BlueprintRegistryStaging
    {
        public void Add(int id, BlueprintDefinition def) { }
    }
    public struct BlueprintDefinition
    {
        public string Name;
        public BlueprintDispatchKind Kind;
        public ulong StructureHash;
        public int StateSize;
    }
    public enum BlueprintDispatchKind { Library }
}
namespace Fdp.Toolkit.Blueprints.Attributes
{
    public sealed class BlueprintRegistrarAttribute : System.Attribute { }
}
";

        /// <summary>
        /// Runs <see cref="EqsTemplateGenerator"/> over the supplied source and stubs.
        /// Returns generator diagnostics and combined generated text.
        /// </summary>
        private static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource)
            RunGenerator(string userSource)
        {
            string combined = userSource + "\n" + CommonStubs;
            var syntaxTree = CSharpSyntaxTree.ParseText(combined);

            var platformPaths = ((string)(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty))
                .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

            var references = platformPaths
                .Where(p =>
                {
                    var name = System.IO.Path.GetFileName(p);
                    return name == "System.Private.CoreLib.dll"
                        || name == "System.Runtime.dll"
                        || name == "System.Collections.dll";
                })
                .Select(p => MetadataReference.CreateFromFile(p))
                .Cast<MetadataReference>()
                .ToList();

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Disable));

            var generator = new EqsTemplateGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation, out var outputCompilation, out _);

            var genDiags = driver.GetRunResult().Diagnostics;

            var originalTrees = new System.Collections.Generic.HashSet<SyntaxTree>(
                compilation.SyntaxTrees);
            var generatedSource = string.Concat(
                outputCompilation.SyntaxTrees
                    .Where(t => !originalTrees.Contains(t))
                    .Select(t => t.GetText().ToString()));

            return (genDiags, generatedSource);
        }

        /// <summary>
        /// Computes the FNV-1a 32-bit hash of <paramref name="s"/> using the same algorithm
        /// as the generator, so test assertions are self-consistent.
        /// </summary>
        private static int ComputeFnv1a32(string s)
        {
            uint h = 2166136261u;
            unchecked
            {
                foreach (char c in s) { h ^= (uint)c; h *= 16777619u; }
            }
            return (int)h;
        }

        // T-EGN1: generator emits correct BlueprintId for the known FindCoverFromTarget GUID.
        [Fact]
        public void EqsTemplateGenerator_EmitsCorrectBlueprintId_ForKnownAssetId()
        {
            const string assetId = "f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d";
            const string source = @"
using Fdp.Toolkit.Spatial.Eqs;
namespace My.Templates
{
    [EqsTemplate(""f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d"")]
    public static class MyTemplate
    {
        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => default;
    }
}
";
            var (_, generated) = RunGenerator(source);

            int expectedId = ComputeFnv1a32(assetId);

            Assert.Contains("staging.Add(", generated);
            Assert.Contains(expectedId.ToString(), generated);
            Assert.Contains("EqsRegistrar_", generated);
        }

        // T-EGN2: no output emitted when no [EqsTemplate] attribute is present.
        [Fact]
        public void EqsTemplateGenerator_NoOutput_WhenNoEqsTemplateAttribute()
        {
            const string source = @"
namespace My.Templates
{
    public static class PlainClass
    {
        public static void DoNothing() { }
    }
}
";
            var (_, generated) = RunGenerator(source);

            Assert.Equal(string.Empty, generated);
        }

        // T-EGN3: generated source has correct structure (Register method, [BlueprintRegistrar], ComputeStructureHash).
        [Fact]
        public void EqsTemplateGenerator_EmitsRegisterMethod_WithCorrectStructure()
        {
            const string source = @"
using Fdp.Toolkit.Spatial.Eqs;
namespace My.Templates
{
    [EqsTemplate(""f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d"")]
    public static class MyTemplate
    {
        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => default;
    }
}
";
            var (_, generated) = RunGenerator(source);

            Assert.Contains("public static void Register(BlueprintRegistryStaging staging)", generated);
            Assert.Contains("[BlueprintRegistrar]", generated);
            Assert.Contains(".ComputeStructureHash()", generated);
        }
    }
}
