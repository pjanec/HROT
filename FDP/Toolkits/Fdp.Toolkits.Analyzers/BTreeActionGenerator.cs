using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Fdp.Toolkit.Behavior.Shared;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    [Generator]
    public class BTreeActionGenerator : IIncrementalGenerator
    {
        // ---- Diagnostic descriptors (BHU-012) ----------------------------------

        private static readonly DiagnosticDescriptor BHU001_TypeMismatch  = SharedBhuDiagnostics.BHU001_TypeMismatch;
        private static readonly DiagnosticDescriptor BHU002_NonStatic     = SharedBhuDiagnostics.BHU002_NonStatic;
        private static readonly DiagnosticDescriptor BHU003_UnknownField  = SharedBhuDiagnostics.BHU003_UnknownField;
        private static readonly DiagnosticDescriptor BHU016_DeactivatorMissingTarget = SharedBhuDiagnostics.BHU016_DeactivatorMissingTarget;
        private static readonly DiagnosticDescriptor BHU017_DeactivatorUnknownTarget = SharedBhuDiagnostics.BHU017_DeactivatorUnknownTarget;
        private static readonly DiagnosticDescriptor BHU022_RetiredReusableForm = SharedBhuDiagnostics.BHU022_RetiredReusableForm;

        // ---- Channel kind -> component type (BHU-014) --------------------------

        // ---- Initialize --------------------------------------------------------

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetMethodInfo(ctx))
                .Where(static m => m != null);

            var compilationAndMethods = context.CompilationProvider.Combine(candidateMethods.Collect());

            context.RegisterSourceOutput(
                compilationAndMethods,
                static (spc, source) => Execute(spc, source.Left, source.Right!));
        }

        // ---- Collect method information ----------------------------------------

        private static BTreeMethodInfo? GetMethodInfo(GeneratorSyntaxContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;

            if (symbol == null) return null;

            // Detect [BTreeDeactivatorAttribute] before the general attribute checks.
            var deactivatorAttr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "BTreeDeactivatorAttribute");
            if (deactivatorAttr != null)
            {
                string target = deactivatorAttr.ConstructorArguments.Length > 0
                    ? deactivatorAttr.ConstructorArguments[0].Value?.ToString() ?? string.Empty
                    : string.Empty;
                if (symbol.Parameters.Length != 4) return null;
                string tbType = symbol.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string tcType = symbol.Parameters[2].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return new BTreeMethodInfo
                {
                    MethodName = symbol.Name,
                    FullQualifiedMethodName = symbol.ContainingType.ToDisplayString() + "." + symbol.Name,
                    TBlackboardType = tbType,
                    TContextType = tcType,
                    IsDeactivator = true,
                    TargetAction = target,
                };
            }

            bool hasActionAttr    = symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "BTreeActionAttribute");
            bool hasConditionAttr = symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "BTreeConditionAttribute");
            bool hasSharedCond         = symbol.GetAttributes().Any(a => IsSharedAiConditionAttr(a));
            bool hasSharedAction        = symbol.GetAttributes().Any(a => IsSharedAiActionAttr(a));

            if (!hasActionAttr && !hasConditionAttr && !hasSharedCond && !hasSharedAction) return null;

            // Only generate adapters for publicly accessible methods; private/protected
            // methods (e.g., schema-scanner test fixtures) must not appear in generated code.
            if (symbol.DeclaredAccessibility == Accessibility.Private ||
                symbol.DeclaredAccessibility == Accessibility.Protected ||
                symbol.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
                return null;

            int paramCount = symbol.Parameters.Length;

            if (hasActionAttr || hasConditionAttr)
            {
                // ⛔ CE-504 slice 4 — the 3-param (ref P, ref BehaviorTreeState, ref TCtx) form is retired; it is reported
                //   (BHU_022) instead of being adapted. Its "@0" bridge adapter is gone with it.
                if (paramCount == 3)
                {
                    return new BTreeMethodInfo
                    {
                        MethodName = symbol.Name,
                        FullQualifiedMethodName = symbol.ContainingType.ToDisplayString() + "." + symbol.Name,
                        IsReusable = true, Symbol = symbol,
                    };
                }
                if (paramCount != 4) return null;
                string tbType  = symbol.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string tcType4 = symbol.Parameters[2].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return new BTreeMethodInfo
                {
                    MethodName = symbol.Name,
                    FullQualifiedMethodName = symbol.ContainingType.ToDisplayString() + "." + symbol.Name,
                    TBlackboardType = tbType, TContextType = tcType4,
                    IsReusable = false, IsActionKind = hasActionAttr,
                    WritesChannels = CollectWritesChannels(symbol),
                };
            }

            if (hasSharedCond || hasSharedAction)
            {
                return new BTreeMethodInfo
                {
                    MethodName = symbol.Name,
                    FullQualifiedMethodName = symbol.ContainingType.ToDisplayString() + "." + symbol.Name,
                    IsSharedAi = true,
                    IsSharedCondition = hasSharedCond,
                    IsActionKind = hasSharedAction,
                    Symbol = symbol, WritesChannels = CollectWritesChannels(symbol),
                };
            }
            return null;
        }

        private static bool IsSharedAiConditionAttr(AttributeData a)
            => a.AttributeClass?.ToDisplayString() == "Fbt.Kernel.SharedAiConditionAttribute";
        private static bool IsSharedAiActionAttr(AttributeData a)
            => a.AttributeClass?.ToDisplayString() == "Fbt.Kernel.SharedAiActionAttribute";
        private static bool IsWritesChannelAttr(AttributeData a)
            => a.AttributeClass?.ToDisplayString() == "Fbt.Kernel.WritesChannelAttribute";

        private static List<int> CollectWritesChannels(IMethodSymbol symbol)
        {
            var result = new List<int>();
            foreach (var attr in symbol.GetAttributes())
            {
                if (!IsWritesChannelAttr(attr) || attr.ConstructorArguments.Length == 0) continue;
                if (attr.ConstructorArguments[0].Value is int i) result.Add(i);
            }
            return result;
        }

        // ---- Execute -----------------------------------------------------------

        private static void Execute(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<BTreeMethodInfo> methods)
        {
            var registrable    = new List<BTreeMethodInfo>();
            var sharedAiMethods = new List<BTreeMethodInfo>();
            var deactivators   = new List<BTreeMethodInfo>();

            foreach (var m in methods)
            {
                if (m == null) continue;
                if (m.IsDeactivator) { deactivators.Add(m); continue; }
                if (m.IsSharedAi) sharedAiMethods.Add(m);
                else if (m.IsReusable)
                    context.ReportDiagnostic(Diagnostic.Create(
                        BHU022_RetiredReusableForm, m.Symbol?.Locations.FirstOrDefault(), m.FullQualifiedMethodName));
                else registrable.Add(m);
            }

            if (registrable.Count == 0 && sharedAiMethods.Count == 0) return;

            string namespaceName = (compilation.AssemblyName ?? "Generated") + ".Generated";

            var groups4 = registrable
                .GroupBy(m => m.TBlackboardType + "|" + m.TContextType)
                .ToDictionary(g => g.Key);

            var mergedGroups = new List<GroupEntry>();
            foreach (var kvp in groups4)
            {
                var first = kvp.Value.First();
                string tb = first.TBlackboardType!;
                string tc = first.TContextType!;
                mergedGroups.Add(new GroupEntry(tb, tc, kvp.Value.ToList()));
            }

            // ⭐ CE-417: the [SharedAi*] methods are still VALIDATED here (BHU001/002/003) — the asset bridge relies on the
            //   attribute being well-formed — but no longer emitted (see GenerateRegistrar).
            if (sharedAiMethods.Count > 0)
                ExpandSharedAiEntries(context, compilation, sharedAiMethods);

            if (mergedGroups.Count == 0) return;

            // Validate deactivators and assign to matching groups.
            foreach (var d in deactivators)
            {
                if (string.IsNullOrEmpty(d.TargetAction))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        BHU016_DeactivatorMissingTarget, null, d.MethodName));
                    continue;
                }

                var group = mergedGroups.FirstOrDefault(
                    g => g.TBlackboardType == d.TBlackboardType && g.TContextType == d.TContextType);
                if (group == null) continue;

                bool knownAction = group.Direct.Any(a => a.FullQualifiedMethodName == d.TargetAction);
                if (!knownAction)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        BHU017_DeactivatorUnknownTarget, null, d.MethodName, d.TargetAction));
                    continue;
                }

                group.Deactivators.Add(d);
            }

            context.AddSource("FbtActionRegistrar.g.cs", GenerateRegistrar(mergedGroups, namespaceName));
        }

        // ---- SharedAi expansion -----------------------------------------------

        private static List<SharedAiEntry> ExpandSharedAiEntries(
            SourceProductionContext context,
            Compilation compilation,
            List<BTreeMethodInfo> sharedAiMethods)
        {
            var result = new List<SharedAiEntry>();
            foreach (var info in sharedAiMethods)
            {
                var sym = info.Symbol!;
                if (!sym.IsStatic)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        BHU002_NonStatic, sym.Locations.FirstOrDefault(), sym.Name));
                    continue;
                }
                if (info.IsSharedCondition)
                {
                    foreach (var attr in sym.GetAttributes().Where(IsSharedAiConditionAttr))
                    {
                        var e = BuildEntry(context, compilation, sym, attr, isCondition: true, info.WritesChannels);
                        if (e != null) result.Add(e);
                    }
                }
                if (info.IsActionKind)
                {
                    foreach (var attr in sym.GetAttributes().Where(IsSharedAiActionAttr))
                    {
                        var e = BuildEntry(context, compilation, sym, attr, isCondition: false, info.WritesChannels);
                        if (e != null) result.Add(e);
                    }
                }
            }
            return result;
        }

        private static SharedAiEntry? BuildEntry(
            SourceProductionContext context,
            Compilation compilation,
            IMethodSymbol sym,
            AttributeData attr,
            bool isCondition,
            List<int> writes)
        {
            if (attr.ConstructorArguments.Length < 2) return null;
            var dtoTypeSymbol = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
            string? fieldName = attr.ConstructorArguments[1].Value as string;
            if (dtoTypeSymbol == null || string.IsNullOrEmpty(fieldName)) return null;

            int? offset = RoslynStructLayout.FieldOffset(dtoTypeSymbol, fieldName!, out var fieldTypeSymbol);
            if (offset == null || fieldTypeSymbol == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    BHU003_UnknownField, sym.Locations.FirstOrDefault(),
                    sym.Name, fieldName, dtoTypeSymbol.ToDisplayString()));
                return null;
            }

            if (sym.Parameters.Length > 0 && sym.Parameters[0].RefKind == RefKind.Ref)
            {
                if (!SymbolEqualityComparer.Default.Equals(sym.Parameters[0].Type, fieldTypeSymbol))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        BHU001_TypeMismatch, sym.Locations.FirstOrDefault(),
                        sym.Name, sym.Parameters[0].Type.ToDisplayString(),
                        dtoTypeSymbol.ToDisplayString(), fieldName,
                        fieldTypeSymbol.ToDisplayString()));
                    return null;
                }
            }

            return new SharedAiEntry
            {
                MethodName = sym.Name,
                FullQualifiedMethodName = sym.ContainingType.ToDisplayString() + "." + sym.Name,
                FieldTypeFqn = fieldTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Offset       = offset.Value,
                CompoundKey  = HsmActionKey.CompoundKeyName(sym.ContainingType.ToDisplayString() + "." + sym.Name, offset.Value),
                IsCondition  = isCondition,
                WritesChannels = writes,
            };
        }

        // ---- Struct field-offset computation: the ONE algorithm, Shared/RoslynStructLayout.cs (CE-2027) ----

        // ---- Code generation ---------------------------------------------------

        private static string GenerateRegistrar(List<GroupEntry> groups, string namespaceName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Compound-key convention: \"{FullyQualifiedMethodName}@{byteOffset}\"");
            sb.AppendLine("// The offset is the byte offset of the DTO field within the blackboard.");
            sb.AppendLine();
            sb.AppendLine("using global::System.Runtime.CompilerServices;");
            sb.AppendLine();
            sb.AppendLine("namespace " + namespaceName);
            sb.AppendLine("{");
            sb.AppendLine("    [global::Fbt.FbtRegistrar]");
            sb.AppendLine("    public static class FbtActionRegistrar");
            sb.AppendLine("    {");
            sb.AppendLine("        // 4-param NodeLogicDelegate methods are registered directly.");

            foreach (var group in groups)
            {
                string tb = group.TBlackboardType, tc = group.TContextType;
                sb.AppendLine();
                sb.AppendLine("        public static void RegisterAll(");
                sb.AppendLine("            global::Fbt.Runtime.ActionRegistry<" + tb + ", " + tc + "> registry)");
                sb.AppendLine("        {");

                foreach (var m in group.Direct)
                {
                    if (m.WritesChannels.Count == 0)
                        sb.AppendLine("            registry.Register(\"" + m.FullQualifiedMethodName + "\", global::" + m.FullQualifiedMethodName + ");");
                    else
                        EmitWrapped4Param(sb, m, tb, tc);
                }

                // ⛔ CE-504 slice 4 — the 3-param [BTreeAction]/[BTreeCondition] "@0" bridge adapters are retired (BHU_022):
                //   a binding calls the shared C# form, emitted per binding by the asset bridge or curried by SharedNodeBinder.

                // ⛔⛔ CE-417 B-2 (a′) — no per-METHOD [SharedAi*] adapters any more: they were keyed by the attribute DTO's
                //   field offset, so an asset's binding (Fqn@hostOffset) reached one only when the offsets agreed (F7/F8),
                //   and the asset's own call registered the same key with the wrong signature. ⭐ The BTree ASSET's bridge now
                //   emits the [SharedAi*] call per binding (BTreeBridgeEmitCore), with the channel release (ChannelClearEmit).

                foreach (var m in group.Deactivators)
                    sb.AppendLine("            registry.RegisterDeactivator(\"" + m.TargetAction + "\", global::" + m.FullQualifiedMethodName + ");");

                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void EmitWrapped4Param(StringBuilder sb, BTreeMethodInfo m, string tb, string tc)
        {
            sb.AppendLine("            registry.Register(\"" + m.FullQualifiedMethodName + "\",");
            sb.AppendLine("                static (ref " + tb + " bb, ref global::Fbt.BehaviorTreeState st, ref " + tc + " ctx, int pi) =>");
            sb.AppendLine("                {");
            sb.AppendLine("                    var status = global::" + m.FullQualifiedMethodName + "(ref bb, ref st, ref ctx, pi);");
            Fdp.Toolkit.Behavior.Shared.ChannelClearEmit.Emit(sb, m.WritesChannels, "                    ");
            sb.AppendLine("                    return status;");
            sb.AppendLine("                });");
        }

    }

    // ---- Data types ------------------------------------------------------------

    internal class BTreeMethodInfo
    {
        public string MethodName { get; set; } = "";
        public string FullQualifiedMethodName { get; set; } = "";
        public string? TBlackboardType { get; set; }
        public string? TContextType { get; set; }
        public bool IsReusable { get; set; }
        public bool IsActionKind { get; set; }
        public bool IsSharedAi { get; set; }
        public bool IsSharedCondition { get; set; }
        public bool IsDeactivator { get; set; }
        public string TargetAction { get; set; } = string.Empty;
        public IMethodSymbol? Symbol { get; set; }
        public List<int> WritesChannels { get; set; } = new List<int>();
    }

    internal class SharedAiEntry
    {
        public string MethodName { get; set; } = "";
        public string FullQualifiedMethodName { get; set; } = "";
        public string FieldTypeFqn { get; set; } = "";
        public int Offset { get; set; }
        public string CompoundKey { get; set; } = "";
        public bool IsCondition { get; set; }
        public List<int> WritesChannels { get; set; } = new List<int>();
    }

    internal class GroupEntry
    {
        public string TBlackboardType { get; }
        public string TContextType { get; }
        public List<BTreeMethodInfo> Direct { get; }
        public List<BTreeMethodInfo> Deactivators { get; } = new List<BTreeMethodInfo>();

        public GroupEntry(string tb, string tc, List<BTreeMethodInfo> direct)
        {
            TBlackboardType = tb; TContextType = tc;
            Direct = direct;
        }
    }
}
