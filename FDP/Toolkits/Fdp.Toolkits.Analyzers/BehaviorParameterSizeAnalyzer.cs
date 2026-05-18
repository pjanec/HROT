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
    /// whose unmanaged size exceeds <see cref="MaxBehaviorParamByteSize"/> bytes will be
    /// flagged as a compiler error (FDP_001).
    ///
    /// This analyzer is intentionally part of the FDP Behavior domain and must never be
    /// moved into the generic FastBTree/FastHSM libraries, which have no knowledge of the
    /// 128-byte BrainBlackboard layout or its partitioning into BehaviorParameters,
    /// SoftAdvice, and Interrupt regions.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class BehaviorParameterSizeAnalyzer : DiagnosticAnalyzer
    {
        // Mirrors BehaviorConstants.MaxBehaviorParamByteSize.
        // Intentionally inlined here because this analyzer targets netstandard2.0
        // and cannot reference the net8.0 Fdp.Toolkits runtime assembly.
        private const int MaxBehaviorParamByteSize = 100;

        private static readonly DiagnosticDescriptor FDP001_DtoTooLarge = new DiagnosticDescriptor(
            id: "FDP_001",
            title: "Behavior parameter DTO exceeds BrainBlackboard capacity",
            messageFormat: "Method '{0}': DTO type '{1}' requires {2} bytes, exceeding the {3}-byte BehaviorParameters region. This would corrupt the SoftAdvice and Interrupt registers in BrainBlackboard.",
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

                int structSize = ComputeStructSize(dtoTypeSymbol);
                if (structSize < 0) continue; // unknown layout, skip safely

                if (structSize > MaxBehaviorParamByteSize)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        FDP001_DtoTooLarge,
                        method.Locations.FirstOrDefault(),
                        method.Name,
                        dtoTypeSymbol.ToDisplayString(),
                        structSize,
                        MaxBehaviorParamByteSize));
                }
            }
        }

        // ---- Struct layout computation (mirrors BTreeActionGenerator / HsmActionGenerator) ----
        // Duplicated intentionally: the generic source generators must not carry domain rules,
        // so the layout math is owned here, at the correct architectural layer.

        private static int ComputeStructSize(INamedTypeSymbol type)
        {
            bool isExplicit = type.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "StructLayoutAttribute"
                       && a.ConstructorArguments.Length > 0
                       && a.ConstructorArguments[0].Value is int v && v == 2); // LayoutKind.Explicit = 2

            var fields = type.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => !f.IsStatic && !f.IsConst)
                .ToList();

            if (isExplicit)
            {
                int max = 0;
                foreach (var field in fields)
                {
                    var fa = field.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "FieldOffsetAttribute");
                    if (fa == null || fa.ConstructorArguments.Length == 0) return -1;
                    int fo = (int)fa.ConstructorArguments[0].Value!;
                    int fs = GetTypeSize(field.Type);
                    if (fs < 0) return -1;
                    max = System.Math.Max(max, fo + fs);
                }
                return max;
            }
            else
            {
                int offset = 0, maxAlign = 1;
                foreach (var field in fields)
                {
                    int size = GetTypeSize(field.Type);
                    int align = GetTypeAlign(field.Type);
                    if (size < 0) return -1;
                    if (align > maxAlign) maxAlign = align;
                    if (size > 0) offset = AlignUp(offset, align);
                    offset += size;
                }
                return AlignUp(offset, maxAlign);
            }
        }

        private static int GetTypeSize(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:   return 1;
                case SpecialType.System_Char:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:  return 2;
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Single:  return 4;
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Double:
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr: return 8;
                default:
                    if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol en)
                        return en.EnumUnderlyingType != null ? GetTypeSize(en.EnumUnderlyingType) : 4;
                    if (type.TypeKind == TypeKind.Struct && type is INamedTypeSymbol named)
                        return ComputeStructSize(named);
                    return -1;
            }
        }

        private static int GetTypeAlign(ITypeSymbol type)
        {
            int size = GetTypeSize(type);
            return size <= 0 ? 1 : (size <= 8 ? size : 8);
        }

        private static int AlignUp(int v, int a) => a <= 1 ? v : (v + a - 1) & ~(a - 1);
    }
}
