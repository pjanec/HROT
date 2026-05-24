using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Fbt.SourceGen
{
    [Generator]
    public class BTreeActionGenerator : IIncrementalGenerator
    {
        // ---- Diagnostic descriptors (BHU-012) ----------------------------------

        private static readonly DiagnosticDescriptor BHU001_TypeMismatch = new DiagnosticDescriptor(
            id: "BHU_001",
            title: "SharedAi parameter type mismatch",
            messageFormat: "Method ''{0}'': ref parameter type ''{1}'' does not match DTO field ''{2}.{3}'' of type ''{4}''",
            category: "BTreeActionGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor BHU002_NonStatic = new DiagnosticDescriptor(
            id: "BHU_002",
            title: "SharedAi method must be static",
            messageFormat: "Method ''{0}'' annotated with [SharedAiCondition] or [SharedAiAction] must be static; skipping",
            category: "BTreeActionGenerator",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor BHU003_UnknownField = new DiagnosticDescriptor(
            id: "BHU_003",
            title: "SharedAi DTO field not found",
            messageFormat: "Method ''{0}'': field ''{1}'' not found on type ''{2}'' or offset cannot be computed",
            category: "BTreeActionGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // ---- Channel kind -> component type (BHU-014) --------------------------
        private const string LocomotionChannelType  = "global::Fdp.Toolkit.Behavior.Components.LocomotionChannel";
        private const string WeaponChannelType      = "global::Fdp.Toolkit.Behavior.Components.WeaponChannel";
        private const string InteractionChannelType = "global::Fdp.Toolkit.Behavior.Components.InteractionChannel";

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

            bool hasActionAttr    = symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "BTreeActionAttribute");
            bool hasConditionAttr = symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "BTreeConditionAttribute");
            bool hasSharedCond         = symbol.GetAttributes().Any(a => IsSharedAiConditionAttr(a));
            bool hasSharedAction        = symbol.GetAttributes().Any(a => IsSharedAiActionAttr(a));
            bool hasSharedHeavy         = symbol.GetAttributes().Any(a => IsSharedAiHeavyActionAttr(a));
            bool hasSharedHeavyCondition = symbol.GetAttributes().Any(a => IsSharedAiHeavyConditionAttr(a));

            if (!hasActionAttr && !hasConditionAttr && !hasSharedCond && !hasSharedAction && !hasSharedHeavy && !hasSharedHeavyCondition) return null;

            // Only generate adapters for publicly accessible methods; private/protected
            // methods (e.g., schema-scanner test fixtures) must not appear in generated code.
            if (symbol.DeclaredAccessibility == Accessibility.Private ||
                symbol.DeclaredAccessibility == Accessibility.Protected ||
                symbol.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
                return null;

            int paramCount = symbol.Parameters.Length;

            if (hasActionAttr || hasConditionAttr)
            {
                if (paramCount == 3)
                {
                    string tvType    = symbol.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    string stateType = symbol.Parameters[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    string tcType    = symbol.Parameters[2].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return new BTreeMethodInfo
                    {
                        MethodName = symbol.Name,
                        FullQualifiedMethodName = symbol.ContainingType.ToDisplayString() + "." + symbol.Name,
                        TContextType = tcType, TValueType = tvType, StateType = stateType,
                        IsReusable = true, IsActionKind = hasActionAttr,
                        WritesChannels = CollectWritesChannels(symbol),
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

            if (hasSharedCond || hasSharedAction || hasSharedHeavy || hasSharedHeavyCondition)
            {
                return new BTreeMethodInfo
                {
                    MethodName = symbol.Name,
                    FullQualifiedMethodName = symbol.ContainingType.ToDisplayString() + "." + symbol.Name,
                    IsSharedAi = true,
                    IsSharedCondition = hasSharedCond,
                    IsActionKind = hasSharedAction || hasSharedHeavy,
                    IsSharedHeavy = hasSharedHeavy,
                    IsSharedHeavyCondition = hasSharedHeavyCondition,
                    Symbol = symbol, WritesChannels = CollectWritesChannels(symbol),
                };
            }
            return null;
        }

        private static bool IsSharedAiConditionAttr(AttributeData a)
            => a.AttributeClass?.ToDisplayString() == "Fbt.Kernel.SharedAiConditionAttribute";
        private static bool IsSharedAiActionAttr(AttributeData a)
            => a.AttributeClass?.ToDisplayString() == "Fbt.Kernel.SharedAiActionAttribute";
        private static bool IsSharedAiHeavyActionAttr(AttributeData a)
            => a.AttributeClass?.ToDisplayString() == "Fbt.Kernel.SharedAiHeavyActionAttribute";
        private static bool IsSharedAiHeavyConditionAttr(AttributeData a)
            => a.AttributeClass?.ToDisplayString() == "Fbt.Kernel.SharedAiHeavyConditionAttribute";
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
            var reusable       = new List<BTreeMethodInfo>();
            var sharedAiMethods = new List<BTreeMethodInfo>();

            foreach (var m in methods)
            {
                if (m == null) continue;
                if (m.IsSharedAi) sharedAiMethods.Add(m);
                else if (m.IsReusable) reusable.Add(m);
                else registrable.Add(m);
            }

            if (registrable.Count == 0 && reusable.Count == 0 && sharedAiMethods.Count == 0) return;

            string namespaceName = (compilation.AssemblyName ?? "Generated") + ".Generated";

            var groups4 = registrable
                .GroupBy(m => m.TBlackboardType + "|" + m.TContextType)
                .ToDictionary(g => g.Key);

            var reusableByCtx = reusable
                .GroupBy(m => m.TContextType)
                .ToDictionary(g => g.Key, g => g.ToList());

            var mergedGroups = new List<GroupEntry>();
            foreach (var kvp in groups4)
            {
                var first = kvp.Value.First();
                string tb = first.TBlackboardType!;
                string tc = first.TContextType!;
                reusableByCtx.TryGetValue(tc, out var bridgeList);
                mergedGroups.Add(new GroupEntry(tb, tc, kvp.Value.ToList(), bridgeList ?? new List<BTreeMethodInfo>()));
            }

            if (sharedAiMethods.Count > 0 && mergedGroups.Count > 0)
            {
                var expanded = ExpandSharedAiEntries(context, compilation, sharedAiMethods);
                AssignSharedAiToGroups(compilation, expanded, mergedGroups);
            }

            if (mergedGroups.Count == 0) return;

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
                if (info.IsActionKind && !info.IsSharedHeavy)
                {
                    foreach (var attr in sym.GetAttributes().Where(IsSharedAiActionAttr))
                    {
                        var e = BuildEntry(context, compilation, sym, attr, isCondition: false, info.WritesChannels);
                        if (e != null) result.Add(e);
                    }
                }
                if (info.IsSharedHeavy)
                {
                    foreach (var attr in sym.GetAttributes().Where(IsSharedAiHeavyActionAttr))
                    {
                        var e = BuildHeavyEntry(context, compilation, sym, attr, info.WritesChannels);
                        if (e != null) result.Add(e);
                    }
                }
                if (info.IsSharedHeavyCondition)
                {
                    foreach (var attr in sym.GetAttributes().Where(IsSharedAiHeavyConditionAttr))
                    {
                        var e = BuildHeavyConditionEntry(context, compilation, sym, attr);
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

            int? offset = TryComputeFieldOffset(dtoTypeSymbol, fieldName!, out var fieldTypeSymbol);
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
                CompoundKey  = sym.ContainingType.ToDisplayString() + "." + sym.Name + "@" + offset.Value,
                IsCondition  = isCondition,
                WritesChannels = writes,
            };
        }

        private static SharedAiEntry? BuildHeavyEntry(
            SourceProductionContext context,
            Compilation compilation,
            IMethodSymbol sym,
            AttributeData attr,
            List<int> writes)
        {
            // Action attribute arg order: arg0=dtoType, arg1=fieldName, arg2=heavyCompType,
            //   3-arg: managed;  5-arg: unmanaged (arg3=heavyFieldName, arg4=heavyDtoType)
            if (attr.ConstructorArguments.Length < 3) return null;
            var dtoTypeSymbol = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
            string? fieldName = attr.ConstructorArguments[1].Value as string;
            var heavyCompSymbol = attr.ConstructorArguments[2].Value as INamedTypeSymbol;
            if (dtoTypeSymbol == null || string.IsNullOrEmpty(fieldName) || heavyCompSymbol == null) return null;

            int? offset = TryComputeFieldOffset(dtoTypeSymbol, fieldName!, out var fieldTypeSymbol);
            if (offset == null || fieldTypeSymbol == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    BHU003_UnknownField, sym.Locations.FirstOrDefault(),
                    sym.Name, fieldName, dtoTypeSymbol.ToDisplayString()));
                return null;
            }

            bool isHeavyManaged = heavyCompSymbol.IsReferenceType;
            string heavyCompFqn = heavyCompSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string? heavyFieldName = null;
            string? heavyDtoFqn = null;

            if (!isHeavyManaged)
            {
                // unmanaged: arg3 = heavyFieldName, arg4 = heavyDtoType
                if (attr.ConstructorArguments.Length < 5) return null;
                heavyFieldName = attr.ConstructorArguments[3].Value as string;
                var heavyDtoSymbol = attr.ConstructorArguments[4].Value as INamedTypeSymbol;
                if (string.IsNullOrEmpty(heavyFieldName) || heavyDtoSymbol == null) return null;
                heavyDtoFqn = heavyDtoSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            return new SharedAiEntry
            {
                MethodName = sym.Name,
                FullQualifiedMethodName = sym.ContainingType.ToDisplayString() + "." + sym.Name,
                FieldTypeFqn = fieldTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Offset       = offset.Value,
                CompoundKey  = sym.ContainingType.ToDisplayString() + "." + sym.Name + "@" + offset.Value,
                IsCondition  = false,
                IsHeavy      = true,
                IsHeavyManaged      = isHeavyManaged,
                HeavyComponentFqn   = heavyCompFqn,
                HeavyFieldName      = heavyFieldName,
                HeavyDtoFqn         = heavyDtoFqn,
                WritesChannels = writes,
            };
        }

        private static SharedAiEntry? BuildHeavyConditionEntry(
            SourceProductionContext context,
            Compilation compilation,
            IMethodSymbol sym,
            AttributeData attr)
        {
            // Condition attribute arg order: arg0=dtoType, arg1=fieldName, arg2=heavyCompType,
            //   arg3=heavyDtoType, arg4=heavyFieldName (optional; present => unmanaged)
            if (attr.ConstructorArguments.Length < 4) return null;
            var dtoTypeSymbol   = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
            string? fieldName   = attr.ConstructorArguments[1].Value as string;
            var heavyCompSymbol = attr.ConstructorArguments[2].Value as INamedTypeSymbol;
            var heavyDtoSymbol  = attr.ConstructorArguments[3].Value as INamedTypeSymbol;
            if (dtoTypeSymbol == null || string.IsNullOrEmpty(fieldName) || heavyCompSymbol == null) return null;

            int? offset = TryComputeFieldOffset(dtoTypeSymbol, fieldName!, out var fieldTypeSymbol);
            if (offset == null || fieldTypeSymbol == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    BHU003_UnknownField, sym.Locations.FirstOrDefault(),
                    sym.Name, fieldName, dtoTypeSymbol.ToDisplayString()));
                return null;
            }

            // heavyFieldName present (arg4) means unmanaged; absence means managed.
            string? heavyFieldName = null;
            string? heavyDtoFqn    = null;
            bool isHeavyManaged    = true;
            string heavyCompFqn    = heavyCompSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (attr.ConstructorArguments.Length >= 5)
            {
                heavyFieldName = attr.ConstructorArguments[4].Value as string;
                if (!string.IsNullOrEmpty(heavyFieldName))
                {
                    // Unmanaged: use the supplied heavyDtoType for the Unsafe.As cast.
                    isHeavyManaged = false;
                    if (heavyDtoSymbol == null) return null;
                    heavyDtoFqn = heavyDtoSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }

            return new SharedAiEntry
            {
                MethodName = sym.Name,
                FullQualifiedMethodName = sym.ContainingType.ToDisplayString() + "." + sym.Name,
                FieldTypeFqn = fieldTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Offset       = offset.Value,
                CompoundKey  = sym.ContainingType.ToDisplayString() + "." + sym.Name + "@" + offset.Value,
                IsCondition  = true,
                IsHeavy      = true,
                IsHeavyManaged    = isHeavyManaged,
                HeavyComponentFqn = heavyCompFqn,
                HeavyFieldName    = heavyFieldName,
                HeavyDtoFqn       = heavyDtoFqn,
                WritesChannels    = new List<int>(),
            };
        }

        // ---- Struct field-offset computation -----------------------------------

        private static int? TryComputeFieldOffset(
            INamedTypeSymbol parentType,
            string fieldName,
            out ITypeSymbol? fieldTypeSymbol)
        {
            fieldTypeSymbol = null;
            var fields = parentType.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => !f.IsStatic && !f.IsConst)
                .ToList();

            bool isExplicit = parentType.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "StructLayoutAttribute"
                       && a.ConstructorArguments.Length > 0
                       && (int?)a.ConstructorArguments[0].Value == 2); // LayoutKind.Explicit = 2

            if (isExplicit)
            {
                var target = fields.FirstOrDefault(f => f.Name == fieldName);
                if (target == null) return null;
                var fa = target.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "FieldOffsetAttribute");
                if (fa == null || fa.ConstructorArguments.Length == 0) return null;
                fieldTypeSymbol = target.Type;
                return (int)fa.ConstructorArguments[0].Value!;
            }

            int offset = 0;
            foreach (var field in fields)
            {
                int size = GetTypeSize(field.Type);
                if (size < 0) return null;
                if (size > 0) offset = AlignUp(offset, GetTypeAlign(field.Type));
                if (field.Name == fieldName) { fieldTypeSymbol = field.Type; return offset; }
                offset += size;
            }
            return null;
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

        private static int ComputeStructSize(INamedTypeSymbol type)
        {
            bool isExplicit = type.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "StructLayoutAttribute"
                       && a.ConstructorArguments.Length > 0
                       && (int?)a.ConstructorArguments[0].Value == 2); // LayoutKind.Explicit = 2

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
                    int size = GetTypeSize(field.Type), align = GetTypeAlign(field.Type);
                    if (size < 0) return -1;
                    if (align > maxAlign) maxAlign = align;
                    if (size > 0) offset = AlignUp(offset, align);
                    offset += size;
                }
                return AlignUp(offset, maxAlign);
            }
        }

        private static int AlignUp(int v, int a) => a <= 1 ? v : (v + a - 1) & ~(a - 1);

        // ---- Assign SharedAi entries to groups ---------------------------------

        private static void AssignSharedAiToGroups(
            Compilation compilation,
            List<SharedAiEntry> entries,
            List<GroupEntry> groups)
        {
            foreach (var entry in entries)
            {
                bool assigned = false;
                foreach (var group in groups)
                {
                    // Use the context type symbol to check for a 'Self' member.
                    string tcName = group.TContextType.Replace("global::", "");
                    var tcSymbol  = compilation.GetTypeByMetadataName(tcName);
                    bool hasSelf  = tcSymbol?.GetMembers("Self").Any() == true;
                    if (hasSelf)
                    {
                        group.SharedAiEntries.Add(entry);
                        assigned = true;
                    }
                }
                if (!assigned)
                {
                    // No suitable group found (none has Self context member).
                    // SharedAi entries require a context with a Self member; the HSM generator
                    // handles these entries on the HSM side when no BTree context claims them.
                }
            }
        }

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
            sb.AppendLine("        // 3-param ReusableDelegate methods are registered as bridge closures");
            sb.AppendLine("        // using Unsafe.As to project the runtime blackboard to TValue.");

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

                foreach (var m in group.Bridges)
                {
                    string stateType = m.StateType ?? "global::Fbt.BehaviorTreeState";
                    string valueType = m.TValueType!;
                    string key       = m.FullQualifiedMethodName + "@0";
                    sb.AppendLine("            registry.Register(\"" + key + "\",");
                    sb.AppendLine("                (ref " + tb + " bb, ref " + stateType + " st, ref " + tc + " ctx, int _) =>");
                    sb.AppendLine("                {");
                    sb.AppendLine("                    ref var p = ref global::System.Runtime.CompilerServices.Unsafe.As<" + tb + ", " + valueType + ">(ref bb);");
                    sb.AppendLine("                    return global::" + m.FullQualifiedMethodName + "(ref p, ref st, ref ctx);");
                    sb.AppendLine("                });");
                }

                foreach (var entry in group.SharedAiEntries)
                    EmitSharedAiAdapter(sb, entry, tb, tc);

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
            EmitChannelClear(sb, m.WritesChannels, "                    ");
            sb.AppendLine("                    return status;");
            sb.AppendLine("                });");
        }

        private static void EmitSharedAiAdapter(StringBuilder sb, SharedAiEntry entry, string tb, string tc)
        {
            if (entry.IsHeavy)
            {
                EmitHeavySharedAiAdapter(sb, entry, tb, tc);
                return;
            }

            if (entry.WritesChannels.Count == 0)
            {
                string regMethod = entry.IsCondition ? "RegisterCondition" : "Register";
                sb.AppendLine("            registry." + regMethod + "(\"" + entry.CompoundKey + "\",");
                sb.AppendLine("                static (ref " + tb + " bb, ref global::Fbt.BehaviorTreeState _, ref " + tc + " ctx, int _) =>");
                sb.AppendLine("                {");
                sb.AppendLine("                    ref var field = ref Unsafe.As<byte, " + entry.FieldTypeFqn + ">(");
                sb.AppendLine("                        ref Unsafe.AddByteOffset(ref Unsafe.As<" + tb + ", byte>(ref bb), (nint)" + entry.Offset + "));");
                if (entry.IsCondition)
                    sb.AppendLine("                    return global::" + entry.FullQualifiedMethodName + "(ref field, ctx.Self, ctx.World) ? global::Fbt.NodeStatus.Success : global::Fbt.NodeStatus.Failure;");
                else
                    sb.AppendLine("                    return global::" + entry.FullQualifiedMethodName + "(ref field, ctx.Self, ctx.World);");
                sb.AppendLine("                });");
            }
            else
            {
                sb.AppendLine("            registry.Register(\"" + entry.CompoundKey + "\",");
                sb.AppendLine("                static (ref " + tb + " bb, ref global::Fbt.BehaviorTreeState st, ref " + tc + " ctx, int pi) =>");
                sb.AppendLine("                {");
                sb.AppendLine("                    ref var field = ref Unsafe.As<byte, " + entry.FieldTypeFqn + ">(");
                sb.AppendLine("                        ref Unsafe.AddByteOffset(ref Unsafe.As<" + tb + ", byte>(ref bb), (nint)" + entry.Offset + "));");
                sb.AppendLine("                    var status = global::" + entry.FullQualifiedMethodName + "(ref field, ctx.Self, ctx.World);");
                EmitChannelClear(sb, entry.WritesChannels, "                    ");
                sb.AppendLine("                    return status;");
                sb.AppendLine("                });");
            }
        }

        private static void EmitHeavySharedAiAdapter(StringBuilder sb, SharedAiEntry entry, string tb, string tc)
        {
            bool isCondition = entry.IsCondition;
            bool hasChannels = entry.WritesChannels.Count > 0;
            string regMethod = isCondition ? "RegisterCondition" : "Register";
            sb.AppendLine("            registry." + regMethod + "(\"" + entry.CompoundKey + "\",");
            sb.AppendLine("                static (ref " + tb + " bb, ref global::Fbt.BehaviorTreeState st, ref " + tc + " ctx, int pi) =>");
            sb.AppendLine("                {");
            sb.AppendLine("                    ref var field = ref Unsafe.As<byte, " + entry.FieldTypeFqn + ">(");
            sb.AppendLine("                        ref Unsafe.AddByteOffset(ref Unsafe.As<" + tb + ", byte>(ref bb), (nint)" + entry.Offset + "));");
            if (entry.IsHeavyManaged)
            {
                // Managed component: fetch via GetComponent<T> (returns the class instance)
                sb.AppendLine("                    var heavy = ctx.World.GetComponent<" + entry.HeavyComponentFqn + ">(ctx.Self);");
                if (isCondition)
                    sb.AppendLine("                    return global::" + entry.FullQualifiedMethodName + "(ref field, heavy, ctx.Self, ctx.World) ? global::Fbt.NodeStatus.Success : global::Fbt.NodeStatus.Failure;");
                else
                    sb.AppendLine("                    var status = global::" + entry.FullQualifiedMethodName + "(ref field, heavy, ctx.Self, ctx.World);");
            }
            else
            {
                // Unmanaged component: fetch via GetComponentRW<T>, then project bytes via Unsafe.As.
                // Assumption: the heavy DTO starts at offset 0 of the component (true for Blackboard1024.Memory).
                sb.AppendLine("                    ref var heavyComp = ref ctx.World.GetComponentRW<" + entry.HeavyComponentFqn + ">(ctx.Self);");
                sb.AppendLine("                    ref var heavy = ref Unsafe.As<byte, " + entry.HeavyDtoFqn + ">(");
                sb.AppendLine("                        ref Unsafe.AddByteOffset(ref Unsafe.As<" + entry.HeavyComponentFqn + ", byte>(ref heavyComp), (nint)0));");
                if (isCondition)
                    sb.AppendLine("                    return global::" + entry.FullQualifiedMethodName + "(ref field, ref heavy, ctx.Self, ctx.World) ? global::Fbt.NodeStatus.Success : global::Fbt.NodeStatus.Failure;");
                else
                    sb.AppendLine("                    var status = global::" + entry.FullQualifiedMethodName + "(ref field, ref heavy, ctx.Self, ctx.World);");
            }
            if (!isCondition)
            {
                if (hasChannels) EmitChannelClear(sb, entry.WritesChannels, "                    ");
                sb.AppendLine("                    return status;");
            }
            sb.AppendLine("                });");
        }

        private static void EmitChannelClear(StringBuilder sb, List<int> channels, string indent)
        {
            sb.AppendLine(indent + "if (status == global::Fbt.NodeStatus.Failure)");
            sb.AppendLine(indent + "{");
            foreach (int kind in channels)
            {
                string? ct = ChannelKindToType(kind);
                if (ct == null) continue;
                sb.AppendLine(indent + "    ref var ch" + kind + " = ref ctx.World.GetComponentRW<" + ct + ">(ctx.Self);");
                sb.AppendLine(indent + "    ch" + kind + ".ActiveAction     = 0;");
                sb.AppendLine(indent + "    ch" + kind + ".ActionInstanceId = unchecked(ch" + kind + ".ActionInstanceId + 1u);");
            }
            sb.AppendLine(indent + "}");
        }

        private static string? ChannelKindToType(int kind) => kind switch
        {
            0 => LocomotionChannelType,
            1 => WeaponChannelType,
            2 => InteractionChannelType,
            _ => null,
        };

        // ---- FNV-1a hash (identical to HsmActionGenerator) ---------------------
        private static ushort ComputeHash(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name) { hash ^= c; hash *= 16777619; }
            return (ushort)(hash & 0xFFFF);
        }
    }

    // ---- Data types ------------------------------------------------------------

    internal class BTreeMethodInfo
    {
        public string MethodName { get; set; } = "";
        public string FullQualifiedMethodName { get; set; } = "";
        public string? TBlackboardType { get; set; }
        public string? TContextType { get; set; }
        public string? TValueType { get; set; }
        public string? StateType { get; set; }
        public bool IsReusable { get; set; }
        public bool IsActionKind { get; set; }
        public bool IsSharedAi { get; set; }
        public bool IsSharedCondition { get; set; }
        public bool IsSharedHeavy { get; set; }        public bool IsSharedHeavyCondition { get; set; }        public IMethodSymbol? Symbol { get; set; }
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
        // Heavy-action fields (populated only when IsHeavy == true)
        public bool IsHeavy { get; set; }
        public bool IsHeavyManaged { get; set; }
        public string? HeavyComponentFqn { get; set; }
        public string? HeavyFieldName { get; set; }
        public string? HeavyDtoFqn { get; set; }
        public List<int> WritesChannels { get; set; } = new List<int>();
    }

    internal class GroupEntry
    {
        public string TBlackboardType { get; }
        public string TContextType { get; }
        public List<BTreeMethodInfo> Direct { get; }
        public List<BTreeMethodInfo> Bridges { get; }
        public List<SharedAiEntry> SharedAiEntries { get; } = new List<SharedAiEntry>();

        public GroupEntry(string tb, string tc, List<BTreeMethodInfo> direct, List<BTreeMethodInfo> bridges)
        {
            TBlackboardType = tb; TContextType = tc;
            Direct = direct; Bridges = bridges;
        }
    }
}
