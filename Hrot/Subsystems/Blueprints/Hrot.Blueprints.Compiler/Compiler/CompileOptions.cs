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
    ExecutionNodeHint ExecutionNode = ExecutionNodeHint.Any,
    // Optional semantic-model-backed resolver for FunctionCall target signatures. Supplied by the
    // Roslyn incremental generator (which cannot reflect over the assembly it is compiling) so Stage0
    // can rehydrate typed FunctionCall pins without explicit pins or runtime reflection. Null in the
    // in-process compiler/editor path, where reflection over loaded assemblies is used instead.
    IClrSignatureResolver? ClrSignatureResolver = null);
