using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Core.Compiler;

public sealed record CompileOptions(
    CompilerMode Mode,
    INodeRegistry NodeRegistry,
    ITypeRegistry TypeRegistry,
    IEngineEventCatalog EngineEvents,
    IChannelCommandCatalog ChannelCommands,
    IWaitPrimitiveCatalog WaitPrimitives,
    IReadOnlyList<BlueprintSignature> SiblingSignatures,
    bool EmitPdbWithEmbeddedSource = false,
    string? VirtualSourcePath = null,
    IEqsTemplateCatalog? EqsTemplates = null,
    // Indicates which cluster node this Blueprint is compiled for.
    // BP2017 is only emitted when ExecutionNode == Brain.
    ExecutionNodeHint ExecutionNode = ExecutionNodeHint.Any);
