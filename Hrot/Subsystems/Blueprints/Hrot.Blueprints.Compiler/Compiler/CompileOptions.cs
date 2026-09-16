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
    IClrSignatureResolver? ClrSignatureResolver = null,

    // ⭐⭐ S2 — the struct-size oracle. FQN in, managed byte size out, null for "no opinion".
    //
    // ⛔ NOT a project reference, and not an ITypeRegistry member. A bare Func<string,int?> is the
    // shape the design mandate names (user decision 2026-06-15, .dev/btree-ai-action-binding
    // TASK-DETAIL.md:58): "StructSizeResolver lives in Hrot.AiEditor.Generators (Roslyn-aware) and is
    // injected via Func<string,int?>. The Persistence assembly stays netstandard2.0 / Roslyn-free."
    // The same shape already ships as BTreeBlackboardPackHelper.Pack(vars, Func<string,int?>, out t).
    //
    // ⚠ Null is the NORMAL case, exactly as ClrSignatureResolver is: one production site supplies one
    // (BlueprintIncrementalGenerator). Absence means "no opinion", and the caller must then mark the
    // size UNRELIABLE rather than trust the AN2 4-byte placeholder -- see Stage4_TypeResolve.
    Func<string, int?>? StructSizeOracle = null);
