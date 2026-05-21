using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Core.Compiler;

public interface IBlueprintCompiler
{
    CompileResult Compile(BlueprintAsset asset, CompileOptions options);
    ValidationResult Validate(BlueprintAsset asset, ValidationOptions? options = null);
}

public sealed class BlueprintCompiler : IBlueprintCompiler
{
    public CompileResult Compile(BlueprintAsset asset, CompileOptions options)
    {
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, options);

        // Stage 2 -- Validate
        Stage2_Validate.Run(asset, ctx);
        if (sink.HasErrors) return FailResult(sink, asset);

        // Stage 3 -- Normalize
        asset = Stage3_Normalize.Run(asset, ctx);
        if (sink.HasErrors) return FailResult(sink, asset);

        // Stage 4 -- Type resolve
        var typed = Stage4_TypeResolve.Run(asset, ctx);
        if (sink.HasErrors) return FailResult(sink, typed.Asset);

        // Stage 5 -- Schedule
        var ir = Stage5_Schedule.Run(typed, ctx);
        if (sink.HasErrors) return FailResult(sink, typed.Asset);

        // Stage 6 -- Lower
        var lowered = Stage6_Lower.Run(ir, options.Mode, sink);
        if (sink.HasErrors) return FailResult(sink, typed.Asset);

        // Stage 7 -- Emit C# source
        var (generatedSource, debugMap) = Stage7_Emit.Run(lowered, options.Mode, sink);
        if (sink.HasErrors) return FailResult(sink, typed.Asset);

        return new CompileResult(
            Succeeded:         true,
            GeneratedSource:   generatedSource,
            GeneratedFileName: $"{lowered.SanitizedName}_{lowered.BlueprintId:X8}_Bp.g.cs",
            BlueprintId:       lowered.BlueprintId,
            StructureHash:     lowered.StructureHash,
            DebugMap:          debugMap,
            Diagnostics:       sink.All,
            CanonicalAsset:    typed.Asset,
            PortablePdb:       null,
            PortablePe:        null);
    }

    public ValidationResult Validate(BlueprintAsset asset, ValidationOptions? options = null)
    {
        var sink = new DiagnosticSink();
        var compileOptions = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        var ctx = new ValidationContext(sink, compileOptions);
        Stage2_Validate.Run(asset, ctx);
        return new ValidationResult(sink.All);
    }

    private static CompileResult FailResult(DiagnosticSink sink, BlueprintAsset? asset) =>
        new CompileResult(
            Succeeded:       false,
            GeneratedSource: null,
            GeneratedFileName: null,
            BlueprintId:     0,
            StructureHash:   0,
            DebugMap:        null,
            Diagnostics:     sink.All,
            CanonicalAsset:  asset,
            PortablePdb:     null,
            PortablePe:      null);
}
