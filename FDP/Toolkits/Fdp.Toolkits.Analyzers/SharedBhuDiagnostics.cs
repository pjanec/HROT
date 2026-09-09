using Microsoft.CodeAnalysis;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    // Shared diagnostic descriptors used by both BTreeActionGenerator and HsmActionGenerator.
    // Centralised here to avoid RS1019 duplicate-ID warnings.
    internal static class SharedBhuDiagnostics
    {
        internal static readonly DiagnosticDescriptor BHU001_TypeMismatch = new DiagnosticDescriptor(
            id: "BHU_001",
            title: "SharedAi parameter type mismatch",
            messageFormat: "Method ''{0}'': ref parameter type ''{1}'' does not match DTO field ''{2}.{3}'' of type ''{4}''",
            category: "BTreeActionGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor BHU002_NonStatic = new DiagnosticDescriptor(
            id: "BHU_002",
            title: "SharedAi method must be static",
            messageFormat: "Method ''{0}'' annotated with [SharedAiCondition] or [SharedAiAction] must be static; skipping",
            category: "BTreeActionGenerator",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor BHU003_UnknownField = new DiagnosticDescriptor(
            id: "BHU_003",
            title: "SharedAi DTO field not found",
            messageFormat: "Method ''{0}'': field ''{1}'' not found on type ''{2}'' or offset cannot be computed",
            category: "BTreeActionGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // ---- HSM dispatcher id space (W1 / Batch 58) ----------------------------
        //
        // ⭐⭐ HsmActionDispatcher.RegisterAction/RegisterGuard is `Table[id] = ptr` — LAST WRITER WINS,
        // with no guard, no diagnostic and no throw. Two mechanisms feed that table from one build:
        //   • hashed  — HsmActionGenerator: `ushort id = ComputeHash(name)`, FNV-1a truncated to 16
        //     bits, so anywhere in 0…65535;
        //   • counted — HsmBridgeEmitCore: literal counters from 100 (actions) and 200 (guards),
        //     registering NO-OP stub bodies (`__hsActionStub` / `__hsGuardStub { }`).
        // ⛔ A gate over the hashed set alone cannot see the second: those ids are never hashed, so they
        // never enter the hash set. These descriptors are therefore reported by an ANALYZER over the
        // FINAL compilation, where both mechanisms have already become literal `Register…(id, …)` calls.
        //
        // ⚠ Same family as UT0102/UT0103/UT0150 — "mirror, do not invent". Those prove the pattern was
        // recognised once, in one id space, and not generalised.

        // ⚠ RS1037: both are reported from a CompilationEndAction, so the tag is required — the driver
        //   uses it to decide whether the analyzer may be skipped on an unchanged compilation.
        internal static readonly DiagnosticDescriptor BHU020_DuplicateDispatcherId = new DiagnosticDescriptor(
            id: "BHU_020",
            title: "HSM dispatcher id registered twice",
            messageFormat: "HSM {0} id {1} is registered more than once, by {2}. "
                         + "The dispatcher table is last-writer-wins, so one of these is silently "
                         + "replaced and never runs; rename the colliding entry.",
            category: "HsmActionGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        internal static readonly DiagnosticDescriptor BHU021_ReservedDispatcherId = new DiagnosticDescriptor(
            id: "BHU_021",
            title: "HSM dispatcher id is a reserved value",
            messageFormat: "HSM {0} id {1} is reserved: {2}. "
                         + "The registration succeeds and the entry is then never invoked; rename it "
                         + "so it hashes elsewhere.",
            category: "HsmActionGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        internal static readonly DiagnosticDescriptor BHU016_DeactivatorMissingTarget = new DiagnosticDescriptor(
            id: "BHU_016",
            title: "BTreeDeactivator missing or empty TargetAction",
            messageFormat: "Deactivator method ''{0}'' has an empty or missing TargetAction; skipping emission",
            category: "BTreeActionGenerator",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor BHU017_DeactivatorUnknownTarget = new DiagnosticDescriptor(
            id: "BHU_017",
            title: "BTreeDeactivator TargetAction not found",
            messageFormat: "Deactivator method ''{0}'': TargetAction ''{1}'' does not match any [BTreeAction] or [BTreeCondition] method in this compilation",
            category: "BTreeActionGenerator",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
