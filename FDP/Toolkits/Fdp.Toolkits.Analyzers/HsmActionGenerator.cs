using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    [Generator]
    public class HsmActionGenerator : IIncrementalGenerator
    {
        // ---- Diagnostic descriptors (BHU-013) ----------------------------------

        private static readonly DiagnosticDescriptor BHU001_TypeMismatch  = SharedBhuDiagnostics.BHU001_TypeMismatch;
        private static readonly DiagnosticDescriptor BHU002_NonStatic     = SharedBhuDiagnostics.BHU002_NonStatic;
        private static readonly DiagnosticDescriptor BHU003_UnknownField  = SharedBhuDiagnostics.BHU003_UnknownField;

        // ---- Channel kind -> fully-qualified component type --------------------
        private const string LocomotionChannelFqn  = "global::Fdp.Toolkit.Behavior.Components.LocomotionChannel";
        private const string WeaponChannelFqn      = "global::Fdp.Toolkit.Behavior.Components.WeaponChannel";
        private const string InteractionChannelFqn = "global::Fdp.Toolkit.Behavior.Components.InteractionChannel";

        // ---- Initialize --------------------------------------------------------

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (s, _) => s is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetMethodInfo(ctx))
                .Where(static m => m != null);

            var compilationAndMethods = context.CompilationProvider.Combine(candidateMethods.Collect());

            context.RegisterSourceOutput(
                compilationAndMethods,
                static (spc, source) => Execute(spc, source.Left, source.Right!));
        }

        // ---- Collect method information ----------------------------------------

        private static MethodInfo? GetMethodInfo(GeneratorSyntaxContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            if (symbol == null) return null;

            var actionAttr    = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "HsmActionAttribute");
            var guardAttr     = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "HsmGuardAttribute");
            bool hasSharedCond         = symbol.GetAttributes().Any(IsSharedAiConditionAttr);
            bool hasSharedAction        = symbol.GetAttributes().Any(IsSharedAiActionAttr);
            bool hasSharedHeavy         = symbol.GetAttributes().Any(IsSharedAiHeavyActionAttr);
            bool hasSharedHeavyCondition = symbol.GetAttributes().Any(IsSharedAiHeavyConditionAttr);

            if (actionAttr == null && guardAttr == null && !hasSharedCond && !hasSharedAction && !hasSharedHeavy && !hasSharedHeavyCondition) return null;

            // Only generate adapters for publicly accessible methods; private/protected
            // methods (e.g., test fixtures) must not appear in generated code.
            if (symbol.DeclaredAccessibility == Accessibility.Private ||
                symbol.DeclaredAccessibility == Accessibility.Protected ||
                symbol.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
                return null;

            if (actionAttr != null || guardAttr != null)
            {
                bool isGuard = guardAttr != null;
                var  attr    = isGuard ? guardAttr : actionAttr;
                string name  = symbol.Name;
                var nameArg  = attr!.NamedArguments.FirstOrDefault(a => a.Key == "Name");
                if (nameArg.Value.Value is string customName && !string.IsNullOrEmpty(customName))
                    name = customName;

                return new MethodInfo
                {
                    Name         = name,
                    FullName     = symbol.ContainingType.ToDisplayString() + "." + symbol.Name,
                    IsGuard      = isGuard,
                    IsStatic     = symbol.IsStatic,
                    WritesChannels = CollectWritesChannels(symbol),
                };
            }

            if (hasSharedCond || hasSharedAction || hasSharedHeavy || hasSharedHeavyCondition)
            {
                return new MethodInfo
                {
                    Name         = symbol.Name,
                    FullName     = symbol.ContainingType.ToDisplayString() + "." + symbol.Name,
                    IsSharedAi   = true,
                    IsSharedCondition     = hasSharedCond,
                    IsSharedAction        = hasSharedAction || hasSharedHeavy,
                    IsSharedHeavy         = hasSharedHeavy,
                    IsSharedHeavyCondition = hasSharedHeavyCondition,
                    IsStatic     = symbol.IsStatic,
                    Symbol       = symbol,
                    WritesChannels = CollectWritesChannels(symbol),
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
            ImmutableArray<MethodInfo> allMethods)
        {
            var actions    = new List<MethodInfo>();
            var guards     = new List<MethodInfo>();
            var sharedAiMethods = new List<MethodInfo>();

            foreach (var m in allMethods)
            {
                if (m == null) continue;
                if (m.IsSharedAi) sharedAiMethods.Add(m);
                else if (m.IsGuard) guards.Add(m);
                else actions.Add(m);
            }

            string assemblyName = compilation.AssemblyName ?? "Fhsm.Kernel";
            bool isKernel       = assemblyName == "Fhsm.Kernel";

            var sharedAiEntries = ExpandSharedAiEntries(context, compilation, sharedAiMethods);

            if (isKernel)
            {
                var source = GenerateKernelDispatcher(actions, guards, "Fhsm.Kernel");
                context.AddSource("HsmActionDispatcher.g.cs", source);
            }
            else
            {
                string namespaceName = assemblyName + ".Generated";
                var source = GenerateRegistrar(actions, guards, sharedAiEntries, namespaceName);
                context.AddSource("HsmActionRegistrar.g.cs", source);
            }
        }

        // ---- SharedAi expansion -----------------------------------------------

        private static List<SharedAiEntry> ExpandSharedAiEntries(
            SourceProductionContext context,
            Compilation compilation,
            List<MethodInfo> sharedAiMethods)
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
                        var e = BuildEntry(context, sym, attr, isCondition: true, info.WritesChannels);
                        if (e != null) result.Add(e);
                    }
                }
                if (info.IsSharedAction && !info.IsSharedHeavy)
                {
                    foreach (var attr in sym.GetAttributes().Where(IsSharedAiActionAttr))
                    {
                        var e = BuildEntry(context, sym, attr, isCondition: false, info.WritesChannels);
                        if (e != null) result.Add(e);
                    }
                }
                if (info.IsSharedHeavy)
                {
                    foreach (var attr in sym.GetAttributes().Where(IsSharedAiHeavyActionAttr))
                    {
                        var e = BuildHeavyEntry(context, sym, attr, info.WritesChannels);
                        if (e != null) result.Add(e);
                    }
                }
                if (info.IsSharedHeavyCondition)
                {
                    foreach (var attr in sym.GetAttributes().Where(IsSharedAiHeavyConditionAttr))
                    {
                        var e = BuildHeavyConditionEntry(context, sym, attr);
                        if (e != null) result.Add(e);
                    }
                }
            }
            return result;
        }

        private static SharedAiEntry? BuildEntry(
            SourceProductionContext context,
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
                MethodName   = sym.Name,
                FullName     = sym.ContainingType.ToDisplayString() + "." + sym.Name,
                FieldTypeFqn = fieldTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Offset       = offset.Value,
                CompoundKey  = sym.Name + "@" + offset.Value,
                IsCondition  = isCondition,
                WritesChannels = writes,
            };
        }

        private static SharedAiEntry? BuildHeavyEntry(
            SourceProductionContext context,
            IMethodSymbol sym,
            AttributeData attr,
            List<int> writes)
        {
            if (attr.ConstructorArguments.Length < 3) return null;
            var dtoTypeSymbol   = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
            string? fieldName   = attr.ConstructorArguments[1].Value as string;
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
                if (attr.ConstructorArguments.Length < 5) return null;
                heavyFieldName = attr.ConstructorArguments[3].Value as string;
                var heavyDtoSymbol = attr.ConstructorArguments[4].Value as INamedTypeSymbol;
                if (string.IsNullOrEmpty(heavyFieldName) || heavyDtoSymbol == null) return null;
                heavyDtoFqn = heavyDtoSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            return new SharedAiEntry
            {
                MethodName   = sym.Name,
                FullName     = sym.ContainingType.ToDisplayString() + "." + sym.Name,
                FieldTypeFqn = fieldTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Offset       = offset.Value,
                CompoundKey  = sym.Name + "@" + offset.Value,
                IsCondition  = false,
                IsHeavy      = true,
                IsHeavyManaged    = isHeavyManaged,
                HeavyComponentFqn = heavyCompFqn,
                HeavyFieldName    = heavyFieldName,
                HeavyDtoFqn       = heavyDtoFqn,
                WritesChannels = writes,
            };
        }

        private static SharedAiEntry? BuildHeavyConditionEntry(
            SourceProductionContext context,
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

            // heavyFieldName (arg4) present => unmanaged; absent => managed.
            string? heavyFieldName = null;
            string? heavyDtoFqn    = null;
            bool isHeavyManaged    = true;
            string heavyCompFqn    = heavyCompSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (attr.ConstructorArguments.Length >= 5)
            {
                heavyFieldName = attr.ConstructorArguments[4].Value as string;
                if (!string.IsNullOrEmpty(heavyFieldName))
                {
                    isHeavyManaged = false;
                    if (heavyDtoSymbol == null) return null;
                    heavyDtoFqn = heavyDtoSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }

            return new SharedAiEntry
            {
                MethodName   = sym.Name,
                FullName     = sym.ContainingType.ToDisplayString() + "." + sym.Name,
                FieldTypeFqn = fieldTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Offset       = offset.Value,
                CompoundKey  = sym.Name + "@" + offset.Value,
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

        // ---- Code generation (kernel dispatcher) --------------------------------

        private static string GenerateKernelDispatcher(
            List<MethodInfo> actions,
            List<MethodInfo> guards,
            string namespaceName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Fhsm.Kernel.Data;");
            sb.AppendLine();
            sb.AppendLine("namespace " + namespaceName);
            sb.AppendLine("{");
            sb.AppendLine("    public static unsafe class HsmActionDispatcher");
            sb.AppendLine("    {");

            // Action table
            sb.AppendLine("        private static readonly Dictionary<ushort, IntPtr> ActionTable = new()");
            sb.AppendLine("        {");
            foreach (var action in actions)
            {
                ushort id = ComputeHash(action.Name);
                sb.AppendLine("            { " + id + ", (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&" + action.FullName + " },");
            }
            sb.AppendLine("        };");
            sb.AppendLine();

            // Guard table
            sb.AppendLine("        private static readonly Dictionary<ushort, IntPtr> GuardTable = new()");
            sb.AppendLine("        {");
            foreach (var guard in guards)
            {
                ushort id = ComputeHash(guard.Name);
                sb.AppendLine("            { " + id + ", (IntPtr)(delegate* <void*, void*, ushort, bool>)&" + guard.FullName + " },");
            }
            sb.AppendLine("        };");
            sb.AppendLine();

            // Dispatch
            sb.AppendLine("        public static void ExecuteAction(ushort actionId, void* instance, void* context, HsmCommandWriter* writer)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (ActionTable.TryGetValue(actionId, out var actionPtr))");
            sb.AppendLine("                ((delegate* <void*, void*, HsmCommandWriter*, void>)actionPtr)(instance, context, writer);");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public static bool EvaluateGuard(ushort guardId, void* instance, void* context, ushort eventId)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (GuardTable.TryGetValue(guardId, out var guardPtr))");
            sb.AppendLine("                return ((delegate* <void*, void*, ushort, bool>)guardPtr)(instance, context, eventId);");
            sb.AppendLine("            return true; // No guard = always pass");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Registration hooks (called by user registrar for SharedAi/ExitCleanup entries)
            sb.AppendLine("        public static void RegisterAction(ushort id, IntPtr action) => ActionTable[id] = action;");
            sb.AppendLine("        public static void RegisterGuard(ushort id, IntPtr guard) => GuardTable[id] = guard;");
            sb.AppendLine();

            // ClearAll: purge all stale function-pointer entries before re-registration on hot reload.
            sb.AppendLine("        public static void ClearAll()");
            sb.AppendLine("        {");
            sb.AppendLine("            ActionTable.Clear();");
            sb.AppendLine("            GuardTable.Clear();");
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ---- Code generation (user registrar) ----------------------------------

        private static string GenerateRegistrar(
            List<MethodInfo> actions,
            List<MethodInfo> guards,
            List<SharedAiEntry> sharedAiEntries,
            string namespaceName)
        {
            // Collect exit-cleanup entries: one per unique method name that has [WritesChannel].
            // Covers both regular [HsmAction] and [SharedAiAction] methods.
            var exitCleanupMethods = actions
                .Where(a => a.WritesChannels.Count > 0)
                .GroupBy(a => a.Name)
                .Select(g => g.First())
                .ToList();

            // Also add SharedAiAction methods that write channels (deduplicated by method name)
            var sharedAiExitCleanups = sharedAiEntries
                .Where(e => !e.IsCondition && e.WritesChannels.Count > 0)
                .GroupBy(e => e.MethodName)
                .Select(g => g.First())
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Compound-key convention: \"{MethodName}@{byteOffset}\"");
            sb.AppendLine("// The offset is the byte offset of the DTO field within BrainBlackboard.");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine("using Fhsm.Kernel;");
            sb.AppendLine("using Fhsm.Kernel.Data;");
            sb.AppendLine();
            sb.AppendLine("namespace " + namespaceName);
            sb.AppendLine("{");
            sb.AppendLine("    public static unsafe class HsmActionRegistrar");
            sb.AppendLine("    {");

            // --- Private thunks for SharedAi entries ---
            foreach (var entry in sharedAiEntries)
            {
                if (entry.IsCondition)
                    EmitSharedAiGuardThunk(sb, entry);
                else
                    EmitSharedAiActionThunk(sb, entry);
            }

            // --- ExitCleanup thunks for regular [HsmAction] with [WritesChannel] ---
            foreach (var m in exitCleanupMethods)
                EmitExitCleanupThunk(sb, m.Name, m.WritesChannels);

            // --- ExitCleanup thunks for [SharedAiAction] with [WritesChannel] ---
            foreach (var entry in sharedAiExitCleanups)
                EmitExitCleanupThunk(sb, entry.MethodName, entry.WritesChannels);

            // --- RegisterAll ---
            sb.AppendLine("        public static void RegisterAll()");
            sb.AppendLine("        {");

            foreach (var action in actions)
            {
                ushort id = ComputeHash(action.Name);
                sb.AppendLine("            HsmActionDispatcher.RegisterAction(" + id + ", (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&" + action.FullName + ");");
            }

            foreach (var guard in guards)
            {
                ushort id = ComputeHash(guard.Name);
                sb.AppendLine("            HsmActionDispatcher.RegisterGuard(" + id + ", (IntPtr)(delegate* <void*, void*, ushort, bool>)&" + guard.FullName + ");");
            }

            foreach (var entry in sharedAiEntries)
            {
                ushort id = ComputeHash(entry.CompoundKey);
                string thunkName = entry.IsCondition
                    ? "Guard_" + entry.MethodName + "_At" + entry.Offset
                    : "Action_" + entry.MethodName + "_At" + entry.Offset;
                if (entry.IsCondition)
                    sb.AppendLine("            HsmActionDispatcher.RegisterGuard(" + id + ", (IntPtr)(delegate* <void*, void*, ushort, bool>)&" + thunkName + ");");
                else
                    sb.AppendLine("            HsmActionDispatcher.RegisterAction(" + id + ", (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&" + thunkName + ");");
            }

            // Register ExitCleanup actions
            foreach (var m in exitCleanupMethods)
            {
                ushort id = ComputeHash("ExitCleanup_" + m.Name);
                sb.AppendLine("            HsmActionDispatcher.RegisterAction(" + id + ", (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&ExitCleanup_" + m.Name + ");");
            }
            foreach (var entry in sharedAiExitCleanups)
            {
                ushort id = ComputeHash("ExitCleanup_" + entry.MethodName);
                sb.AppendLine("            HsmActionDispatcher.RegisterAction(" + id + ", (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&ExitCleanup_" + entry.MethodName + ");");
            }

            sb.AppendLine("        }");
            sb.AppendLine();

            // --- RequiredExitCleanups dictionary (BHU-014) ---
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Maps channel-writing action names to their required exit-cleanup action name.");
            sb.AppendLine("        /// Pass to HsmGraphValidator.ValidateChannelSafety() during compile-time validation.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static readonly global::System.Collections.Generic.IReadOnlyDictionary<string, string> RequiredExitCleanups =");
            sb.AppendLine("            new global::System.Collections.Generic.Dictionary<string, string>");
            sb.AppendLine("            {");

            foreach (var m in exitCleanupMethods)
                sb.AppendLine("                [\"" + m.Name + "\"] = \"ExitCleanup_" + m.Name + "\",");
            foreach (var entry in sharedAiExitCleanups)
                sb.AppendLine("                [\"" + entry.MethodName + "\"] = \"ExitCleanup_" + entry.MethodName + "\",");

            sb.AppendLine("            };");

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ---- Thunk emitters ----------------------------------------------------

        private static void EmitSharedAiGuardThunk(StringBuilder sb, SharedAiEntry entry)
        {
            sb.AppendLine("        /// <summary>SharedAi guard thunk for " + entry.MethodName + " operating on DTO field at byte offset " + entry.Offset + ".</summary>");
            sb.AppendLine("        private static unsafe bool Guard_" + entry.MethodName + "_At" + entry.Offset + "(void* instancePtr, void* contextPtr, ushort eventId)");
            sb.AppendLine("        {");
            sb.AppendLine("            // CONSTRAINT: Do NOT add or remove ECS components from this thunk.");
            sb.AppendLine("            // Shared action thunks write directly to EntityRepository, bypassing FastHSM's");
            sb.AppendLine("            // deferred HsmCommandWriter. Structural ECS mutations during chunk iteration");
            sb.AppendLine("            // corrupt the chunk arrays. Only read/write fields of existing components.");
            sb.AppendLine("            var bridge = (global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge*)contextPtr;");
            sb.AppendLine("            var repo   = (global::Fdp.Core.EntityRepository)global::System.Runtime.InteropServices.GCHandle.FromIntPtr(bridge->WorldHandle).Target!;");
            sb.AppendLine("            ref var bb = ref repo.GetComponentRW<global::Fdp.Toolkit.Behavior.Components.BrainBlackboard>(bridge->Self);");
            sb.AppendLine("            ref var field = ref Unsafe.As<byte, " + entry.FieldTypeFqn + ">(");
            sb.AppendLine("                ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (IntPtr)" + entry.Offset + "));");
            if (entry.IsHeavy)
            {
                if (entry.IsHeavyManaged)
                {
                    sb.AppendLine("            var heavy = repo.GetComponent<" + entry.HeavyComponentFqn + ">(bridge->Self);");
                    sb.AppendLine("            return global::" + entry.FullName + "(ref field, heavy, bridge->Self, repo);");
                }
                else
                {
                    sb.AppendLine("            ref var heavyComp = ref repo.GetComponentRW<" + entry.HeavyComponentFqn + ">(bridge->Self);");
                    sb.AppendLine("            ref var heavy = ref Unsafe.As<byte, " + entry.HeavyDtoFqn + ">(");
                    sb.AppendLine("                ref Unsafe.AddByteOffset(ref Unsafe.As<" + entry.HeavyComponentFqn + ", byte>(ref heavyComp), (IntPtr)0));");
                    sb.AppendLine("            return global::" + entry.FullName + "(ref field, ref heavy, bridge->Self, repo);");
                }
            }
            else
            {
                sb.AppendLine("            return global::" + entry.FullName + "(ref field, bridge->Self, repo);");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static void EmitSharedAiActionThunk(StringBuilder sb, SharedAiEntry entry)
        {
            sb.AppendLine("        /// <summary>SharedAi action thunk for " + entry.MethodName + " operating on DTO field at byte offset " + entry.Offset + ".</summary>");
            sb.AppendLine("        private static unsafe void Action_" + entry.MethodName + "_At" + entry.Offset + "(void* instancePtr, void* contextPtr, HsmCommandWriter* writer)");
            sb.AppendLine("        {");
            sb.AppendLine("            // CONSTRAINT: Do NOT add or remove ECS components from this thunk.");
            sb.AppendLine("            // Shared action thunks write directly to EntityRepository, bypassing FastHSM's");
            sb.AppendLine("            // deferred HsmCommandWriter. Structural ECS mutations during chunk iteration");
            sb.AppendLine("            // corrupt the chunk arrays. Only read/write fields of existing components.");
            sb.AppendLine("            var bridge = (global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge*)contextPtr;");
            sb.AppendLine("            var repo   = (global::Fdp.Core.EntityRepository)global::System.Runtime.InteropServices.GCHandle.FromIntPtr(bridge->WorldHandle).Target!;");
            sb.AppendLine("            ref var bb = ref repo.GetComponentRW<global::Fdp.Toolkit.Behavior.Components.BrainBlackboard>(bridge->Self);");
            sb.AppendLine("            ref var field = ref Unsafe.As<byte, " + entry.FieldTypeFqn + ">(");
            sb.AppendLine("                ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (IntPtr)" + entry.Offset + "));");
            if (entry.IsHeavy)
            {
                if (entry.IsHeavyManaged)
                {
                    sb.AppendLine("            var heavy = repo.GetComponent<" + entry.HeavyComponentFqn + ">(bridge->Self);");
                    sb.AppendLine("            // Discard the NodeStatus return; the HSM action slot is void.");
                    sb.AppendLine("            global::" + entry.FullName + "(ref field, heavy, bridge->Self, repo);");
                }
                else
                {
                    sb.AppendLine("            ref var heavyComp = ref repo.GetComponentRW<" + entry.HeavyComponentFqn + ">(bridge->Self);");
                    sb.AppendLine("            ref var heavy = ref Unsafe.As<byte, " + entry.HeavyDtoFqn + ">(");
                    sb.AppendLine("                ref Unsafe.AddByteOffset(ref Unsafe.As<" + entry.HeavyComponentFqn + ", byte>(ref heavyComp), (IntPtr)0));");
                    sb.AppendLine("            // Discard the NodeStatus return; the HSM action slot is void.");
                    sb.AppendLine("            global::" + entry.FullName + "(ref field, ref heavy, bridge->Self, repo);");
                }
            }
            else
            {
                sb.AppendLine("            // Discard the NodeStatus return; the HSM action slot is void.");
                sb.AppendLine("            global::" + entry.FullName + "(ref field, bridge->Self, repo);");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static void EmitExitCleanupThunk(StringBuilder sb, string methodName, List<int> channels)
        {
            sb.AppendLine("        /// <summary>Exit-cleanup thunk: resets channel state when action '" + methodName + "' leaves its state.</summary>");
            sb.AppendLine("        private static unsafe void ExitCleanup_" + methodName + "(void* instancePtr, void* contextPtr, HsmCommandWriter* writer)");
            sb.AppendLine("        {");
            sb.AppendLine("            // CONSTRAINT: Do NOT add or remove ECS components from this thunk.");
            sb.AppendLine("            // Shared action thunks write directly to EntityRepository, bypassing FastHSM's");
            sb.AppendLine("            // deferred HsmCommandWriter. Structural ECS mutations during chunk iteration");
            sb.AppendLine("            // corrupt the chunk arrays. Only read/write fields of existing components.");
            sb.AppendLine("            var bridge = (global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge*)contextPtr;");
            sb.AppendLine("            var repo   = (global::Fdp.Core.EntityRepository)global::System.Runtime.InteropServices.GCHandle.FromIntPtr(bridge->WorldHandle).Target!;");
            foreach (int kind in channels)
            {
                string? ct = ChannelKindToFqn(kind);
                if (ct == null) continue;
                string varName = "ch" + kind;
                sb.AppendLine("            ref var " + varName + " = ref repo.GetComponentRW<" + ct + ">(bridge->Self);");
                sb.AppendLine("            " + varName + ".ActiveAction     = 0;");
                sb.AppendLine("            " + varName + ".ActionInstanceId = unchecked(" + varName + ".ActionInstanceId + 1u);");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static string? ChannelKindToFqn(int kind) => kind switch
        {
            0 => LocomotionChannelFqn,
            1 => WeaponChannelFqn,
            2 => InteractionChannelFqn,
            _ => null,
        };

        // ---- FNV-1a hash (identical to BTreeActionGenerator) -------------------

        private static ushort ComputeHash(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name) { hash ^= c; hash *= 16777619; }
            return (ushort)(hash & 0xFFFF);
        }

        // ---- Data types --------------------------------------------------------

        private class MethodInfo
        {
            public string Name      { get; set; } = "";
            public string FullName  { get; set; } = "";
            public bool IsGuard     { get; set; }
            public bool IsStatic    { get; set; }
            public bool IsSharedAi  { get; set; }
            public bool IsSharedCondition { get; set; }
            public bool IsSharedAction    { get; set; }
            public bool IsSharedHeavy     { get; set; }
            public bool IsSharedHeavyCondition { get; set; }
            public IMethodSymbol? Symbol  { get; set; }
            public List<int> WritesChannels { get; set; } = new List<int>();
        }

        private class SharedAiEntry
        {
            public string MethodName   { get; set; } = "";
            public string FullName     { get; set; } = "";
            public string FieldTypeFqn { get; set; } = "";
            public int    Offset       { get; set; }
            public string CompoundKey  { get; set; } = "";
            public bool   IsCondition  { get; set; }
            // Heavy-action fields (populated only when IsHeavy == true)
            public bool IsHeavy { get; set; }
            public bool IsHeavyManaged    { get; set; }
            public string? HeavyComponentFqn { get; set; }
            public string? HeavyFieldName    { get; set; }
            public string? HeavyDtoFqn       { get; set; }
            public List<int> WritesChannels { get; set; } = new List<int>();
        }
    }
}
