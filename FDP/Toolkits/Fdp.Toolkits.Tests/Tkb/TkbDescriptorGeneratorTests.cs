using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;
using System.Linq;
using Fdp.Toolkit.SourceGen;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests
{
    /// <summary>
    /// Roslyn compilation tests for TkbDescriptorGenerator.
    /// No shared state, no [Collection] isolation needed.
    /// </summary>
    public class TkbDescriptorGeneratorTests
    {
        // ── Helper ────────────────────────────────────────────────────────────

        private static (GeneratorDriverRunResult result, Compilation outputCompilation) RunGenerator(
            string source)
        {
            // Include the TkbDescriptorAttribute stub (same FQN as production).
            const string attributeStub = @"
namespace Fdp.Toolkit.Tkb.Attributes
{
    [System.AttributeUsage(
        System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public sealed class TkbDescriptorAttribute : System.Attribute
    {
        public TkbDescriptorAttribute(string hierarchicalName) { }
    }
}";

            var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(attributeStub),
                CSharpSyntaxTree.ParseText(source)
            };

            // Try to locate netstandard.dll alongside mscorlib/System.Private.CoreLib.
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var netstandardPath = Path.Combine(runtimeDir, "netstandard.dll");

            var references = File.Exists(netstandardPath)
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

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new TkbDescriptorGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);
            driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
                compilation, out var outputCompilation, out _);

            return (driver.GetRunResult(), outputCompilation);
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public void Generator_SingleType_EmitsRegisterParserCall()
        {
            const string source = @"
using Fdp.Toolkit.Tkb.Attributes;
namespace MyAssembly
{
    [TkbDescriptor(""Test.Foo"")]
    public class FooDto { }
}";
            var (result, _) = RunGenerator(source);

            Assert.Equal(1, result.GeneratedTrees.Length);
            string text = result.GeneratedTrees[0].GetText().ToString();
            Assert.Contains("RegisterParser", text);
            Assert.Contains("\"Test.Foo\"", text);
            Assert.Contains("MyAssembly.FooDto", text);
        }

        [Fact]
        public void Generator_SingleType_EmitsModuleInitializer()
        {
            const string source = @"
using Fdp.Toolkit.Tkb.Attributes;
namespace MyAssembly
{
    [TkbDescriptor(""Test.Bar"")]
    public class BarDto { }
}";
            var (result, _) = RunGenerator(source);

            Assert.Equal(1, result.GeneratedTrees.Length);
            string text = result.GeneratedTrees[0].GetText().ToString();
            Assert.Contains("ModuleInitializer", text);
            Assert.Contains("internal static void Register", text);
        }

        [Fact]
        public void Generator_NoDescriptorTypes_EmitsNoFile()
        {
            const string source = @"
namespace MyAssembly
{
    public class PlainClass { }
}";
            var (result, _) = RunGenerator(source);

            Assert.Equal(0, result.GeneratedTrees.Length);
        }

        [Fact]
        public void Generator_DuplicateHierarchicalName_EmitsWarning()
        {
            const string source = @"
using Fdp.Toolkit.Tkb.Attributes;
namespace MyAssembly
{
    [TkbDescriptor(""Dup.Name"")]
    public class FirstDto { }

    [TkbDescriptor(""Dup.Name"")]
    public class SecondDto { }
}";
            var (result, _) = RunGenerator(source);

            var tkbDiag = result.Diagnostics.FirstOrDefault(d => d.Id == "TKB001");
            Assert.NotNull(tkbDiag);
            Assert.Equal(DiagnosticSeverity.Warning, tkbDiag!.Severity);
        }

        [Fact]
        public void Generator_MultipleTypes_AllRegistered()
        {
            const string source = @"
using Fdp.Toolkit.Tkb.Attributes;
namespace MyAssembly
{
    [TkbDescriptor(""Alpha.One"")]
    public class AlphaDto { }

    [TkbDescriptor(""Beta.Two"")]
    public class BetaDto { }

    [TkbDescriptor(""Gamma.Three"")]
    public class GammaDto { }
}";
            var (result, _) = RunGenerator(source);

            Assert.Equal(1, result.GeneratedTrees.Length);
            string text = result.GeneratedTrees[0].GetText().ToString();
            Assert.Contains("\"Alpha.One\"", text);
            Assert.Contains("\"Beta.Two\"", text);
            Assert.Contains("\"Gamma.Three\"", text);
        }
    }
}
