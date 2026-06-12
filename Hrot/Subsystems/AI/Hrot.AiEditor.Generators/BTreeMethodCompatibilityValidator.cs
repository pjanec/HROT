using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// Validates that every bound Action/Condition leaf in a BTree asset has a method
/// whose signature is compatible with <c>NodeLogicDelegate&lt;TBB,TCtx&gt;</c>
/// (i.e. <c>NodeStatus Method(ref TBB, ref BehaviorTreeState, ref TCtx, int)</c>).
///
/// An incompatible binding causes the whole asset to be skipped with BTREE0002
/// instead of breaking the <c>Hrot.AI.Behaviors</c> build.
///
/// Design note (incrementality): this validator is invoked from the generator's
/// <c>RegisterSourceOutput</c> that is combined with the full <c>CompilationProvider</c>.
/// This means generation re-runs on every compilation change (not just asset changes).
/// This is acceptable given the small number of .btree.json assets; a fancier
/// incremental symbol extraction is left as future work (VE-DEBT-003).
/// </summary>
internal static class BTreeMethodCompatibilityValidator
{
    private const string NodeStatusFqn         = "Fbt.NodeStatus";
    private const string BehaviorTreeStateFqn  = "Fbt.BehaviorTreeState";

    /// <summary>
    /// Validates all reachable bound Action/Condition leaves in <paramref name="dto"/>.
    /// Returns <c>null</c> if all bindings are valid; otherwise returns a human-readable
    /// reason string to embed in a BTREE0002 diagnostic.
    /// </summary>
    internal static string? Validate(BehaviorTreeAssetDto dto, Compilation compilation)
    {
        // Build the expected type symbols from the asset's declared BB/Ctx names.
        string bbTypeName  = dto.BlackboardTypeName;
        string ctxTypeName = dto.ContextTypeName;

        // Resolve BehaviorTreeState once — it is the same across all assets.
        INamedTypeSymbol? behaviorTreeStateSymbol =
            compilation.GetTypeByMetadataName(BehaviorTreeStateFqn);

        // Resolve NodeStatus return type.
        INamedTypeSymbol? nodeStatusSymbol =
            compilation.GetTypeByMetadataName(NodeStatusFqn);

        // Resolve TBB and TCtx from the asset's declared type names.
        INamedTypeSymbol? bbSymbol  = ResolveType(compilation, bbTypeName);
        INamedTypeSymbol? ctxSymbol = ResolveType(compilation, ctxTypeName);

        // Walk reachable nodes (mirror the emitter's traversal: start from entry).
        var nodeById = new Dictionary<Guid, BTreeNodeDto>(dto.Nodes.Count);
        foreach (var n in dto.Nodes)
            nodeById[n.VisualId] = n;

        // Determine entry node (same logic as BTreeEmitCore.EmitCreateBuilder).
        BTreeNodeDto? entry = null;
        var root = null as BTreeNodeDto;
        foreach (var n in dto.Nodes)
        {
            if (n is BTreeRootNodeDto)
            {
                root = n;
                break;
            }
        }

        if (root != null)
        {
            if (root.ChildVisualIds.Count > 0 &&
                nodeById.TryGetValue(root.ChildVisualIds[0], out var entryChild))
            {
                entry = entryChild;
            }
            // else: no children — no leaves to validate
        }
        else if (dto.Nodes.Count > 0)
        {
            entry = dto.Nodes[0];
        }

        if (entry == null)
            return null; // nothing to validate

        // DFS to find all reachable Action/Condition leaves with a bound MethodFqn.
        var visited  = new HashSet<Guid>();
        var toVisit  = new Stack<BTreeNodeDto>();
        toVisit.Push(entry);

        while (toVisit.Count > 0)
        {
            var node = toVisit.Pop();
            if (!visited.Add(node.VisualId))
                continue; // cycle guard — BT-14 already catches cycles; just don't recurse

            // Check Action/Condition leaves.
            if (node is BTreeActionNodeDto actNode)
            {
                var p = actNode.Action;
                if (p != null && !string.IsNullOrEmpty(p.MethodFqn))
                {
                    string? reason = CheckPayload(
                        p.MethodFqn, p.DelegateShape,
                        compilation, bbSymbol, ctxSymbol,
                        behaviorTreeStateSymbol, nodeStatusSymbol,
                        bbTypeName, ctxTypeName);
                    if (reason != null)
                        return $"Action leaf {node.VisualId:D} binds '{p.MethodFqn}': {reason}";
                }
            }
            else if (node is BTreeConditionNodeDto condNode)
            {
                var p = condNode.Condition;
                if (p != null && !string.IsNullOrEmpty(p.MethodFqn))
                {
                    string? reason = CheckPayload(
                        p.MethodFqn, p.DelegateShape,
                        compilation, bbSymbol, ctxSymbol,
                        behaviorTreeStateSymbol, nodeStatusSymbol,
                        bbTypeName, ctxTypeName);
                    if (reason != null)
                        return $"Condition leaf {node.VisualId:D} binds '{p.MethodFqn}': {reason}";
                }
            }

            // Push children for traversal.
            foreach (var childId in node.ChildVisualIds)
            {
                if (nodeById.TryGetValue(childId, out var child))
                    toVisit.Push(child);
            }
        }

        return null; // all reachable leaves are valid
    }

