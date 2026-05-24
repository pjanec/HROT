using System;
using System.Collections.Immutable;
using System.Linq;
using Fdp.Toolkit.Behavior.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for EqsTemplatePurityAnalyzer (TASK-EQS-020).
    /// Uses in-memory Roslyn compilation and runs the analyzer via WithAnalyzers.
    /// </summary>
    public sealed class EqsTemplatePurityAnalyzerTests
    {
        // ---- Stubs used for in-memory compilation ----

        private const string CommonStubs = @"
namespace Fdp.Toolkit.Spatial.Eqs
{
    public sealed class EqsTemplateAttribute : System.Attribute
    {
        public EqsTemplateAttribute(string assetId) { }
    }
    public struct EqsQueryTemplate { }
    public interface IEqsTemplateBuilder { }
}
";

        private static ImmutableArray<Diagnostic> RunAnalyzer(string userSource)
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

            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
                new EqsTemplatePurityAnalyzer());

            var withAnalyzers = compilation.WithAnalyzers(analyzers);
            return withAnalyzers.GetAnalyzerDiagnosticsAsync().Result;
        }

        // T-EPA1: non-static Build(IEqsTemplateBuilder) must trigger EQS_001.
        [Fact]
        public void PurityAnalyzer_FlagsNonStaticBuild()
        {
            const string source = @"
using Fdp.Toolkit.Spatial.Eqs;
namespace My.Templates
{
    [EqsTemplate(""some-guid"")]
    public class MyTemplate
    {
        public EqsQueryTemplate Build(IEqsTemplateBuilder b) => default;
    }
}
";
            var diagnostics = RunAnalyzer(source);

            Assert.Contains(diagnostics, d => d.Id == "EQS_001");
        }

        // T-EPA2: static Build(IEqsTemplateBuilder) must not trigger EQS_001.
        [Fact]
        public void PurityAnalyzer_AcceptsStaticBuildWithCorrectParam()
        {
            const string source = @"
using Fdp.Toolkit.Spatial.Eqs;
namespace My.Templates
{
    [EqsTemplate(""some-guid"")]
    public class MyTemplate
    {
        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => default;
    }
}
";
            var diagnostics = RunAnalyzer(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "EQS_001");
        }

        // T-EPA3: Build(int x) with wrong parameter type must trigger EQS_001.
        [Fact]
        public void PurityAnalyzer_FlagsBuildWithWrongParam()
        {
            const string source = @"
using Fdp.Toolkit.Spatial.Eqs;
namespace My.Templates
{
    [EqsTemplate(""some-guid"")]
    public class MyTemplate
    {
        public static EqsQueryTemplate Build(int x) => default;
    }
}
";
            var diagnostics = RunAnalyzer(source);

            Assert.Contains(diagnostics, d => d.Id == "EQS_001");
        }
    }
}
