using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    /// <summary>
    /// CE-371 -- the missing twin of <c>BTreeDefinitionGenerator</c>.
    /// See DESIGN_Behavior_Self_Registration.md §2.1 / §9 item 1.
    ///
    /// <para>
    /// Measured 2026-09-27: of the eight definition/registrar attributes in the engine, seven have a
    /// generator or a boot scan and <c>[HsmDefinition]</c> had NEITHER -- its only reader in the repo
    /// was the editor's asset contributor. That gap is why the Idle machine had to be registered by
    /// hand while every JSON-authored machine got a generated registrar. It is a missing generator,
    /// not a property of hand-built HSMs.
    /// </para>
    ///
    /// <para>
    /// Emits <c>FhsmMachineCatalog</c> with a <c>Get&lt;Name&gt;()</c> per machine, exactly mirroring
    /// <c>FbtTreeCatalog</c>. Two return shapes are accepted, as the BTree twin accepts a blob or a
    /// builder:
    /// <list type="bullet">
    ///   <item><c>HsmDefinitionBlob</c> -- returned as-is.</item>
    ///   <item><c>StateMachineGraph</c> -- the catalog runs Normalize -&gt; Flatten -&gt; Emit.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// CE-370: a <c>Get&lt;Name&gt;Metadata()</c> is emitted BESIDE each blob. The catalog is the one
    /// place that holds the graph, so it is the only place that can produce both without a second
    /// traversal -- which is what lets every HSM carry <c>BehaviorDefinition.HsmMetadata</c> instead
    /// of only the one hand-written machine. Three production consumers read it:
    /// HsmTraceWorkingMemoryTranslator, HsmTraceWorkingMemoryRenderer and BrainTickSystem.
    /// A blob-returning method cannot supply metadata (the graph is gone by then) and returns null --
    /// that is why the graph-returning shape is the preferred one for hand-built machines.
    /// </para>
    /// </summary>
    [Generator]
    public class HsmDefinitionGenerator : IIncrementalGenerator
    {
        private static readonly DiagnosticDescriptor InvalidDefinitionMethod = new DiagnosticDescriptor(
            id: "HSM002",
            title: "Invalid HsmDefinition method",
            messageFormat: "Method '{0}' annotated with [HsmDefinition] must be static, have no parameters, and return HsmDefinitionBlob or StateMachineGraph",
            category: "HsmSourceGen",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetDefinitionInfo(ctx))
                .Where(static m => m != null);

            var compilationAndMethods = context.CompilationProvider.Combine(candidateMethods.Collect());

            context.RegisterSourceOutput(
                compilationAndMethods,
                static (spc, source) => Execute(spc, source.Left, source.Right));
        }

        private static HsmDefinitionInfo? GetDefinitionInfo(GeneratorSyntaxContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;

            if (symbol == null) return null;

            var definitionAttr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "HsmDefinitionAttribute");

            if (definitionAttr == null) return null;

            string? machineName = null;
            if (definitionAttr.ConstructorArguments.Length > 0)
                machineName = definitionAttr.ConstructorArguments[0].Value as string;

            if (string.IsNullOrEmpty(machineName)) return null;

            // Two accepted return shapes, mirroring BTreeDefinitionGenerator's blob-or-builder rule.
            bool returnsBlob  = symbol.ReturnType.Name == "HsmDefinitionBlob";
            bool returnsGraph = symbol.ReturnType.Name == "StateMachineGraph";

            bool isValid = symbol.IsStatic
                && symbol.Parameters.Length == 0
                && (returnsBlob || returnsGraph);

            return new HsmDefinitionInfo
            {
                MethodName = symbol.Name,
                FullyQualifiedTypeName = symbol.ContainingType.ToDisplayString(),
                MachineName = machineName!,
                IsValid = isValid,
                ReturnsGraph = returnsGraph,
            };
        }

        private static void Execute(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<HsmDefinitionInfo?> methods)
        {
            var valid = new List<HsmDefinitionInfo>();
            foreach (var m in methods)
            {
                if (m == null) continue;
                if (!m.IsValid)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(InvalidDefinitionMethod, Location.None, m.MethodName));
                }
                else
                {
                    valid.Add(m);
                }
            }

            // Do not emit the catalog when nothing is attributed -- an assembly with no hand-built
            // machine keeps byte-identical generated output.
            if (valid.Count == 0) return;

            string assemblyName = compilation.AssemblyName ?? "Generated";
            string namespaceName = assemblyName + ".Generated";

            var source = GenerateCatalog(valid, namespaceName);
            context.AddSource("FhsmMachineCatalog.g.cs", source);
        }

        // Replace any character that is not a letter, digit, or underscore with '_'.
        // Prepend '_' if the name starts with a digit. (Same rule as BTreeDefinitionGenerator.)
        private static string SanitizeIdentifier(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            if (sb.Length > 0 && char.IsDigit(sb[0]))
                sb.Insert(0, '_');
            return sb.ToString();
        }

        private static string GenerateCatalog(List<HsmDefinitionInfo> methods, string namespaceName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("namespace " + namespaceName);
            sb.AppendLine("{");
            sb.AppendLine("    public static class FhsmMachineCatalog");
            sb.AppendLine("    {");

            foreach (var m in methods)
            {
                string safeName = SanitizeIdentifier(m.MachineName);
                string call = "global::" + m.FullyQualifiedTypeName + "." + m.MethodName + "()";

                if (m.ReturnsGraph)
                {
                    // StateMachineGraph.Compile() IS the pipeline -- Normalize, Flatten, Emit, AND
                    // `blob.Metadata = BuildMachineMetadata(this)`. Spelling those four steps out
                    // here would be a second copy of a sequence that already exists, and it is what
                    // an earlier draft of this generator did.
                    sb.AppendLine("        public static global::Fhsm.Kernel.Data.HsmDefinitionBlob Get" + safeName + "()");
                    sb.AppendLine("            => " + call + ".Compile();");
                }
                else
                {
                    sb.AppendLine("        public static global::Fhsm.Kernel.Data.HsmDefinitionBlob Get" + safeName + "()");
                    sb.AppendLine("            => " + call + ";");
                }
                sb.AppendLine();
                // CE-370 -- the metadata rides ON the blob, so BOTH return shapes carry it and
                // neither needs a second traversal.
                sb.AppendLine("        public static global::Fhsm.Kernel.Data.MachineMetadata? Get" + safeName + "Metadata()");
                sb.AppendLine("            => Get" + safeName + "().Metadata;");
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    internal class HsmDefinitionInfo
    {
        public string MethodName { get; set; } = "";
        public string FullyQualifiedTypeName { get; set; } = "";
        public string MachineName { get; set; } = "";
        public bool IsValid { get; set; }
        public bool ReturnsGraph { get; set; }
    }
}