    private static string? CheckPayload(
        string methodFqn,
        BTreeDelegateShapeDto delegateShape,
        Compilation compilation,
        INamedTypeSymbol? bbSymbol,
        INamedTypeSymbol? ctxSymbol,
        INamedTypeSymbol? behaviorTreeStateSymbol,
        INamedTypeSymbol? nodeStatusSymbol,
        string bbTypeName,
        string ctxTypeName)
    {
        // TODO VE-DEBT-002: support reusable/expression-target binding validation.
        if (delegateShape == BTreeDelegateShapeDto.ThreeParamReusable)
            return "ThreeParamReusable delegate shape is not supported by the generator (VE-DEBT-002); bind a FourParamFull method instead";

        // Resolve the method symbol.
        IMethodSymbol? method = ResolveMethod(compilation, methodFqn);
        if (method == null)
            return $"method '{methodFqn}' could not be resolved in the compilation; ensure the declaring assembly is referenced";

        // Must be public and static.
        if (!method.IsStatic)
            return $"method '{methodFqn}' is not static";
        if (method.DeclaredAccessibility != Accessibility.Public)
            return $"method '{methodFqn}' is not public";

        // Return type must be Fbt.NodeStatus.
        if (nodeStatusSymbol == null)
            return "Fbt.NodeStatus could not be resolved; ensure Fbt.Kernel is referenced";
        if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, nodeStatusSymbol))
            return $"method '{methodFqn}' returns '{method.ReturnType.ToDisplayString()}' but NodeLogicDelegate requires Fbt.NodeStatus";

        // Must have exactly 4 parameters.
        if (method.Parameters.Length != 4)
            return $"method '{methodFqn}' has {method.Parameters.Length} parameter(s) but NodeLogicDelegate requires exactly 4";

        // Param 0: ref TBB (the asset's blackboard type).
        if (bbSymbol == null)
            return $"blackboard type '{bbTypeName}' could not be resolved; ensure the assembly is referenced";
        string? p0Err = CheckRefParam(method.Parameters[0], bbSymbol, 0, "blackboard (TBB)", methodFqn);
        if (p0Err != null) return p0Err;

        // Param 1: ref Fbt.BehaviorTreeState.
        if (behaviorTreeStateSymbol == null)
            return "Fbt.BehaviorTreeState could not be resolved; ensure Fbt.Kernel is referenced";
        string? p1Err = CheckRefParam(method.Parameters[1], behaviorTreeStateSymbol, 1, "BehaviorTreeState", methodFqn);
        if (p1Err != null) return p1Err;

        // Param 2: ref TCtx (the asset's context type).
        if (ctxSymbol == null)
            return $"context type '{ctxTypeName}' could not be resolved; ensure the assembly is referenced";
        string? p2Err = CheckRefParam(method.Parameters[2], ctxSymbol, 2, "context (TCtx)", methodFqn);
        if (p2Err != null) return p2Err;

        // Param 3: int paramIndex (no ref).
        var p3 = method.Parameters[3];
        if (p3.RefKind != RefKind.None)
            return $"method '{methodFqn}' param 3 (paramIndex) must not be ref/out/in; got '{p3.RefKind}'";
        if (p3.Type.SpecialType != SpecialType.System_Int32)
            return $"method '{methodFqn}' param 3 must be System.Int32; got '{p3.Type.ToDisplayString()}'";

        return null; // valid
    }

    private static string? CheckRefParam(
        IParameterSymbol param,
        INamedTypeSymbol expectedType,
        int index,
        string role,
        string methodFqn)
    {
        if (param.RefKind != RefKind.Ref)
            return $"method '{methodFqn}' param {index} ({role}) must be 'ref'; got '{param.RefKind}'";
        if (!SymbolEqualityComparer.Default.Equals(param.Type, expectedType))
            return $"method '{methodFqn}' param {index} ({role}) has type '{param.Type.ToDisplayString()}' but expected '{expectedType.ToDisplayString()}'";
        return null;
    }

    /// <summary>
    /// Resolves a fully-qualified type name (e.g. "Fdp.Toolkit.Behavior.Components.BrainBlackboard")
    /// to its Roslyn symbol in the given compilation.
    /// </summary>
    private static INamedTypeSymbol? ResolveType(Compilation compilation, string fqn)
    {
        if (string.IsNullOrEmpty(fqn))
            return null;
        return compilation.GetTypeByMetadataName(fqn);
    }

    /// <summary>
    /// Resolves a fully-qualified method reference (e.g.
    /// "Hrot.AI.Behaviors.Brains.CgfNodes.Action_Wander") to its Roslyn symbol.
    ///
    /// Strategy: split on the last '.' — left part is the containing type, right part
    /// is the method name — then look up all overloads.  Returns the first match
    /// (there should be exactly one for BTree action/condition methods, which are
    /// static and not overloaded).
    /// </summary>
    private static IMethodSymbol? ResolveMethod(Compilation compilation, string methodFqn)
    {
        if (string.IsNullOrEmpty(methodFqn))
            return null;

        int lastDot = methodFqn.LastIndexOf('.');
        if (lastDot <= 0)
            return null;

        string typeFqn   = methodFqn.Substring(0, lastDot);
        string methodName = methodFqn.Substring(lastDot + 1);

        INamedTypeSymbol? typeSymbol = compilation.GetTypeByMetadataName(typeFqn);
        if (typeSymbol == null)
            return null;

        // Find the first public static method with the given name.
        foreach (var member in typeSymbol.GetMembers(methodName))
        {
            if (member is IMethodSymbol m && m.IsStatic &&
                m.DeclaredAccessibility == Accessibility.Public)
            {
                return m;
            }
        }

        return null;
    }
}
