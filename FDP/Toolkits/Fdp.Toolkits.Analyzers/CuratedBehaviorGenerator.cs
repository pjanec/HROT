using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    /// <summary>
    /// CE-373 -- emits <c>CuratedBehaviorRegistrar</c>, the generated replacement for the
    /// hand-written <c>CgfCuratedBehaviorRegistrar</c>.
    /// See DESIGN_Behavior_Self_Registration.md §4-§7.
    ///
    /// <para>
    /// DISCOVERY DOES NOT CHANGE. The emitted class carries <c>[BlueprintRegistrar]</c>, so
    /// <c>BlueprintRegistrarScanner</c> finds it exactly as it finds the 30 generated JSON
    /// registrars, and hot reload works by construction because generated code is compiled INTO the
    /// assembly (the scanner already handles collectible ALCs). A runtime reflective sweep was
    /// rejected: it would be a SECOND discovery mechanism, not AOT-friendly and not breakpointable.
    /// </para>
    ///
    /// <para>
    /// WHAT IT REGISTERS is an explicit opt-in, <c>Curated = true</c>, and that is measured rather
    /// than assumed: Hrot.AI.Behaviors carries nine <c>[BTreeDefinition]</c> methods and only five
    /// are curated topologies. <c>HideInCover_BT</c>/<c>_v2</c> are registered as behaviours nowhere,
    /// and <c>PlatoonHillAttack</c>/<c>HullDownAttackRun</c> have their topology owned by generated
    /// JSON registrars -- registering those here would hard-error on a duplicate name.
    /// </para>
    ///
    /// <para>
    /// RESOLVERS bind by name through <c>RegisterResolver</c> and are order-independent, so a
    /// resolver may name a behaviour whose topology a generated registrar owns. R-132 rules that a
    /// curated overlay WINS; the <c>[BehaviorResolver]</c> attribute is now that declaration.
    /// </para>
    /// </summary>
    [Generator]
    public class CuratedBehaviorGenerator : IIncrementalGenerator
    {
        private static readonly DiagnosticDescriptor InvalidResolver = new DiagnosticDescriptor(
            id: "BEH001",
            title: "Invalid BehaviorResolver method",
            messageFormat: "Method '{0}' annotated with [BehaviorResolver] must be static and take (string, byte*, int), the full 5-parameter ParseParamsDelegate shape, or the typed block shape (in TAuthored, ref TBlock, EntityRepository, Entity)",
            category: "BehaviorSourceGen",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var topologies = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetTopology(ctx))
                .Where(static m => m != null);

            var resolvers = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetResolver(ctx))
                .Where(static m => m != null);

            var combined = context.CompilationProvider
                .Combine(topologies.Collect())
                .Combine(resolvers.Collect());

            context.RegisterSourceOutput(
                combined,
                static (spc, src) => Execute(spc, src.Left.Left, src.Left.Right, src.Right));
        }

        // ---- collection -------------------------------------------------------

        private static CuratedTopology? GetTopology(GeneratorSyntaxContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            if (context.SemanticModel.GetDeclaredSymbol(method) is not IMethodSymbol symbol) return null;

            foreach (var attr in symbol.GetAttributes())
            {
                string? attrName = attr.AttributeClass?.Name;
                bool isBTree = attrName == "BTreeDefinitionAttribute";
                bool isHsm   = attrName == "HsmDefinitionAttribute";
                if (!isBTree && !isHsm) continue;

                // The explicit opt-in. Absent or false => not a curated behaviour.
                var curated = attr.NamedArguments.FirstOrDefault(kv => kv.Key == "Curated");
                if (curated.Key == null || curated.Value.Value is not bool c || !c) return null;

                string? name = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value as string
                    : null;
                if (string.IsNullOrEmpty(name)) return null;

                var paramsArg = attr.NamedArguments.FirstOrDefault(kv => kv.Key == "ParamsType");
                string? paramsType = paramsArg.Key != null && paramsArg.Value.Value is INamedTypeSymbol pt
                    ? pt.ToDisplayString()
                    : null;

                return new CuratedTopology
                {
                    Name       = name!,
                    IsHsm      = isHsm,
                    ParamsType = paramsType,
                };
            }
            return null;
        }

        private static CuratedResolver? GetResolver(GeneratorSyntaxContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            if (context.SemanticModel.GetDeclaredSymbol(method) is not IMethodSymbol symbol) return null;

            var attr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "BehaviorResolverAttribute");
            if (attr == null) return null;

            string? name = attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value as string
                : null;
            if (string.IsNullOrEmpty(name)) return null;

            var paramsArg = attr.NamedArguments.FirstOrDefault(kv => kv.Key == "ParamsType");
            string? paramsType = paramsArg.Key != null && paramsArg.Value.Value is INamedTypeSymbol pt
                ? pt.ToDisplayString()
                : null;

            // Two accepted shapes. The 3-param one is wrapped; the hand-written registrar spelled
            // that wrapper out by hand, twice.
            int argc = symbol.Parameters.Length;
            // ⭐⭐ CE-427 — a THIRD shape: the typed block resolver
            //   (in TAuthored authored, ref TBlock block, EntityRepository, Entity).
            //   Recognised by the ref-kinds of its first two parameters, never by a name.
            // ⭐ CE-445: `IHostVariableAccess host` is retired, so the full delegate is 5 params and the typed one 4.
            bool isBlock = argc == 4
                && symbol.Parameters[0].RefKind == RefKind.In
                && symbol.Parameters[1].RefKind == RefKind.Ref;
            bool valid = symbol.IsStatic && (argc == 3 || argc == 5 || isBlock);

            return new CuratedResolver
            {
                Name        = name!,
                MethodRef   = symbol.ContainingType.ToDisplayString() + "." + symbol.Name,
                MethodName  = symbol.Name,
                ParamCount  = argc,
                ParamsType  = paramsType,
                IsValid     = valid,
                AuthoredType = isBlock ? symbol.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null,
                BlockType    = isBlock ? symbol.Parameters[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null,
            };
        }

        // ---- emission ---------------------------------------------------------

        private static void Execute(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<CuratedTopology?> topologies,
            ImmutableArray<CuratedResolver?> resolvers)
        {
            var tops = new List<CuratedTopology>();
            foreach (var t in topologies) if (t != null) tops.Add(t);

            var res = new List<CuratedResolver>();
            foreach (var r in resolvers)
            {
                if (r == null) continue;
                if (!r.IsValid)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(InvalidResolver, Location.None, r.MethodName));
                    continue;
                }
                res.Add(r);
            }

            // Nothing curated in this assembly => emit nothing at all, so an assembly that has no
            // curated behaviour keeps byte-identical generated output.
            if (tops.Count == 0 && res.Count == 0) return;

            // Deterministic order: the emitted source must not move because Roslyn visited files in a
            // different order. A golden that moves for a reason nobody changed trains everyone to
            // regenerate, and a gate that is routinely regenerated is not a gate.
            tops.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            res.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            string assemblyName  = compilation.AssemblyName ?? "Generated";
            string namespaceName = assemblyName + ".Generated";

            context.AddSource("CuratedBehaviorRegistrar.g.cs", Generate(tops, res, namespaceName));
        }

        private static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string Generate(
            List<CuratedTopology> tops, List<CuratedResolver> res, string namespaceName)
        {
            string catalogNs = "global::" + namespaceName + ".";

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("namespace " + namespaceName);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// CE-373 -- generated replacement for the hand-written CgfCuratedBehaviorRegistrar.");
            sb.AppendLine("    /// Discovered and invoked by BlueprintRegistrarScanner exactly like the generated");
            sb.AppendLine("    /// per-asset registrars; see DESIGN_Behavior_Self_Registration.md.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    [global::Fdp.Toolkit.Blueprints.Attributes.BlueprintRegistrar]");
            sb.AppendLine("    public static class CuratedBehaviorRegistrar");
            sb.AppendLine("    {");
            // `unsafe` because ParseParamsDelegate takes a byte* -- the 3-param wrapper lambdas
            // cannot be expressed without it, and the hand-written registrar was unsafe for the
            // same reason.
            sb.AppendLine("        public static unsafe void Register(");
            sb.AppendLine("            global::Fdp.Toolkit.Behavior.BehaviorRegistry beh,");
            sb.AppendLine("            global::Fbt.Runtime.ActionRegistry<byte, global::Fdp.Toolkit.Behavior.BTreeContext> actionRegistry)");
            sb.AppendLine("        {");

            if (tops.Any(t => !t.IsHsm))
            {
                sb.AppendLine("            // Bake the resource-owning bit off the action registry so branch-abort");
                sb.AppendLine("            // deactivators fire -- the same seam the hand-written registrar used.");
                sb.AppendLine("            global::System.Func<string, bool> isResourceOwning =");
                sb.AppendLine("                name => actionRegistry.TryGetDeactivator(name, out _);");
                sb.AppendLine();
            }

            foreach (var t in tops)
            {
                string safe = SanitizeIdentifier(t.Name);
                if (!t.IsHsm)
                {
                    // E6 / CE-364 -- the blob must be a LOCAL: PlanFor walks it for hosting sites,
                    // and the resulting Slots must be on the definition before it is constructed
                    // (StatefulWorkingSlots is `init`). HostedSubtree.Tick THROWS on a slot the
                    // manifest never declared, so the plan and the Bind ship together or neither.
                    // ⭐ CE-504 slice 3 — the runtime overload: the shared-node verbs' byte thunks land in actionRegistry.
                    sb.AppendLine("            var __blob" + safe + " = " + catalogNs + "FbtTreeCatalog.Get" + safe + "(isResourceOwning, actionRegistry);");
                    sb.AppendLine("            var __plan" + safe + " = global::Fdp.Toolkit.Behavior.BTreeHostedSites.PlanFor(__blob" + safe + ", " + Q(t.Name) + ");");
                    sb.AppendLine("            var __interp" + safe + " = new global::Fbt.Runtime.Interpreter<byte, global::Fdp.Toolkit.Behavior.BTreeContext>(__blob" + safe + ", actionRegistry);");
                    // Hosting is opt-in per interpreter; without this a Subtree node returns Failure.
                    sb.AppendLine("            __interp" + safe + ".SubtreeHost = global::Fdp.Toolkit.Behavior.OccurrenceSubtreeHost.Instance;");
                }
                sb.AppendLine("            beh.Register(" + Q(t.Name) + ", new global::Fdp.Toolkit.Behavior.BehaviorDefinition");
                sb.AppendLine("            {");
                sb.AppendLine("                Name      = " + Q(t.Name) + ",");
                if (t.IsHsm)
                {
                    sb.AppendLine("                BrainTier = global::Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierHsm,");
                    sb.AppendLine("                HsmDefinition = " + catalogNs + "FhsmMachineCatalog.Get" + safe + "(),");
                    // CE-370: metadata beside the blob, so HSM traces symbolicate for every machine.
                    sb.AppendLine("                HsmMetadata   = " + catalogNs + "FhsmMachineCatalog.Get" + safe + "Metadata(),");
                }
                else
                {
                    sb.AppendLine("                BrainTier = global::Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBTree,");
                    sb.AppendLine("                BTreeInterpreter = __interp" + safe + ",");
                    sb.AppendLine("                StatefulWorkingSlots = __plan" + safe + ".Slots,");
                }
                if (t.ParamsType != null)
                    sb.AppendLine("                BlackboardLayoutType = typeof(global::" + t.ParamsType + "),");
                sb.AppendLine("            });");
                // E6 / CE-364 -- bind AFTER Register, because HostedChildren resolves the CHILD
                // through the registry and registrars run in an arbitrary order. An unresolvable
                // child is skipped and surfaces at the hosting site as Require's named exception.
                if (!t.IsHsm)
                    sb.AppendLine("            global::Fdp.Toolkit.Behavior.BTreeHostedSites.Bind(beh, __blob" + safe + ", __plan" + safe + ");");
                sb.AppendLine();
            }

            if (res.Count > 0)
            {
                sb.AppendLine("            // Named resolver overlays. Bound by NAME and order-independent, so a resolver");
                sb.AppendLine("            // may name a behaviour whose topology a generated registrar owns.");
                sb.AppendLine("            // R-132: a curated overlay WINS -- the [BehaviorResolver] attribute is that");
                sb.AppendLine("            // declaration. R-149: two curated bindings for one name THROW, never race.");
                foreach (var r in res)
                {
                    string tail = r.ParamsType != null
                        ? ", typeof(global::" + r.ParamsType + ")"
                        : "";

                    if (r.BlockType != null)
                    {
                        // CE-427: SUPPLY + RESOLVE through BehaviorParams.FromBlockResolver; the registry
                        // composes the behaviour's BAKE in front of it (ApplyResolverOverlay).
                        sb.AppendLine("            beh.RegisterResolver(" + Q(r.Name) + ",");
                        sb.AppendLine("                global::Fdp.Toolkit.Behavior.BehaviorParams.FromBlockResolver<" + r.AuthoredType + ", " + r.BlockType + ">(");
                        sb.AppendLine("                    global::" + r.MethodRef + ")" + tail + ");");
                        // CE-443/CE-438: and its FROM-BYTES arm, so a HOSTED child can run it with its host
                        // variable as the source. null (class-typed TAuthored) registers nothing.
                        sb.AppendLine("            {");
                        sb.AppendLine("                var __src = global::Fdp.Toolkit.Behavior.BehaviorParams.FromBlockResolverSource<" + r.AuthoredType + ", " + r.BlockType + ">(");
                        sb.AppendLine("                    global::" + r.MethodRef + ");");
                        sb.AppendLine("                if (__src != null) beh.RegisterSourceResolver(" + Q(r.Name) + ", __src);");
                        sb.AppendLine("            }");
                    }
                    else if (r.ParamCount == 5)
                    {
                        sb.AppendLine("            beh.RegisterResolver(" + Q(r.Name) + ", global::" + r.MethodRef + tail + ");");
                    }
                    else
                    {
                        // The 3-param shape takes only (json, memory, capacity); world/self/host are
                        // discarded, exactly as the hand-written lambdas did.
                        // NOTE the params type is RegisterResolver's THIRD ARGUMENT -- it must attach
                        // to the call, never to the wrapped invocation.
                        sb.AppendLine("            beh.RegisterResolver(" + Q(r.Name) + ",");
                        sb.AppendLine("                (json, memory, capacity, world, self) =>");
                        sb.AppendLine("                    global::" + r.MethodRef + "(json, memory, capacity)" + tail + ");");
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string SanitizeIdentifier(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            if (sb.Length > 0 && char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }
    }

    internal class CuratedTopology
    {
        public string Name { get; set; } = "";
        public bool IsHsm { get; set; }
        public string? ParamsType { get; set; }
    }

    internal class CuratedResolver
    {
        public string Name { get; set; } = "";
        public string MethodRef { get; set; } = "";
        public string MethodName { get; set; } = "";
        public int ParamCount { get; set; }
        /// <summary>CE-427: the typed block resolver's authored type (FQN), or null for the byte-level shapes.</summary>
        public string? AuthoredType { get; set; }
        /// <summary>CE-427: the typed block resolver's block type (FQN), or null for the byte-level shapes.</summary>
        public string? BlockType { get; set; }
        public string? ParamsType { get; set; }
        public bool IsValid { get; set; }
    }
}
