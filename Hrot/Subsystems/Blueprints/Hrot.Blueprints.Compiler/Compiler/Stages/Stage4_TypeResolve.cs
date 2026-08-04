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

        // Resolve variable/parameter/working-state field types
        ResolveFieldTypes(asset.Variables,    resolvedFieldTypes, ctx, asset.AssetId);
        ResolveFieldTypes(asset.Parameters,   resolvedFieldTypes, ctx, asset.AssetId);
        ResolveFieldTypes(asset.WorkingState, resolvedFieldTypes, ctx, asset.AssetId);

        // Check unmanaged constraint on state fields
        CheckUnmanagedConstraint(asset.Variables,    ctx, asset.AssetId, "Instance state");
        CheckUnmanagedConstraint(asset.WorkingState, ctx, asset.AssetId, "AiPrimitive WorkingState");

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

    private static void ResolveFieldTypes(
        IEnumerable<VariableDecl> fields,
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

            if (TryResolveFieldType(ctx, f.Type, out var resolved))
                result[f.Id] = resolved;
            else
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1500,
                    $"Field type '{f.Type.TypeId}' does not resolve.", assetId));
        }
    }

    private static void ResolveFieldTypes(
        IEnumerable<ParameterDecl> fields,
        Dictionary<Guid, IrTypeRef> result,
        ValidationContext ctx,
        Guid assetId)
    {
        foreach (var f in fields)
        {
            if (TryResolveFieldType(ctx, f.Type, out var resolved))
                result[f.Id] = resolved;
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
    private static bool TryResolveFieldType(ValidationContext ctx, BlueprintTypeRef type, out IrTypeRef resolved)
    {
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
                return true;
            }
        }
        return false;
    }

    private static void CheckUnmanagedConstraint(
        IEnumerable<VariableDecl> fields,
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
        // reflection in the MSBuild host; CA-07c's CollectionItemCountNode.Collection, which has no
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

