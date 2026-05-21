using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Roslyn;

namespace Hrot.Blueprints.Core.Compiler.Stages;

internal static class Stage8_RoslynFinalize
{
    public static (byte[] Pe, byte[] Pdb) Run(
        string generatedSource,
        string virtualSourcePath,
        string assemblyName,
        MetadataReferenceResolver references,
        DiagnosticSink sink)
    {
        var compiler = new Roslyn.InMemoryRoslynCompiler(references);
        return compiler.Compile(generatedSource, virtualSourcePath, assemblyName, sink);
    }
}
