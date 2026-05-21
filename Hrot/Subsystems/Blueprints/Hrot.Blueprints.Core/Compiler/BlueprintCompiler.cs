using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler;

public interface IBlueprintCompiler
{
    CompileResult Compile(BlueprintAsset asset, CompileOptions options);
    ValidationResult Validate(BlueprintAsset asset, ValidationOptions? options = null);
}

public sealed class BlueprintCompiler : IBlueprintCompiler
{
    public CompileResult Compile(BlueprintAsset asset, CompileOptions options)
        => throw new NotImplementedException("Compiler Stage 1-8 not yet implemented (Phase 3).");

    public ValidationResult Validate(BlueprintAsset asset, ValidationOptions? options = null)
        => throw new NotImplementedException("Compiler Stage 2 not yet implemented (Phase 3).");
}
