using Microsoft.CodeAnalysis;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    // Centralised diagnostic descriptors shared by UtilityInputGenerator and
    // UtilityAuthoringAnalyzer (see §6 of the source-generator design doc).
    // Centralizing avoids RS1019 duplicate-descriptor warnings when both components
    // share a Roslyn host.
    internal static class SharedUtilityDiagnostics
    {
        // ---- Input attribute diagnostics ----------------------------------------

        // UT0101: [UtilityInput] missing Name
        internal static readonly DiagnosticDescriptor UT0101_MissingName = new DiagnosticDescriptor(
            id: "UT0101",
            title: "UtilityInput missing Name",
            messageFormat: "Method ''{0}'' has [UtilityInput] but Name is null or empty; the method will be excluded from the registrar",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // UT0102: duplicate input Name across compilation
        internal static readonly DiagnosticDescriptor UT0102_DuplicateName = new DiagnosticDescriptor(
            id: "UT0102",
            title: "UtilityInput duplicate Name",
            messageFormat: "Method ''{0}'' has duplicate [UtilityInput] Name ''{1}''; only the first declaration will be registered",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // UT0103: hash collision (two input names produce same FNV-1a-16)
        internal static readonly DiagnosticDescriptor UT0103_HashCollision = new DiagnosticDescriptor(
            id: "UT0103",
            title: "UtilityInput hash collision",
            messageFormat: "Method ''{0}'' (name ''{1}'') produces the same FNV-1a-16 hash (0x{2:X4}) as ''{3}'' (name ''{4}''); rename one of these inputs",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // ---- Signature diagnostics -----------------------------------------------

        // UT0110: [UtilityInput] method is not static
        internal static readonly DiagnosticDescriptor UT0110_NotStatic = new DiagnosticDescriptor(
            id: "UT0110",
            title: "UtilityInput method must be static",
            messageFormat: "Method ''{0}'' annotated with [UtilityInput] must be static; skipping",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // UT0111: [UtilityInput] does not return float
        internal static readonly DiagnosticDescriptor UT0111_NotFloat = new DiagnosticDescriptor(
            id: "UT0111",
            title: "UtilityInput method must return float",
            messageFormat: "Method ''{0}'' annotated with [UtilityInput] must return float, but returns ''{1}''; skipping",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // UT0112: [UtilityInput] parameter is not (in UtilityInputCtx)
        internal static readonly DiagnosticDescriptor UT0112_WrongSignature = new DiagnosticDescriptor(
            id: "UT0112",
            title: "UtilityInput method has wrong parameter",
            messageFormat: "Method ''{0}'' annotated with [UtilityInput] must take exactly one parameter of type 'in UtilityInputCtx'; skipping",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // ---- Decision attribute diagnostics -------------------------------------

        // UT0140: [UtilityDecision] class missing IUtilityDecisionDefinition
        internal static readonly DiagnosticDescriptor UT0140_MissingInterface = new DiagnosticDescriptor(
            id: "UT0140",
            title: "UtilityDecision missing IUtilityDecisionDefinition",
            messageFormat: "Class ''{0}'' has [UtilityDecision] but does not implement IUtilityDecisionDefinition; the class will be excluded from the catalog",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // UT0141: [UtilityDecision] class missing public static void Build(IUtilityDecisionBuilder) method
        internal static readonly DiagnosticDescriptor UT0141_MissingBuildMethod = new DiagnosticDescriptor(
            id: "UT0141",
            title: "UtilityDecision missing static Build method",
            messageFormat: "Class ''{0}'' has [UtilityDecision] but is missing a public static void Build(IUtilityDecisionBuilder) method; the class will be excluded from the catalog",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // UT0150: duplicate AssetId across two [UtilityDecision] classes
        internal static readonly DiagnosticDescriptor UT0150_DuplicateAssetId = new DiagnosticDescriptor(
            id: "UT0150",
            title: "UtilityDecision duplicate AssetId",
            messageFormat: "Class ''{0}'' has duplicate [UtilityDecision] AssetId ''{1}''; only the first declaration will be registered",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // UT0151: ManeuverSelect decision references a Candidate or Target context binding
        internal static readonly DiagnosticDescriptor UT0151_ManeuverSelectInvalidContext = new DiagnosticDescriptor(
            id: "UT0151",
            title: "ManeuverSelect decision uses invalid context binding",
            messageFormat: "Class ''{0}'' is a ManeuverSelect decision but accesses ''{1}''; ManeuverSelect decisions must not bind Candidate or Target context — use squad-leader self-inputs only",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // ---- Catalog-aware consideration diagnostics ----------------------------

        // UT0120: consideration references an unknown input name (not in catalog)
        internal static readonly DiagnosticDescriptor UT0120_UnknownInput = new DiagnosticDescriptor(
            id: "UT0120",
            title: "UtilityDecision references unknown input",
            messageFormat: "Consideration in ''{0}'' references unknown input ''{1}''; add a [UtilityInput] method with that Name or fix the typo",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // UT0121: input used with a context outside its AllowedContexts
        internal static readonly DiagnosticDescriptor UT0121_WrongContext = new DiagnosticDescriptor(
            id: "UT0121",
            title: "UtilityInput used with disallowed context",
            messageFormat: "Input ''{0}'' in ''{1}'' is used with context ''{2}'' which is outside its allowed contexts",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // UT0122: parameterized input missing its required param
        internal static readonly DiagnosticDescriptor UT0122_MissingParam = new DiagnosticDescriptor(
            id: "UT0122",
            title: "UtilityInput missing required parameter",
            messageFormat: "Input ''{0}'' in ''{1}'' requires a parameter but none was supplied",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // ---- Purity check -------------------------------------------------------

        // UT0130: Build reads disallowed runtime state (purity violation)
        internal static readonly DiagnosticDescriptor UT0130_ImpureBuild = new DiagnosticDescriptor(
            id: "UT0130",
            title: "UtilityDecision Build must be pure",
            messageFormat: "Method ''{0}'' in [UtilityDecision] class reads non-constant state. Build() must be pure.",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // ---- Weight check -------------------------------------------------------

        // UT0131: weight outside [0, 1]
        internal static readonly DiagnosticDescriptor UT0131_WeightOutOfRange = new DiagnosticDescriptor(
            id: "UT0131",
            title: "UtilityDecision consideration weight out of range",
            messageFormat: "Consideration weight {0} in ''{1}'' is outside [0, 1]; weights are clamped at runtime but the author should correct the value",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // ---- Decision structure checks ------------------------------------------

        // UT0143: PostureSelect decision has zero options
        internal static readonly DiagnosticDescriptor UT0143_ZeroOptions = new DiagnosticDescriptor(
            id: "UT0143",
            title: "PostureSelect decision has zero options",
            messageFormat: "Class ''{0}'' is a PostureSelect decision but defines no options in Build(); add at least one Option(...) call",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // UT0144: all options are product-mode with gates, no sum-mode fallback
        internal static readonly DiagnosticDescriptor UT0144_NoSumFallback = new DiagnosticDescriptor(
            id: "UT0144",
            title: "UtilityDecision has no WeightedSum fallback option",
            messageFormat: "All options in ''{0}'' use WeightedProduct scoring with gate considerations; add a WeightedSum fallback option to avoid a no-winner situation",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // UT0145: duplicate OptionId within a decision
        internal static readonly DiagnosticDescriptor UT0145_DuplicateOptionId = new DiagnosticDescriptor(
            id: "UT0145",
            title: "UtilityDecision duplicate OptionId",
            messageFormat: "Option id {0} appears more than once in ''{1}''; each option must have a unique id",
            category: "Fdp.UtilityAI",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
