// Placeholder for Hrot.Blueprints.Core assembly.
// Asset schema types are defined in the Assets/ subdirectory.
using System.Runtime.CompilerServices;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Roslyn;
using Hrot.Blueprints.Core.Compiler.Stages;

namespace Hrot.Blueprints.Core;

internal static class BlueprintsCoreModuleInit
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "Intentional: wires Roslyn stage into BlueprintCompiler on assembly load.")]
    internal static void Initialize()
    {
        // Wire the Roslyn finalization stage into BlueprintCompiler so that
        // Compile() with EmitPdbWithEmbeddedSource=true produces real PE+PDB bytes.
        BlueprintCompiler.RoslynFinalizer = (source, virtualPath, assemblyName, sink) =>
        {
            var refs = MetadataReferenceResolver.ForRuntimeAssemblies(
                AppDomain.CurrentDomain.GetAssemblies());
            return Stage8_RoslynFinalize.Run(source, virtualPath, assemblyName, refs, sink);
        };
    }
}
