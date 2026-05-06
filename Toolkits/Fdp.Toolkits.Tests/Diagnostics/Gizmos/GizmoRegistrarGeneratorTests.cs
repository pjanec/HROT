using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Fdp.Toolkit.Diagnostics.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // ========================================================================
    // SC-GZ024: Roslyn source generator tests for GizmoRegistrarGenerator.
    // ========================================================================

    public sealed class GizmoRegistrarGeneratorTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        // Minimal stubs; no [AttributeUsage] to avoid resolution ambiguity in test compilation.
        private const string CommonStubs = @"
namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public sealed class GizmoProjectorAttribute : System.Attribute
    {
        public System.Type[] RequiredComponents;
        public GizmoProjectorAttribute(params System.Type[] t) { RequiredComponents = t; }
    }
    public interface IStatelessGizmo { void Draw(); }
    public class GizmoRegistry { }
    public class StatelessGizmoRegistry
    {
        public void Register(IStatelessGizmo g, System.Type[] t) { }
    }
}
namespace Fdp.Toolkit.Diagnostics.Gizmos.Settings
{
    public class GizmoSettingsRegistry { }
}
";

        /// <summary>
        /// Creates a compilation and runs <see cref="GizmoRegistrarGenerator"/> on it.
        /// Returns the generator diagnostics and the combined generated text.
        /// </summary>
        private static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource)
            RunGenerator(string userSource)
        {
            // User source first so its using-directives precede all namespace declarations.
            string combined = userSource + "\n" + CommonStubs;
            var syntaxTree = CSharpSyntaxTree.ParseText(combined);

            // Use the trusted platform assemblies list to find core .NET assemblies.
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

            var generator = new GizmoRegistrarGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation, out var outputCompilation, out _);

            var genDiags = driver.GetRunResult().Diagnostics;

            // Generated source: trees added by the generator (not in the original compilation).
            var originalTrees = new HashSet<SyntaxTree>(compilation.SyntaxTrees);
            var generatedSource = string.Concat(
                outputCompilation.SyntaxTrees
                    .Where(t => !originalTrees.Contains(t))
                    .Select(t => t.GetText().ToString()));

            return (genDiags, generatedSource);
        }

        // Returns compilation error messages from a RunGenerator call for debugging.
        private static List<string> GetCompilationErrors(string userSource)
        {
            // User source first so its using-directives precede all namespace declarations.
            string combined = userSource + "\n" + CommonStubs;
            var syntaxTree = CSharpSyntaxTree.ParseText(combined);
            var platformPaths = ((string)(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty))
                .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var references = platformPaths
                .Where(p => { var name = System.IO.Path.GetFileName(p); return name == "System.Private.CoreLib.dll" || name == "System.Runtime.dll" || name == "System.Collections.dll"; })
                .Select(p => MetadataReference.CreateFromFile(p))
                .Cast<MetadataReference>()
                .ToList();
            var compilation = CSharpCompilation.Create(
                "TestAssembly", syntaxTrees: new[] { syntaxTree }, references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable));
            return compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();
        }

        // ── SC-GZ024-1 ────────────────────────────────────────────────────────

        // Compile-diagnostic guard: ensures in-memory stubs resolve without errors.
        [Fact]
        public void GizmoRegistrarGeneratorTests_StubsCompileClean()
        {
            const string source = @"
using Fdp.Toolkit.Diagnostics.Gizmos;
namespace My.Gizmos { public class X : IStatelessGizmo { public void Draw() { } } }
";
            var errors = GetCompilationErrors(source);
            Assert.True(errors.Count == 0, "Stubs have compilation errors:\n" + string.Join("\n", errors));
        }

        [Fact]
        public void SC_GZ024_1_StatelessGizmo_AppearsAs_StatelessRegistry_Register()
        {
            // A class with [GizmoProjector] implementing IStatelessGizmo must appear
            // as statelessRegistry.Register(...) in the generated output.
            const string source = @"
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace My.Gizmos
{
    [GizmoProjector]
    public sealed class MyRotationGizmo : IStatelessGizmo
    {
        public void Draw() { }
    }
}
";
            var (diagnostics, generated) = RunGenerator(source);

            // Pre-check: compilation must succeed for the generator to work.
            var compilationErrors = GetCompilationErrors(source);
            Assert.True(compilationErrors.Count == 0,
                "Test compilation has errors (generator will not run):\n" + string.Join("\n", compilationErrors));

            // No FDP_002 warnings.
            Assert.Empty(diagnostics.Where(d => d.Id == "FDP_002"));

            // Generated source must contain statelessRegistry.Register.
            Assert.Contains("statelessRegistry.Register", generated);

            // Must reference MyRotationGizmo.
            Assert.Contains("MyRotationGizmo", generated);
        }

        // ── SC-GZ024-2 ────────────────────────────────────────────────────────

        [Fact]
        public void SC_GZ024_2_IStatelessGizmo_WithConstructorSettings_PassesSettingsArg()
        {
            // A class that takes GizmoSettingsRegistry in its constructor must use
            // `new T(settings)` in the generated call.
            const string source = @"
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace My.Gizmos
{
    [GizmoProjector]
    public sealed class MyGizmoWithSettings : IStatelessGizmo
    {
        public MyGizmoWithSettings(GizmoSettingsRegistry settings) { }
        public void Draw() { }
    }
}
";
            var (diagnostics, generated) = RunGenerator(source);

            Assert.Empty(diagnostics.Where(d => d.Id == "FDP_002"));

            // Constructor arg must be `settings`.
            Assert.Contains("MyGizmoWithSettings(settings)", generated);
        }

        // ── SC-GZ024-5 ────────────────────────────────────────────────────────

        [Fact]
        public void SC_GZ024_5_ClassWithAttribute_ButNotStateless_TriggersFDP002()
        {
            // A class with [GizmoProjector] that does NOT implement IStatelessGizmo
            // must trigger the FDP_002 warning and NOT appear in the output.
            const string source = @"
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace My.Gizmos
{
    [GizmoProjector]
    public sealed class BadGizmo
    {
        // Does not implement IStatelessGizmo.
    }
}
";
            var (diagnostics, generated) = RunGenerator(source);

            // FDP_002 must be reported.
            var fdp002 = diagnostics.Where(d => d.Id == "FDP_002").ToList();
            Assert.NotEmpty(fdp002);
            Assert.Contains(fdp002, d => d.GetMessage().Contains("BadGizmo"));

            // BadGizmo must NOT appear in generated statelessRegistry.Register calls.
            Assert.DoesNotContain("BadGizmo", generated);
        }
    }
}
