using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Roslyn;

/// <summary>
/// Compiles C# source strings in memory using Roslyn and loads the result
/// into a collectible AssemblyLoadContext.
/// </summary>
public sealed class InMemoryRoslynCompiler
{
    private readonly MetadataReferenceResolver _references;

    public InMemoryRoslynCompiler(MetadataReferenceResolver references)
        => _references = references;

    /// <summary>
    /// Compile source to PE and PDB bytes with embedded source text.
    /// Throws BlueprintCompileException on Roslyn errors.
    /// </summary>
    public (byte[] Pe, byte[] Pdb) Compile(
        string source,
        string virtualSourcePath,
        string assemblyName,
        DiagnosticSink sink)
    {
        var encoding = Encoding.UTF8;
        var sourceText = SourceText.From(source, encoding);
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            DocumentationMode.None,
            SourceCodeKind.Regular);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            sourceText,
            parseOptions,
            path: virtualSourcePath);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            _references.Resolve(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                deterministic: true,
                allowUnsafe: true));

        var embeddedText = EmbeddedTextHelper.Create(virtualSourcePath, source);
        var emitOptions = new EmitOptions(
            debugInformationFormat: DebugInformationFormat.PortablePdb);

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();

        var result = compilation.Emit(
            peStream: peStream,
            pdbStream: pdbStream,
            embeddedTexts: new[] { embeddedText },
            options: emitOptions);

        if (!result.Success)
        {
            var bpDiags = result.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => Diagnostics.Diagnostic.Error(
                    DiagnosticCodes.BP7001,
                    $"Roslyn: {d.Id} {d.GetMessage()}"))
                .ToList();

            foreach (var diag in bpDiags)
                sink.Add(diag);

            throw new BlueprintCompileException(
                "In-memory Roslyn compilation failed. See diagnostics.",
                bpDiags);
        }

        return (peStream.ToArray(), pdbStream.ToArray());
    }

    /// <summary>
    /// Compile then load into a new collectible AssemblyLoadContext.
    /// The caller owns the ALC and is responsible for calling Unload().
    /// </summary>
    public (Assembly Assembly, AssemblyLoadContext Alc) CompileAndLoad(
        string source,
        string virtualSourcePath,
        string assemblyName,
        DiagnosticSink sink)
    {
        var (pe, pdb) = Compile(source, virtualSourcePath, assemblyName, sink);
        var alc = new AssemblyLoadContext($"BlueprintPatch_{assemblyName}", isCollectible: true);
        using var peStream = new MemoryStream(pe);
        using var pdbStream = new MemoryStream(pdb);
        var assembly = alc.LoadFromStream(peStream, pdbStream);
        return (assembly, alc);
    }
}
