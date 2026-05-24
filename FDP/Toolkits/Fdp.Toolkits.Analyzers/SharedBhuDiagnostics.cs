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
