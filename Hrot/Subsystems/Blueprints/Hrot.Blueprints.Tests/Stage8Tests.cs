using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Compiler.Roslyn;
using Hrot.Blueprints.Tests.Builders;
using Microsoft.CodeAnalysis.CSharp;
using RoslynCompileException = Hrot.Blueprints.Core.Compiler.Roslyn.BlueprintCompileException;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Tests covering Compiler Stage 8 (TASK-CP-005).
/// Test method names are suffixed with Stage8 so they can be filtered:
///   dotnet test --filter "Stage8"
/// </summary>
public sealed class Stage8Tests
{
    private static (BlueprintCompiler Compiler, BlueprintAsset Asset) MakeSimpleLibrary()
    {
        var asset = BlueprintAssetBuilder
            .Library("Stage8Lib")
            .WithGraph("Add", g => g.Entry().Return())
            .Build();
        return (new BlueprintCompiler(), asset);
    }

    private static CompileOptions OptionsWithPdb() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>(),
            EmitPdbWithEmbeddedSource: true);

    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    // SC1: InMemoryRoslynCompiler produces non-empty PE + PDB
    [Fact]
    public void Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb()
    {
        var (compiler, asset) = MakeSimpleLibrary();
        var result = compiler.Compile(asset, OptionsWithPdb());

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.PortablePe);
        Assert.NotNull(result.PortablePdb);
        Assert.True(result.PortablePe!.Length > 0, "PE should be non-empty");
        Assert.True(result.PortablePdb!.Length > 0, "PDB should be non-empty");
    }

    // SC2: PDB contains embedded source (verified by extracting and comparing content)
    [Fact]
    public void Stage8_PdbContainsEmbeddedSource()
    {
        var (compiler, asset) = MakeSimpleLibrary();
        var result = compiler.Compile(asset, OptionsWithPdb());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.PortablePdb);
        Assert.NotNull(result.GeneratedSource);

        // Extract embedded source from the portable PDB and compare to generated source.
        var embeddedSource = ExtractEmbeddedSourceFromPdb(result.PortablePdb!);
        Assert.NotNull(embeddedSource);
        Assert.Equal(result.GeneratedSource, embeddedSource);
    }

    /// <summary>
    /// Reads the first document's embedded source from a portable PDB byte array.
    /// Returns null if no embedded source is found.
    /// </summary>
    private static string? ExtractEmbeddedSourceFromPdb(byte[] pdbBytes)
    {
        // EmbeddedSource custom debug information GUID (defined by the Portable PDB spec).
        var embeddedSourceGuid = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

        using var ms = new MemoryStream(pdbBytes);
        using var provider = System.Reflection.Metadata.MetadataReaderProvider.FromPortablePdbStream(ms);
        var reader = provider.GetMetadataReader();

        foreach (var docHandle in reader.Documents)
        {
            foreach (var cdiHandle in reader.GetCustomDebugInformation(docHandle))
            {
                var cdi  = reader.GetCustomDebugInformation(cdiHandle);
                var kind = reader.GetGuid(cdi.Kind);
                if (kind != embeddedSourceGuid)
                    continue;

                var blob = reader.GetBlobBytes(cdi.Value);
                if (blob.Length < 4) continue;

                // First 4 bytes are the uncompressed length; 0 means data is stored raw.
                int uncompressedLen = BitConverter.ToInt32(blob, 0);
                byte[] sourceBytes;
                if (uncompressedLen == 0)
                {
                    // Raw (not compressed).
                    sourceBytes = blob[4..];
                }
                else
                {
                    // Deflate-compressed.
                    using var compressed   = new MemoryStream(blob, 4, blob.Length - 4);
                    using var deflate      = new System.IO.Compression.DeflateStream(
                        compressed, System.IO.Compression.CompressionMode.Decompress);
                    using var decompressed = new MemoryStream();
                    deflate.CopyTo(decompressed);
                    sourceBytes = decompressed.ToArray();
                }

                // Strip UTF-8 BOM if present.
                if (sourceBytes.Length >= 3
                    && sourceBytes[0] == 0xEF
                    && sourceBytes[1] == 0xBB
                    && sourceBytes[2] == 0xBF)
                {
                    sourceBytes = sourceBytes[3..];
                }

                return System.Text.Encoding.UTF8.GetString(sourceBytes);
            }
        }
        return null;
    }

    // SC3: InMemoryRoslynCompiler throws BlueprintCompileException for invalid C#
    [Fact]
    public void Stage8_InvalidCSharp_ThrowsBlueprintCompileException()
    {
        var invalidSource = "this is not valid C# code { unclosed";
        var sink = new DiagnosticSink();
        var refs = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());
        var compiler = new InMemoryRoslynCompiler(refs);

        Assert.Throws<RoslynCompileException>(() =>
            compiler.Compile(invalidSource, "invalid.g.cs", "InvalidAssembly", sink));

        Assert.True(sink.HasErrors, "Sink should have errors after failed compile");
    }

    // SC4: MetadataReferenceResolver excludes in-memory (Location == "") assemblies
    [Fact]
    public void Stage8_MetadataReferenceResolver_ExcludesNoLocationAssemblies()
    {
        // Create a tiny in-memory assembly via Roslyn
        var source = "namespace Test { public class X {} }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());
        var comp = CSharpCompilation.Create(
            "InMemoryAssembly",
            new[] { tree },
            refs.Resolve(),
            new CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        comp.Emit(ms);
        ms.Position = 0;

        var alc = new System.Runtime.Loader.AssemblyLoadContext("SC4Test", isCollectible: true);
        try
        {
            var asm = alc.LoadFromStream(ms);
            // asm.Location should be "" for in-memory loaded assembly
            Assert.Equal("", asm.Location);

            // ForRuntimeAssemblies should exclude it
            var allAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Concat(new[] { asm });
            var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(allAssemblies);
            var resolvedPaths = resolver.Resolve()
                .Cast<Microsoft.CodeAnalysis.PortableExecutableReference>()
                .Select(r => r.FilePath)
                .ToList();

            Assert.DoesNotContain("", resolvedPaths);
        }
        finally
        {
            alc.Unload();
        }
    }

    // SC5: BlueprintSignatureParser.Parse extracts basic fields
    [Fact]
    public void Stage8_SignatureParser_ExtractsFields()
    {
        var json = """
        {
            "assetId": "11111111-2222-3333-4444-555555555555",
            "name": "TestAiPrimitive",
            "dispatch": "AiPrimitive",
            "primitive": { "hostings": ["BTreeAction"] },
            "graphs": [
                { "id": "g1", "name": "Execute", "kind": "Function", "nodes": [], "links": [] }
            ]
        }
        """;

        var sig = BlueprintSignatureParser.Parse("test.bp.json", json);

        Assert.Equal("test.bp.json", sig.Path);
        Assert.Equal(new Guid("11111111-2222-3333-4444-555555555555"), sig.AssetId);
        Assert.Equal("TestAiPrimitive", sig.Name);
        Assert.Equal(AssetDispatchKind.AiPrimitive, sig.Dispatch);
        Assert.Contains("Execute", sig.ExportedFunctionNames);
        Assert.Contains(AiPrimitiveHosting.BTreeAction, sig.Hostings);
    }

    // SC6: DebugMap serialization is deterministic
    [Fact]
    public void Stage8_DebugMapSerializer_IsDeterministic()
    {
        var (compiler, asset) = MakeSimpleLibrary();
        var result1 = compiler.Compile(asset, DefaultOptions());
        var result2 = compiler.Compile(asset, DefaultOptions());

        Assert.True(result1.Succeeded);
        Assert.True(result2.Succeeded);
        Assert.NotNull(result1.DebugMap);
        Assert.NotNull(result2.DebugMap);

        var json1 = DebugMapSerializer.Serialize(result1.DebugMap!);
        var json2 = DebugMapSerializer.Serialize(result2.DebugMap!);
        Assert.Equal(json1, json2);
    }
}
