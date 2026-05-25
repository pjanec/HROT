using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Stages;

/// <summary>
/// Mutable context threaded through all Stage 2 validators.
/// Carries catalogs, registries, and sibling signature table.
/// </summary>
internal sealed class ValidationContext
{
    public DiagnosticSink Diagnostics { get; }
    public ITypeRegistry TypeRegistry { get; }
    public INodeRegistry NodeRegistry { get; }
    public IEngineEventCatalog EngineEvents { get; }
    public IChannelCommandCatalog ChannelCommands { get; }
    public IWaitPrimitiveCatalog WaitPrimitives { get; }
    public IEqsTemplateCatalog? EqsTemplates { get; }

    // Patch 1: signatures only, NOT full assets.
    public IReadOnlyDictionary<Guid, BlueprintSignature> SiblingSignaturesById { get; }

    // AssetId set when beginning validation of a specific asset.
    public Guid AssetId { get; set; }

    public ValidationContext(DiagnosticSink sink, CompileOptions options)
    {
        Diagnostics      = sink;
        TypeRegistry     = options.TypeRegistry;
        NodeRegistry     = options.NodeRegistry;
        EngineEvents     = options.EngineEvents;
        ChannelCommands  = options.ChannelCommands;
        WaitPrimitives   = options.WaitPrimitives;
        EqsTemplates     = options.EqsTemplates;
        SiblingSignaturesById = options.SiblingSignatures
            .ToDictionary(s => s.AssetId);
    }
}
