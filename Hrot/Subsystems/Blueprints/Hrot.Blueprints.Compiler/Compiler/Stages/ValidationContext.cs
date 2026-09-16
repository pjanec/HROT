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
    public ExecutionNodeHint ExecutionNode { get; }

    /// <summary>
    /// U-7 / <c>BP-228</c> — the compile's type oracle, or null. ⚠ <b>Null is the normal case</b>:
    /// exactly one production site supplies one (<c>BlueprintIncrementalGenerator</c>). Rails that
    /// consult it must treat absence as "no opinion", never as "does not exist".
    /// </summary>
    public IClrSignatureResolver? ClrSignatureResolver { get; }

    /// <summary>
    /// ⭐⭐ <c>S2</c> — the compile's struct-size oracle (FQN → managed byte size), or null.
    /// ⚠ <b>Null is the normal case</b>, same as <see cref="ClrSignatureResolver"/>. ⛔ Absence is
    /// <i>"no opinion"</i>, and a caller must then treat the AN2 4-byte placeholder as UNRELIABLE —
    /// never as a size it may bake an offset from.
    /// </summary>
    public Func<string, int?>? StructSizeOracle { get; }

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
        ExecutionNode    = options.ExecutionNode;
        ClrSignatureResolver = options.ClrSignatureResolver;
        StructSizeOracle     = options.StructSizeOracle;
        SiblingSignaturesById = options.SiblingSignatures
            .ToDictionary(s => s.AssetId);
    }
}
