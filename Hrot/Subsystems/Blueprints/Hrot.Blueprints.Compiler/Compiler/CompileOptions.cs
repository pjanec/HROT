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
    string? VirtualSourcePath = null);
