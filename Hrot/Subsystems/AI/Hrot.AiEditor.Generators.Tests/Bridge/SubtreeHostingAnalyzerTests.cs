using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fdp.Toolkit.Behavior.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// ⭐⭐ <b><c>CE-367</c> — the authoring warning that is the WHOLE mitigation for the ordinal site
/// fallback.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §33.6.
///
/// <para>⛔⛔ <b>Why these rails exist at all:</b> <c>BEH010</c> has <b>no opportunity to fire
/// anywhere in the solution today</b> — no production tree hosts a sub-tree yet — so a full build
/// proves nothing about it. An analyzer nobody can see fire is indistinguishable from one that does
/// not work, and this one is the only thing standing between an author and a silently
/// ordinal-keyed hosting site.</para>
///
/// <para>⭐⭐ <b>The probe compiles against the REAL <c>Fbt.Compiler.BTreeBuilder</c>, not a stub.</b>
/// ⚠ That matters here specifically: the real <c>Subtree</c> carries two <c>[Caller*]</c> optional
/// parameters after <c>visualId</c>, so a stub with a two-parameter signature would silently agree
/// with an analyzer that counted parameters instead of ARGUMENTS. Binding against the shipped
/// assembly is what makes the positional-argument arm mean anything.</para>
/// </summary>
public sealed class SubtreeHostingAnalyzerTests
{
    /// <summary>
    /// The full runtime reference set, plus the three assemblies the probe source names.
    /// ⚠ Touching the types first is deliberate — an assembly that has not been loaded yet is not
    /// in <c>AppDomain.CurrentDomain.GetAssemblies()</c>.
    /// </summary>
    private static readonly MetadataReference[] References = BuildReferences();

    private static MetadataReference[] BuildReferences()
    {
        var byName = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            string key = Path.GetFileNameWithoutExtension(path);
            if (!byName.ContainsKey(key)) byName[key] = MetadataReference.CreateFromFile(path);
        }

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            foreach (var p in tpa.Split(Path.PathSeparator))
                if (p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) Add(p);

        Add(typeof(Fbt.Compiler.BTreeBuilder<,>).Assembly.Location);
        Add(typeof(Fbt.IAIContext).Assembly.Location);
        Add(typeof(Fdp.Toolkit.Behavior.BTreeContext).Assembly.Location);

        return byName.Values.ToArray();
    }

    private const string Preamble =
        "using System;\n" +
        "using Fbt.Compiler;\n" +
        "using Fdp.Toolkit.Behavior;\n" +
        "class Probe { void M(BTreeBuilder<byte, BTreeContext> b) {\n";

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string body)
    {
        var compilation = CSharpCompilation.Create(
            "AnalyzerProbe",
            new[] { CSharpSyntaxTree.ParseText(Preamble + body + "\n} }") },
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // ⛔ A probe that does not BIND would make every arm vacuously silent — the analyzer bails
        //    the moment GetSymbolInfo returns null. Prove the source compiles before trusting it.
        compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the analyzer probe source must bind for the semantic check to mean anything");

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new SubtreeHostingAnalyzer()));

        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    /// <summary>⭐⭐⭐ The case the warning exists for: no <c>visualId</c> ⇒ the site is an ORDINAL.</summary>
    [Fact]
    public async Task AHostingSubtreeWithNoVisualId_WarnsBEH010()
    {
        var diags = await AnalyzeAsync(@"b.Subtree(""Patrol"");");

        diags.Should().ContainSingle(d => d.Id == "BEH010")
             .Which.GetMessage().Should().Contain("Patrol").And.Contain("ORDINAL");
    }

    /// <summary>⭐ The control arm — with a stable site id there is nothing to warn about.</summary>
    [Fact]
    public async Task AHostingSubtreeWithAVisualId_IsSilent()
    {
        var diags = await AnalyzeAsync(@"b.Subtree(""Patrol"", visualId: Guid.NewGuid());");

        diags.Should().NotContain(d => d.Id == "BEH010");
    }

    /// <summary>⭐ A positional second argument counts too — the author supplied the site either way.</summary>
    [Fact]
    public async Task APositionalVisualId_IsAlsoAccepted()
    {
        var diags = await AnalyzeAsync(@"b.Subtree(""Patrol"", Guid.NewGuid());");

        diags.Should().NotContain(d => d.Id == "BEH010");
    }

    /// <summary>⭐ An empty child name plans no site at all, so it gets its own, louder message.</summary>
    [Fact]
    public async Task AnEmptyChildName_WarnsBEH011()
    {
        var diags = await AnalyzeAsync(@"b.Subtree("""");");

        diags.Should().Contain(d => d.Id == "BEH011");
        diags.Should().NotContain(d => d.Id == "BEH010", "the empty-name arm returns before the site check");
    }

    /// <summary>
    /// ⛔ A same-named method on ANY other type must not warn — the analyzer's containing-type check
    /// is what keeps <c>BEH010</c> off unrelated code, and nothing else tests it.
    /// </summary>
    [Fact]
    public async Task ASameNamedMethodOnAnotherType_IsNotAHostingSite()
    {
        var compilation = CSharpCompilation.Create(
            "AnalyzerProbeOther",
            new[]
            {
                CSharpSyntaxTree.ParseText(
                    "class Impostor { public void Subtree(string n) { } }\n" +
                    "class Probe2 { void M(Impostor i) { i.Subtree(\"Patrol\"); } }"),
            },
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diags = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SubtreeHostingAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        diags.Should().BeEmpty();
    }
}
