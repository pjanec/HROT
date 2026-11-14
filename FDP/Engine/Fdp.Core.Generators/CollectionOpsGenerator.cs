using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    /// <summary>
    /// FC-1b (Fixed Collections, Q#20 "G1 resolution") -- emits the curated collection accessor ops
    /// class for every <c>[Fdp.Core.BlueprintCollectionField]</c>-marked <c>[InlineArray]</c> buffer
    /// field: <c>public static class {Component}{Field}Ops</c> in the component's namespace, carrying
    /// the <c>[BlueprintCollection]</c>/<c>[BlueprintCollectionItem]</c> READ pair plus the
    /// <c>[BlueprintCollectionWrite]</c> write set selected by the attribute's <c>Access</c>/<c>Ops</c>
    /// knobs. The emitted body is the FC-0 reference template (<c>BpFixedListDemoOps</c>) verbatim in
    /// shape: <c>Span&lt;T&gt;</c> write-through (never the inline-array indexer through a ref chain),
    /// the G6 tail-always-default invariant (RemoveAt/Clear/Resize-shrink zero vacated slots; grow
    /// never fills), and the F2 defensive Count clamp (a garbage Count can never drive an OOB access).
    ///
    /// <para>
    /// A HAND-WRITTEN accessor for the same (component FQN, collection name) anywhere in the
    /// compilation wins: the generator emits nothing for that field (bespoke-semantics escape hatch,
    /// silent by design). Structural contract violations are reported as FCOL diagnostics and the
    /// field is skipped.
    /// </para>
    /// </summary>
    [Generator]
    public class CollectionOpsGenerator : IIncrementalGenerator
    {
        private const string FieldAttributeFqn = "Fdp.Core.BlueprintCollectionFieldAttribute";

        // ---- Diagnostics ------------------------------------------------------

        private static readonly DiagnosticDescriptor FCOL001 = new(
            "FCOL001", "Count field missing or not int",
            "[BlueprintCollectionField] on '{0}': the component has no int field named '{1}' (the CountField argument must name a sibling int field)",
            "FixedCollections", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor FCOL002 = new(
            "FCOL002", "Field type is not an [InlineArray] struct",
            "[BlueprintCollectionField] on '{0}': the field's type '{1}' does not carry [System.Runtime.CompilerServices.InlineArray] -- only fixed-capacity inline-array buffers are supported",
            "FixedCollections", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor FCOL003 = new(
            "FCOL003", "Element type is not unmanaged",
            "[BlueprintCollectionField] on '{0}': the buffer's element type '{1}' is not unmanaged -- collection elements must be blittable (memcpy-safe snapshots)",
            "FixedCollections", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor FCOL004 = new(
            "FCOL004", "Component is not an unmanaged struct",
            "[BlueprintCollectionField] on '{0}': the containing component must be an unmanaged struct -- a managed (class) component's collections are never element-writable (Q#20-C)",
            "FixedCollections", DiagnosticSeverity.Error, isEnabledByDefault: true);

        // ---- Model ------------------------------------------------------------

        private sealed class FieldModel
        {
            public string Namespace;          // component's namespace ("" for global)
            public string ComponentName;      // short name (nested containers concatenated)
            public string ComponentFqn;       // dotted FQN (claims matching, hint names, doc text)
            public string ComponentGlobal;    // fully-qualified codegen form (global::Ns.Type)
            public string FieldName;          // == the collection's logical name
            public string CountField;
            public string ElementGlobal;      // fully-qualified codegen form of the element type (keyword for special types)
            public int Capacity;
            public bool ReadOnly;
            public int OpsMask;               // CollectionOps flags
            public Location Location;
            public DiagnosticDescriptor ErrorDescriptor;
            public object[] ErrorArgs;
        }

        // ---- Initialize -------------------------------------------------------

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var fields = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) =>
                        node is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: FieldDeclarationSyntax { AttributeLists.Count: > 0 } } },
                    transform: static (ctx, _) => GetFieldModel(ctx))
                .Where(static m => m != null);

            // Hand-written-wins: collect every (component FQN, collection name) already claimed by a
            // hand-authored [BlueprintCollection]-marked Count accessor in this compilation.
            var handWritten = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                    transform: static (ctx, _) => GetHandWrittenClaim(ctx))
                .Where(static c => c != null)
                .Collect();

            context.RegisterSourceOutput(
                fields.Collect().Combine(handWritten),
                static (spc, source) => Execute(spc, source.Left!, source.Right!));
        }

        // ---- Transform: the marked field --------------------------------------

        private static FieldModel GetFieldModel(GeneratorSyntaxContext context)
        {
            var declarator = (VariableDeclaratorSyntax)context.Node;
            if (context.SemanticModel.GetDeclaredSymbol(declarator) is not IFieldSymbol field) return null;

            AttributeData attr = null;
            foreach (var a in field.GetAttributes())
            {
                if (a.AttributeClass?.ToDisplayString() == FieldAttributeFqn
                    || a.AttributeClass?.Name == "BlueprintCollectionFieldAttribute")
                {
                    attr = a;
                    break;
                }
            }
            if (attr == null) return null;

            var component = field.ContainingType;
            var location  = declarator.GetLocation();
            string fieldDisplay = component.ToDisplayString() + "." + field.Name;

            string countField = attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value?.ToString() ?? ""
                : "";
            bool readOnly = false;
            int opsMask = 0x3F; // CollectionOps.All
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Access" && named.Value.Value is int access) readOnly = access == 1;
                if (named.Key == "Ops" && named.Value.Value is int ops) opsMask = ops;
            }

            FieldModel Error(DiagnosticDescriptor d, params object[] args) => new()
            {
                Location = location, ErrorDescriptor = d, ErrorArgs = args,
            };

            // FCOL004: component must be an unmanaged struct (Q#20-C).
            if (component.TypeKind != TypeKind.Struct || component.IsRefLikeType)
                return Error(FCOL004, fieldDisplay);

            // FCOL001: sibling int count field.
            var count = component.GetMembers(countField).OfType<IFieldSymbol>()
                .FirstOrDefault(f => !f.IsStatic && f.Type.SpecialType == SpecialType.System_Int32);
            if (count is null)
                return Error(FCOL001, fieldDisplay, countField);

            // FCOL002: the field's type carries [InlineArray(N)].
            if (field.Type is not INamedTypeSymbol buffer)
                return Error(FCOL002, fieldDisplay, field.Type.ToDisplayString());
            var inlineArray = buffer.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.InlineArrayAttribute");
            if (inlineArray is null || inlineArray.ConstructorArguments.Length == 0
                || inlineArray.ConstructorArguments[0].Value is not int capacity || capacity <= 0)
                return Error(FCOL002, fieldDisplay, buffer.ToDisplayString());

            // FCOL003: element type (the buffer's single instance field) is unmanaged.
            var element = buffer.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic)?.Type;
            if (element is null || !element.IsUnmanagedType)
                return Error(FCOL003, fieldDisplay, element?.ToDisplayString() ?? "?");
            if (!component.IsUnmanagedType)
                return Error(FCOL004, fieldDisplay);

            // Nested component containers concatenate into the class name (Outer.Inner -> OuterInner).
            var nameParts = new List<string>();
            for (var t = component; t != null; t = t.ContainingType) nameParts.Insert(0, t.Name);

            return new FieldModel
            {
                Namespace       = component.ContainingNamespace.IsGlobalNamespace
                    ? "" : component.ContainingNamespace.ToDisplayString(),
                ComponentName   = string.Concat(nameParts),
                ComponentFqn    = component.ToDisplayString(),
                ComponentGlobal = component.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                FieldName       = field.Name,
                CountField      = countField,
                // FullyQualifiedFormat keeps special types as their KEYWORD form ("int"), which is
                // valid everywhere -- never prefix global:: manually (global::int is not C#).
                ElementGlobal   = element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Capacity        = capacity,
                ReadOnly        = readOnly,
                OpsMask         = opsMask,
                Location        = location,
            };
        }

        // ---- Transform: hand-written claims ------------------------------------

        private static string GetHandWrittenClaim(GeneratorSyntaxContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            if (context.SemanticModel.GetDeclaredSymbol(method) is not IMethodSymbol symbol) return null;

            foreach (var a in symbol.GetAttributes())
            {
                var name = a.AttributeClass?.Name;
                if (name != "BlueprintCollectionAttribute" && name != "BlueprintCollectionItemAttribute"
                    && name != "BlueprintCollectionWriteAttribute")
                    continue;
                if (a.ConstructorArguments.Length < 2) continue;
                var componentType = a.ConstructorArguments[0].Value as ITypeSymbol;
                var collName      = a.ConstructorArguments[1].Value?.ToString();
                if (componentType is null || string.IsNullOrEmpty(collName)) continue;
                return componentType.ToDisplayString() + "|" + collName;
            }
            return null;
        }

        // ---- Execute -----------------------------------------------------------

        private static void Execute(
            SourceProductionContext spc,
            ImmutableArray<FieldModel> fields,
            ImmutableArray<string> handWritten)
        {
            if (fields.IsDefaultOrEmpty) return;
            var claimed = new HashSet<string>(handWritten.Where(c => c != null));

            foreach (var m in fields)
            {
                if (m.ErrorDescriptor != null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(m.ErrorDescriptor, m.Location, m.ErrorArgs));
                    continue;
                }

                // Hand-written wins -- silent skip (the documented escape hatch).
                if (claimed.Contains(m.ComponentFqn + "|" + m.FieldName))
                    continue;

                var hint = (m.Namespace.Length > 0 ? m.Namespace + "." : "") + m.ComponentName + m.FieldName + "Ops.g.cs";
                spc.AddSource(hint, GenerateOpsClass(m));
            }
        }

        // ---- Codegen (the FC-0 BpFixedListDemoOps template) --------------------

        private static string GenerateOpsClass(FieldModel m)
        {
            string cls   = m.ComponentName + m.FieldName + "Ops";
            string comp  = m.ComponentGlobal;
            string elem  = m.ElementGlobal;
            string field = m.FieldName;
            string count = m.CountField;
            int cap      = m.Capacity;

            bool Has(int flag) => !m.ReadOnly && (m.OpsMask & flag) != 0;

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Emitted by CollectionOpsGenerator (FC-1b, Fixed Collections Q#20 \"G1 resolution\")");
            sb.AppendLine($"// from [BlueprintCollectionField] on {m.ComponentFqn}.{field}. Template: BpFixedListDemoOps (FC-0).");
            sb.AppendLine("// Rules embodied here: Span<T> write-through (never the inline-array indexer through a");
            sb.AppendLine("// ref chain), G6 tail-always-default (vacated slots zeroed at mutation time; grow never");
            sb.AppendLine("// fills), F2 defensive Count clamp (a garbage Count can never drive an OOB access).");
            sb.AppendLine("using System;");
            sb.AppendLine();
            if (m.Namespace.Length > 0)
            {
                sb.AppendLine($"namespace {m.Namespace}");
                sb.AppendLine("{");
            }
            sb.AppendLine($"    public static class {cls}");
            sb.AppendLine("    {");

            // ---- read pair (always) ----
            sb.AppendLine($"        [global::Fdp.Core.BlueprintCollection(typeof({comp}), \"{field}\")]");
            sb.AppendLine($"        public static int Count(in {comp} c) => __Clamp(c.{count});");
            sb.AppendLine();
            sb.AppendLine($"        [global::Fdp.Core.BlueprintCollectionItem(typeof({comp}), \"{field}\")]");
            sb.AppendLine($"        public static {elem} Item(in {comp} c, int i)");
            sb.AppendLine($"            => ((ReadOnlySpan<{elem}>)c.{field})[i];");

            if (Has(1 << 0)) // Add
            {
                sb.AppendLine();
                sb.AppendLine($"        [global::Fdp.Core.BlueprintCollectionWrite(typeof({comp}), \"{field}\", global::Fdp.Core.BlueprintCollectionOp.Add)]");
                sb.AppendLine($"        public static bool Add(ref {comp} c, {elem} v)");
                sb.AppendLine("        {");
                sb.AppendLine($"            int count = __Clamp(c.{count});");
                sb.AppendLine($"            if (count >= {cap}) return false;");
                sb.AppendLine($"            ((Span<{elem}>)c.{field})[count] = v;");
                sb.AppendLine($"            c.{count} = count + 1;");
                sb.AppendLine("            return true;");
                sb.AppendLine("        }");
            }
            if (Has(1 << 1)) // SetAt
            {
                sb.AppendLine();
                sb.AppendLine($"        [global::Fdp.Core.BlueprintCollectionWrite(typeof({comp}), \"{field}\", global::Fdp.Core.BlueprintCollectionOp.SetAt)]");
                sb.AppendLine($"        public static bool SetAt(ref {comp} c, int i, {elem} v)");
                sb.AppendLine("        {");
                sb.AppendLine($"            if ((uint)i >= (uint)__Clamp(c.{count})) return false;");
                sb.AppendLine($"            ((Span<{elem}>)c.{field})[i] = v;");
                sb.AppendLine("            return true;");
                sb.AppendLine("        }");
            }
            if (Has(1 << 2)) // InsertAt
            {
                sb.AppendLine();
                sb.AppendLine($"        [global::Fdp.Core.BlueprintCollectionWrite(typeof({comp}), \"{field}\", global::Fdp.Core.BlueprintCollectionOp.InsertAt)]");
                sb.AppendLine($"        public static bool InsertAt(ref {comp} c, int i, {elem} v)");
                sb.AppendLine("        {");
                sb.AppendLine($"            int count = __Clamp(c.{count});");
                sb.AppendLine($"            if (count >= {cap} || (uint)i > (uint)count) return false;");
                sb.AppendLine($"            Span<{elem}> s = c.{field};");
                sb.AppendLine("            s[i..count].CopyTo(s[(i + 1)..]);");
                sb.AppendLine("            s[i] = v;");
                sb.AppendLine($"            c.{count} = count + 1;");
                sb.AppendLine("            return true;");
                sb.AppendLine("        }");
            }
            if (Has(1 << 3)) // RemoveAt
            {
                sb.AppendLine();
                sb.AppendLine($"        [global::Fdp.Core.BlueprintCollectionWrite(typeof({comp}), \"{field}\", global::Fdp.Core.BlueprintCollectionOp.RemoveAt)]");
                sb.AppendLine($"        public static bool RemoveAt(ref {comp} c, int i)");
                sb.AppendLine("        {");
                sb.AppendLine($"            int count = __Clamp(c.{count});");
                sb.AppendLine("            if ((uint)i >= (uint)count) return false;");
                sb.AppendLine($"            Span<{elem}> s = c.{field};");
                sb.AppendLine("            s[(i + 1)..count].CopyTo(s[i..]);");
                sb.AppendLine("            s[count - 1] = default;   // G6: vacated slot re-zeroed");
                sb.AppendLine($"            c.{count} = count - 1;");
                sb.AppendLine("            return true;");
                sb.AppendLine("        }");
            }
            if (Has(1 << 4)) // Clear
            {
                sb.AppendLine();
                sb.AppendLine($"        [global::Fdp.Core.BlueprintCollectionWrite(typeof({comp}), \"{field}\", global::Fdp.Core.BlueprintCollectionOp.Clear)]");
                sb.AppendLine($"        public static void Clear(ref {comp} c)");
                sb.AppendLine("        {");
                sb.AppendLine($"            ((Span<{elem}>)c.{field})[..__Clamp(c.{count})].Clear();   // G6");
                sb.AppendLine($"            c.{count} = 0;");
                sb.AppendLine("        }");
            }
            if (Has(1 << 5)) // Resize
            {
                sb.AppendLine();
                sb.AppendLine($"        [global::Fdp.Core.BlueprintCollectionWrite(typeof({comp}), \"{field}\", global::Fdp.Core.BlueprintCollectionOp.Resize)]");
                sb.AppendLine($"        public static bool Resize(ref {comp} c, int n)");
                sb.AppendLine("        {");
                sb.AppendLine($"            if ((uint)n > {cap}) return false;");
                sb.AppendLine($"            int count = __Clamp(c.{count});");
                sb.AppendLine("            if (n < count)");
                sb.AppendLine($"                ((Span<{elem}>)c.{field})[n..count].Clear();   // G6: dropped tail re-zeroed");
                sb.AppendLine($"            c.{count} = n;");
                sb.AppendLine("            return true;");
                sb.AppendLine("        }");
            }

            sb.AppendLine();
            sb.AppendLine("        private static int __Clamp(int count)");
            sb.AppendLine($"            => count < 0 ? 0 : count > {cap} ? {cap} : count;");
            sb.AppendLine("    }");
            if (m.Namespace.Length > 0)
                sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
