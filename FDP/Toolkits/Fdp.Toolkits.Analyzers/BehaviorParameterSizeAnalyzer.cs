using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    /// <summary>
    /// Enforces FDP blackboard memory layout constraints at compile time.
    ///
    /// Any method annotated with [SharedAiAction] or [SharedAiCondition] that binds a DTO
    /// whose unmanaged size exceeds <see cref="MaxRootParamsByteSize"/> bytes will be
    /// flagged as a compiler error (FDP_001).
    ///
    /// <para>⭐⭐⭐ <b><c>CE-307</c> (2026-09-22) — THIS STOPPED BEING A CAP.</b> It used to refuse any
    /// DTO over <b>100</b> bytes, because params lived inline in <c>BrainBlackboard</c> <i>with the
    /// SoftAdvice and Interrupt registers after them</i>, so an oversized DTO silently overwrote
    /// unrelated state. ⛔ Both halves of that rationale are gone: <c>O2</c> moved the registers to
    /// <c>BrainInterrupts</c>, and <c>P3-C</c> moved params into their own occurrence slot, sized to
    /// the behaviour and promoted up the 256/1024/4096/16384 tier ladder. <b>There are no neighbours
    /// to corrupt.</b></para>
    ///
    /// <para>⭐ What survives is a <b>capacity</b> check: a DTO wider than the largest tier's whole
    /// payload can never be stored by any tier, so it is still worth refusing at compile time rather
    /// than at run time. ⚠ Passing it is NOT a guarantee of fit — the region shares its tier with the
    /// behaviour's stateful slots — and that case is caught loudly at ingress instead.</para>
    ///
    /// This analyzer is intentionally part of the FDP Behavior domain and must never be
    /// moved into the generic FastBTree/FastHSM libraries, which have no knowledge of the
    /// occurrence storage tiers.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class BehaviorParameterSizeAnalyzer : DiagnosticAnalyzer
    {
        // Mirrors BehaviorConstants.MaxRootParamsByteSize (= BlueprintTierLadder.Tier16384PayloadSize).
        // Intentionally inlined here because this analyzer targets netstandard2.0
        // and cannot reference the net8.0 Fdp.Toolkits runtime assembly.
        // ⭐ InlineBudgetConstantAgreementTests is the only thing that can see both sides; it pins them.
        private const int MaxRootParamsByteSize = 16096;

        private static readonly DiagnosticDescriptor FDP001_DtoTooLarge = new DiagnosticDescriptor(
            id: "FDP_001",
            title: "Behavior parameter DTO exceeds occurrence storage capacity",
            messageFormat: "Method '{0}': DTO type '{1}' requires {2} bytes, exceeding {3} — the payload of the largest occurrence storage tier. No tier can hold a root params region this wide.",
            category: "Fdp.Memory",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(FDP001_DtoTooLarge);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        }

        private static void AnalyzeMethod(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;

            foreach (var attr in method.GetAttributes())
            {
                string? attrName = attr.AttributeClass?.Name;
                if (attrName != "SharedAiActionAttribute" && attrName != "SharedAiConditionAttribute")
                    continue;

                if (attr.ConstructorArguments.Length < 1) continue;

                var dtoTypeSymbol = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
                if (dtoTypeSymbol == null) continue;

                int structSize = Fdp.Toolkit.Behavior.Shared.RoslynStructLayout.StructSize(dtoTypeSymbol);
                if (structSize < 0) continue; // unknown layout, skip safely

                if (structSize > MaxRootParamsByteSize)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        FDP001_DtoTooLarge,
                        method.Locations.FirstOrDefault(),
                        method.Name,
                        dtoTypeSymbol.ToDisplayString(),
                        structSize,
                        MaxRootParamsByteSize));
                }
            }
        }

        // ---- Struct layout: the ONE algorithm, Shared/RoslynStructLayout.cs (CE-2027). It was duplicated here "intentionally"
        //      because a helper ASSEMBLY could not be shared; a linked source file adds none. ----
    }
}
