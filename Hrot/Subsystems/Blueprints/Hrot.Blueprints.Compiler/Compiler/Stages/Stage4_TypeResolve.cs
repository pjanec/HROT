using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Stages;

internal static class Stage4_TypeResolve
{
    public static TypedAsset Run(BlueprintAsset asset, ValidationContext ctx)
    {
        var resolvedPinTypes   = new Dictionary<Guid, IrTypeRef>();
        var resolvedFieldTypes = new Dictionary<Guid, IrTypeRef>();

        // U-11: one call over every declaration, in place of three over two near-identical overloads.
        ResolveFieldTypes(asset.Declarations, resolvedFieldTypes, ctx, asset.AssetId);

        // BP-57: function-locals are typed the same way and into the same dictionary — the dictionary
        // is keyed by DECL ID, so a per-graph list sharing it costs nothing and cannot collide.
        // ⚠ This is the only place locals touch anything asset-scoped; their INDEX space stays
        // per-graph (IrGraph.Locals), which is what keeps them out of FindVariableIndex's union.
        foreach (var graph in asset.Graphs)
            ResolveFieldTypes(
                graph.LocalVariables.Select(v => BlueprintDeclaration.For(DeclarationKind.Variable, v)),
                resolvedFieldTypes, ctx, asset.AssetId);

        // Check unmanaged constraint on state fields
        // ⚠ Variables and WorkingState only — Parameters are deliberately NOT state-struct fields.
        CheckUnmanagedConstraint(
            asset.Declarations.Of(DeclarationKind.Variable),     ctx, asset.AssetId, "Instance state");
        CheckUnmanagedConstraint(
            asset.Declarations.Of(DeclarationKind.WorkingState), ctx, asset.AssetId, "AiPrimitive WorkingState");

        // Two-pass wildcard resolution
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var graph in asset.Graphs)
            {
                foreach (var node in graph.Nodes)
                {
                    foreach (var pin in node.Pins.Where(p => !p.IsExec))
                    {
                        if (resolvedPinTypes.ContainsKey(pin.Id)) continue;
                        if (string.IsNullOrEmpty(pin.TypeRef.TypeId)) continue;

                        if (ctx.TypeRegistry.TryResolve(pin.TypeRef, out var resolved))
                            resolvedPinTypes[pin.Id] = resolved;
                        // Wildcard resolution attempted on second pass via wildcard propagation
                    }

                    // Wildcard propagation for ArrayMakeNode / ArrayGetNode
                    if (pass == 1)
                    {
                        TryPropagateWildcard(node, graph, resolvedPinTypes, ctx, asset.AssetId);
                    }
                }
            }
        }

        // After two passes, emit BP1500 for still-unresolved non-empty type refs
        // and BP1502 for wildcard nodes
        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                foreach (var pin in node.Pins.Where(p => !p.IsExec))
                {
                    if (resolvedPinTypes.ContainsKey(pin.Id)) continue;
                    if (string.IsNullOrEmpty(pin.TypeRef.TypeId)) continue;

                    string code = node is ArrayMakeNode or ArrayGetNode
                        ? DiagnosticCodes.BP1502
                        : DiagnosticCodes.BP1500;
                    ctx.Diagnostics.Add(Diagnostic.Error(code,
                        $"Pin type '{pin.TypeRef.TypeId}' could not be resolved.",
                        asset.AssetId, graph.Id, node.Id, pin.Id));
                }
            }
        }

        // Verify link type compatibility
        foreach (var graph in asset.Graphs)
            foreach (var link in graph.Links)
                VerifyLinkTypes(link, graph, resolvedPinTypes, ctx);

        return new TypedAsset(asset, resolvedPinTypes, resolvedFieldTypes);
    }

    /// <summary>
    /// <b>U-11 — ONE resolver over every declaration kind.</b>
    ///
    /// <para>
    /// ⛔ <b>This was two near-identical overloads</b>, one per backing type, and the duplication had
    /// already cost something: <c>U-7</c>'s <c>BP1671</c> rail landed on the <c>VariableDecl</c> half
    /// first and had to be applied to the other by hand. ⭐ <c>BlueprintDeclaration</c> removes the
    /// type difference that forced the split.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>The <c>BP1504</c> fixed-list check lived on the <c>VariableDecl</c> overload only</b>, and
    /// the merged method now applies it to every kind. ⭐ <b>That asymmetry was safe for a reason, and
    /// the reason is upstream:</b> <c>Stage2</c>'s <c>BP1507</c> already refuses a <c>Parameter</c>
    /// carrying a fixed-list type outright, so the arm this widens is unreachable for parameters in a
    /// compile that got here. ⚠ Independently measured as a corpus no-op before merging — across all
    /// 58 shipped assets, declarations with <c>Capacity &gt; 0</c> are <b>Parameters 0 · WorkingState
    /// 0 · Variables 1</b> — and the golden set confirms it.
    /// </para>
    /// </summary>
    private static void ResolveFieldTypes(
        IEnumerable<BlueprintDeclaration> fields,
        Dictionary<Guid, IrTypeRef> result,
        ValidationContext ctx,
        Guid assetId)
    {
        foreach (var f in fields)
        {
            // FC-2/LV-1 (BP1504): a fixed-list declaration's InitialLength must stay within
            // [0, Capacity] -- checked BEFORE resolve so a bad declaration is reported as itself,
            // not as a confusing unresolvable-type BP1500.
            if (f.Type.Capacity > 0
                && (f.Type.InitialLength < 0 || f.Type.InitialLength > f.Type.Capacity))
            {
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1504,
                    $"Fixed-list variable '{f.Name}': InitialLength {f.Type.InitialLength} is outside "
                    + $"[0, Capacity={f.Type.Capacity}].", assetId));
                continue;
            }

            if (TryResolveFieldType(ctx, f.Type, out var resolved, out bool trustedVerbatim))
            {
                // U-7 / BP-228: the AN2 path accepted this id because it CONTAINS A DOT, not because
                // anything checked it. When an oracle is available, ask.
                if (trustedVerbatim && !TypeExistsPerOracle(ctx, f.Type.TypeId))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1671,
                        $"Variable '{f.Name}' is declared as type '{f.Type.TypeId}', which does not exist. "
                        + "Check the fully-qualified name, or the assembly that declares it.", assetId));
                    continue;
                }
                result[f.Id] = resolved;
            }
            else
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1500,
                    $"Field type '{f.Type.TypeId}' does not resolve.", assetId));
        }
    }

    /// <summary>
    /// Resolves a field/variable type. Falls back to the AN2 "trust the string" project-type path for a
    /// dotted FQN the registry doesn't know — so a struct-typed Variable (Q#14 Option B) resolves even
    /// though the reflection-less compiler can't verify it. Size is a placeholder (the real State layout +
    /// <c>StateSize => Unsafe.SizeOf&lt;State&gt;()</c> come from the generated struct at compile time).
    /// </summary>
    /// <param name="trustedVerbatim">
    /// U-7 — true when the type was accepted by the AN2 <i>"contains a dot ⇒ it must be a project
    /// type"</i> fallback rather than resolved by the registry. ⭐ Only these ids need an oracle: a
    /// primitive is known outright, and asking about one would make the rail depend on every oracle
    /// knowing <c>System.Int32</c>.
    /// </param>
    private static bool TryResolveFieldType(
        ValidationContext ctx, BlueprintTypeRef type, out IrTypeRef resolved, out bool trustedVerbatim)
    {
        trustedVerbatim = false;
        if (ctx.TypeRegistry.TryResolve(type, out resolved)) return true;
        var id = type.TypeId;
        if (!string.IsNullOrEmpty(id)
            && !id.StartsWith("global::", StringComparison.Ordinal)
            && id.IndexOf('.') >= 0)   // looks like a project FQN (netstandard2.0: no string.Contains(char))
        {
            if (ctx.TypeRegistry.TryResolve(
                    new BlueprintTypeRef { TypeId = "global::" + id, IsArray = type.IsArray }, out resolved))
            {
                // The AN2 path guesses a 4-byte size; for a real project struct that's a placeholder, so
                // flag it — StateFields layout must then use runtime offsets, not baked field-size sums.
                resolved = resolved with { SizeReliable = false };
                trustedVerbatim = true;   // U-7: accepted on the strength of a dot
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// U-7 / <c>BP-228</c> — asks the compile's type oracle whether <paramref name="typeId"/> names
    /// anything.
    ///
    /// <para>
    /// ⭐⭐ <b>No oracle ⇒ no opinion.</b> Returning <c>true</c> when
    /// <c>CompileOptions.ClrSignatureResolver</c> is null is the fallback contract, and it is
    /// load-bearing: ⚠ <b>exactly one production site supplies a resolver</b>
    /// (<c>BlueprintIncrementalGenerator</c>). Every unit test, every in-memory <c>.Succeeded</c>
    /// check and the golden harness pass <c>null</c>, so a rail that fired without an oracle would
    /// redden them for a reason unrelated to the asset.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>The corollary, stated rather than hidden:</b> this rail protects the BUILD, which is where
    /// the defect actually bit (a `CS0246` naming a generated file). It does <b>not</b> protect the
    /// in-editor compile, because that path supplies no oracle — wiring one there is <c>U-8</c>'s
    /// question, not this rail's.
    /// </para>
    /// </summary>
    private static bool TypeExistsPerOracle(ValidationContext ctx, string typeId)
        => ctx.ClrSignatureResolver is not { } oracle || oracle.TypeExists(typeId);

    private static void CheckUnmanagedConstraint(
        IEnumerable<BlueprintDeclaration> fields,
        ValidationContext ctx,
        Guid assetId,
        string context)
    {
        foreach (var f in fields)
        {
            if (!ctx.TypeRegistry.TryResolve(f.Type, out var resolved)) continue;
            if (!resolved.IsUnmanaged)
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1503,
                    $"{context} field '{f.Name}' has managed type '{resolved.FullName}'. "
                    + "Only unmanaged (blittable) types are allowed in state structs.",
                    assetId));
        }
    }

    private static void TryPropagateWildcard(
        Node node,
        Graph graph,
        Dictionary<Guid, IrTypeRef> pinTypes,
        ValidationContext ctx,
        Guid assetId)
    {
        if (node is not (ArrayMakeNode or ArrayGetNode)) return;

        // For ArrayMakeNode: infer output type from first input.
        if (node is ArrayMakeNode amn)
        {
            var inputPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In");
            if (inputPin is null) return;
            if (!pinTypes.TryGetValue(inputPin.Id, out var elemType)) return;

            var arrayType = new IrTypeRef
            {
                FullName    = elemType.FullName + "[]",
                IsArray     = true,
                ElementType = elemType,
                IsUnmanaged = false,
                SizeBytes   = 0,
            };

            var outputPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out");
            if (outputPin is not null && !pinTypes.ContainsKey(outputPin.Id))
                pinTypes[outputPin.Id] = arrayType;
        }
        // For ArrayGetNode: infer element type from input array.
        else if (node is ArrayGetNode agn)
        {
            var inputPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In");
            if (inputPin is null) return;
            if (!pinTypes.TryGetValue(inputPin.Id, out var arrayType)) return;
            if (arrayType.ElementType is null) return;

            var outputPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out");
            if (outputPin is not null && !pinTypes.ContainsKey(outputPin.Id))
                pinTypes[outputPin.Id] = arrayType.ElementType;
        }
    }

    private static void VerifyLinkTypes(
        Link link,
        Graph graph,
        Dictionary<Guid, IrTypeRef> pinTypes,
        ValidationContext ctx)
    {
        if (!pinTypes.TryGetValue(link.FromPinId, out var fromType)) return;
        if (!pinTypes.TryGetValue(link.ToPinId,   out var toType))   return;

        if (fromType.FullName == toType.FullName) return;
        if (ctx.TypeRegistry.TryGetCoercion(fromType, toType, out _)) return;
        // System.Object pins are typed-unknown placeholders (e.g. CLR calls rehydrated without
        // reflection in the MSBuild host; CA-07c's ComponentItemCountNode.Collection, which has no
        // ElementTypeFqn of its own and so is ALWAYS "System.Object" -- see
        // Stage0_Rehydrate.EnrichComponentItemCountPins); suppress mismatch to let the graph compile.
        // StaticTypeRegistry.TryResolve wraps an array element's FullName as "ElementFullName[]"
        // (e.g. "System.Object[]"), so the wildcard check must strip that suffix too -- otherwise a
        // REAL wired collection (e.g. "System.Int32[]" -> "System.Object[]") would be flagged as a
        // mismatch even though the scalar "System.Int32" -> "System.Object" case is explicitly fine.
        if (WildcardFullName(fromType) == "System.Object" || WildcardFullName(toType) == "System.Object") return;

        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1501,
            $"Link type mismatch: '{fromType.FullName}' -> '{toType.FullName}' -- no coercion.",
            ctx.AssetId, graph.Id, link.FromNodeId, link.FromPinId));
    }

    /// <summary>
    /// The type's element FullName when it's an array wrapper (strips the trailing "[]"
    /// <see cref="Hrot.Blueprints.Core.Compiler.Catalogs.StaticTypeRegistry.TryResolve"/> appends),
    /// otherwise the type's own FullName verbatim. Used ONLY by <see cref="VerifyLinkTypes"/>'s
    /// System.Object wildcard check -- an array of the placeholder type is just as much a
    /// "typed-unknown" as the scalar placeholder itself.
    /// </summary>
    private static string WildcardFullName(IrTypeRef type)
        => type.IsArray && type.ElementType != null ? type.ElementType.FullName : type.FullName;
}

