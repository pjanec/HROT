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
                        p.ExpressionTargetField, dto.Blackboard,
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
                        p.ExpressionTargetField, dto.Blackboard,
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
        string? expressionTargetField,
        BlackboardBlockDto blackboard,
        Compilation compilation,
        INamedTypeSymbol? bbSymbol,
        INamedTypeSymbol? ctxSymbol,
        INamedTypeSymbol? behaviorTreeStateSymbol,
        INamedTypeSymbol? nodeStatusSymbol,
        string bbTypeName,
        string ctxTypeName)
    {
        // S1-4: ThreeParamReusable is now validated via the 3-param shape check.
        // A ThreeParamReusable binding is valid when:
        //   1. The method resolves to a public static method.
        //   2. The method has exactly 3 parameters: (ref TDto, ref BehaviorTreeState, ref TCtx)
        //      and returns NodeStatus.
        //   3. ExpressionTargetField names a variable in the managed blackboard block
        //      whose TypeId matches param-0's type FQN.
        // If any of those conditions fail the binding is skipped (BTREE0002), not hard-errored.
        if (delegateShape == BTreeDelegateShapeDto.ThreeParamReusable)
        {
            return CheckThreeParamReusable(
                methodFqn, expressionTargetField, blackboard,
                compilation, behaviorTreeStateSymbol, nodeStatusSymbol, ctxSymbol,
                ctxTypeName);
        }

        // S2-1: ThreeParamReusableStateful uses the 4-param stateful shape:
        //   (ref TDto, ref TWorkingState, ref BehaviorTreeState, ref TCtx)
        // This is NOT the FourParamFull shape — param-0 is a DTO (not TBB) and param-1
        // is a WorkingState struct (not TBB). Validate it via a dedicated check.
        if (delegateShape == BTreeDelegateShapeDto.ThreeParamReusableStateful)
        {
            return CheckThreeParamReusableStateful(
                methodFqn, expressionTargetField, blackboard,
                compilation, behaviorTreeStateSymbol, nodeStatusSymbol, ctxSymbol,
                ctxTypeName);
        }

        // I2/I3: AiPrimitiveTickCore composes a blueprint AiPrimitive as a host node. The bound method
        // is the blueprint's generated TickCore with the signature
        //   (ref Params, ref WorkingState, Fdp.Core.Entity self, Fdp.Core.EntityRepository world, float time)
        // — 5 params, distinct from every other shape. Validate it via a dedicated check.
        if (delegateShape == BTreeDelegateShapeDto.AiPrimitiveTickCore)
        {
            return CheckAiPrimitiveTickCore(
                methodFqn, expressionTargetField, blackboard,
                compilation, nodeStatusSymbol);
        }

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

    /// <summary>
    /// S1-4: Validates a ThreeParamReusable binding.
    /// Accepts when:
    ///   - method resolves, is public static, returns NodeStatus
    ///   - method has exactly 3 ref parameters: (ref TDto, ref BehaviorTreeState, ref TCtx)
    ///   - ExpressionTargetField names a variable in the blackboard block
    ///   - that variable's TypeId matches param-0's type FQN (type-safe binding)
    /// Returns null on success, reason string on failure.
    /// </summary>
    private static string? CheckThreeParamReusable(
        string methodFqn,
        string? expressionTargetField,
        BlackboardBlockDto blackboard,
        Compilation compilation,
        INamedTypeSymbol? behaviorTreeStateSymbol,
        INamedTypeSymbol? nodeStatusSymbol,
        INamedTypeSymbol? ctxSymbol,
        string ctxTypeName)
    {
        // ExpressionTargetField must be set.
        if (string.IsNullOrEmpty(expressionTargetField))
            return $"ThreeParamReusable binding has no ExpressionTargetField — set the target variable in the editor";

        // The blackboard block must be managed and contain the variable.
        if (!blackboard.Managed)
            return $"ThreeParamReusable binding requires a managed blackboard (Managed=true); got Managed=false";

        BlackboardVariableDto? targetVar = null;
        foreach (var v in blackboard.Variables)
        {
            if (string.Equals(v.Name, expressionTargetField, StringComparison.Ordinal))
            {
                targetVar = v;
                break;
            }
        }
        if (targetVar == null)
            return $"ThreeParamReusable: variable '{expressionTargetField}' not found in the managed blackboard block";

        // Resolve the method.
        IMethodSymbol? method = ResolveMethod(compilation, methodFqn);
        if (method == null)
            return $"method '{methodFqn}' could not be resolved in the compilation; ensure the declaring assembly is referenced";

        if (!method.IsStatic)
            return $"method '{methodFqn}' is not static";
        if (method.DeclaredAccessibility != Accessibility.Public)
            return $"method '{methodFqn}' is not public";

        // Return type must be NodeStatus.
        if (nodeStatusSymbol == null)
            return "Fbt.NodeStatus could not be resolved; ensure Fbt.Kernel is referenced";
        if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, nodeStatusSymbol))
            return $"method '{methodFqn}' returns '{method.ReturnType.ToDisplayString()}' but 3-param reusable requires Fbt.NodeStatus";

        // Must have exactly 3 parameters.
        if (method.Parameters.Length != 3)
            return $"method '{methodFqn}' has {method.Parameters.Length} parameter(s) but ThreeParamReusable requires exactly 3 (ref TDto, ref BehaviorTreeState, ref TCtx)";

        // Param 0: ref TDto — must be a ref struct matching the variable's TypeId.
        var param0 = method.Parameters[0];
        if (param0.RefKind != RefKind.Ref)
            return $"method '{methodFqn}' param 0 must be 'ref'; got '{param0.RefKind}'";

        // Get the FQN of param0's type and compare with the variable's TypeId.
        // S1-2b: The symbol display format uses '.' for nested types but the asset
        // TypeId uses the CLR metadata form with '+' (e.g. "Outer+Inner").
        // Normalize both sides to use '.' before comparing so a nested-struct DTO
        // binding validates correctly regardless of which separator was used.
        string param0TypeFqn = param0.Type.ToDisplayString(
            new SymbolDisplayFormat(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));

        string varTypeId = targetVar.Type?.TypeId ?? string.Empty;

        // Normalize nested-type separators to '.' on both sides for comparison.
        string param0TypeNormalized = param0TypeFqn.Replace('+', '.');
        string varTypeNormalized    = varTypeId.Replace('+', '.');

        if (!string.Equals(param0TypeNormalized, varTypeNormalized, StringComparison.Ordinal))
            return $"method '{methodFqn}' param 0 type '{param0TypeFqn}' does not match variable '{expressionTargetField}' type '{varTypeId}'; ensure the DTO type and the blackboard variable type are the same";

        // Param 1: ref BehaviorTreeState.
        var param1 = method.Parameters[1];
        if (param1.RefKind != RefKind.Ref)
            return $"method '{methodFqn}' param 1 must be 'ref BehaviorTreeState'; got refkind '{param1.RefKind}'";
        if (behaviorTreeStateSymbol != null &&
            !SymbolEqualityComparer.Default.Equals(param1.Type, behaviorTreeStateSymbol))
            return $"method '{methodFqn}' param 1 must be 'ref Fbt.BehaviorTreeState'; got '{param1.Type.ToDisplayString()}'";

        // Param 2: ref TCtx.
        var param2 = method.Parameters[2];
        if (param2.RefKind != RefKind.Ref)
            return $"method '{methodFqn}' param 2 must be 'ref TCtx'; got refkind '{param2.RefKind}'";
        if (ctxSymbol != null &&
            !SymbolEqualityComparer.Default.Equals(param2.Type, ctxSymbol))
            return $"method '{methodFqn}' param 2 type '{param2.Type.ToDisplayString()}' does not match context type '{ctxTypeName}'";

        return null; // valid
    }

    /// <summary>
    /// S2-1: Validates a ThreeParamReusableStateful binding.
    /// The stateful shape has 4 parameters: (ref TDto, ref TWorkingState, ref BehaviorTreeState, ref TCtx).
    /// Unlike FourParamFull, param-0 is a DTO type (matching the blackboard variable) and
    /// param-1 is a WorkingState struct (projected from the partition slot).
    /// Accepts when:
    ///   - method resolves, is public static, returns NodeStatus
    ///   - method has exactly 4 parameters: (ref TDto, ref TWorkingState, ref BehaviorTreeState, ref TCtx)
    ///   - ExpressionTargetField names a variable in the managed blackboard block
    ///   - that variable's TypeId (normalized) matches param-0's type FQN (type-safe binding)
    /// Returns null on success, reason string on failure (BTREE0002 skip, not build break).
    /// </summary>
    private static string? CheckThreeParamReusableStateful(
        string methodFqn,
        string? expressionTargetField,
        BlackboardBlockDto blackboard,
        Compilation compilation,
        INamedTypeSymbol? behaviorTreeStateSymbol,
        INamedTypeSymbol? nodeStatusSymbol,
        INamedTypeSymbol? ctxSymbol,
        string ctxTypeName)
    {
        // ExpressionTargetField must be set.
        if (string.IsNullOrEmpty(expressionTargetField))
            return $"ThreeParamReusableStateful binding has no ExpressionTargetField — set the target variable in the editor";

        // The blackboard block must be managed and contain the variable.
        if (!blackboard.Managed)
            return $"ThreeParamReusableStateful binding requires a managed blackboard (Managed=true); got Managed=false";

        BlackboardVariableDto? targetVar = null;
        foreach (var v in blackboard.Variables)
        {
            if (string.Equals(v.Name, expressionTargetField, StringComparison.Ordinal))
            {
                targetVar = v;
                break;
            }
        }
        if (targetVar == null)
            return $"ThreeParamReusableStateful: variable '{expressionTargetField}' not found in the managed blackboard block";

        // Resolve the method.
        IMethodSymbol? method = ResolveMethod(compilation, methodFqn);
        if (method == null)
            return $"method '{methodFqn}' could not be resolved in the compilation; ensure the declaring assembly is referenced";

        if (!method.IsStatic)
            return $"method '{methodFqn}' is not static";
        if (method.DeclaredAccessibility != Accessibility.Public)
            return $"method '{methodFqn}' is not public";

        // Return type must be NodeStatus.
        if (nodeStatusSymbol == null)
            return "Fbt.NodeStatus could not be resolved; ensure Fbt.Kernel is referenced";
        if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, nodeStatusSymbol))
            return $"method '{methodFqn}' returns '{method.ReturnType.ToDisplayString()}' but ThreeParamReusableStateful requires Fbt.NodeStatus";

        // Must have exactly 4 parameters: (ref TDto, ref TWorkingState, ref BehaviorTreeState, ref TCtx).
        if (method.Parameters.Length != 4)
            return $"method '{methodFqn}' has {method.Parameters.Length} parameter(s) but ThreeParamReusableStateful requires exactly 4 (ref TDto, ref TWorkingState, ref BehaviorTreeState, ref TCtx)";

        // Param 0: ref TDto — must match the variable's TypeId.
        var param0 = method.Parameters[0];
        if (param0.RefKind != RefKind.Ref)
            return $"method '{methodFqn}' param 0 must be 'ref TDto'; got '{param0.RefKind}'";

        string param0TypeFqn = param0.Type.ToDisplayString(
            new SymbolDisplayFormat(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));

        string varTypeId = targetVar.Type?.TypeId ?? string.Empty;
        string param0TypeNormalized = param0TypeFqn.Replace('+', '.');
        string varTypeNormalized    = varTypeId.Replace('+', '.');

        if (!string.Equals(param0TypeNormalized, varTypeNormalized, StringComparison.Ordinal))
            return $"method '{methodFqn}' param 0 type '{param0TypeFqn}' does not match variable '{expressionTargetField}' type '{varTypeId}'";

        // Param 1: ref TWorkingState — must be a ref struct (not validated against a specific type
        // since it comes from the partition slot, not the blackboard). Just check it is a ref param.
        var param1 = method.Parameters[1];
        if (param1.RefKind != RefKind.Ref)
            return $"method '{methodFqn}' param 1 (WorkingState) must be 'ref'; got '{param1.RefKind}'";

        // Param 2: ref BehaviorTreeState.
        var param2 = method.Parameters[2];
        if (param2.RefKind != RefKind.Ref)
            return $"method '{methodFqn}' param 2 must be 'ref BehaviorTreeState'; got refkind '{param2.RefKind}'";
        if (behaviorTreeStateSymbol != null &&
            !SymbolEqualityComparer.Default.Equals(param2.Type, behaviorTreeStateSymbol))
            return $"method '{methodFqn}' param 2 must be 'ref Fbt.BehaviorTreeState'; got '{param2.Type.ToDisplayString()}'";

        // Param 3: ref TCtx.
        var param3 = method.Parameters[3];
        if (param3.RefKind != RefKind.Ref)
            return $"method '{methodFqn}' param 3 must be 'ref TCtx'; got refkind '{param3.RefKind}'";
        if (ctxSymbol != null &&
            !SymbolEqualityComparer.Default.Equals(param3.Type, ctxSymbol))
            return $"method '{methodFqn}' param 3 type '{param3.Type.ToDisplayString()}' does not match context type '{ctxTypeName}'";

        return null; // valid
    }

    /// <summary>
    /// I2/I3: validates a blueprint-AiPrimitive composition binding (<see cref="BTreeDelegateShapeDto.AiPrimitiveTickCore"/>).
    /// The bound method is the blueprint's generated <c>TickCore</c>:
    ///   <c>(ref Params, ref WorkingState, Fdp.Core.Entity self, Fdp.Core.EntityRepository world, float time)</c>.
    /// Param 0 (Params) must match the target variable's TypeId (bin-packed into BrainBlackboard);
    /// param 1 (WorkingState) is a ref struct projected from the partition slot; params 2-4 are the
    /// world-context args passed by the bridge thunk. Returns null when valid, else a BTREE0002 reason.
    /// </summary>
    private static string? CheckAiPrimitiveTickCore(
        string methodFqn,
        string? expressionTargetField,
        BlackboardBlockDto blackboard,
        Compilation compilation,
        INamedTypeSymbol? nodeStatusSymbol)
    {
        if (string.IsNullOrEmpty(expressionTargetField))
            return "AiPrimitiveTickCore binding has no ExpressionTargetField — set the target variable in the editor";
        if (!blackboard.Managed)
            return "AiPrimitiveTickCore binding requires a managed blackboard (Managed=true); got Managed=false";

        BlackboardVariableDto? targetVar = null;
        foreach (var v in blackboard.Variables)
        {
            if (string.Equals(v.Name, expressionTargetField, StringComparison.Ordinal))
            {
                targetVar = v;
                break;
            }
        }
        if (targetVar == null)
            return $"AiPrimitiveTickCore: variable '{expressionTargetField}' not found in the managed blackboard block";

        IMethodSymbol? method = ResolveMethod(compilation, methodFqn);
        if (method == null)
            return $"method '{methodFqn}' could not be resolved in the compilation; ensure the declaring assembly is referenced";
        if (!method.IsStatic)
            return $"method '{methodFqn}' is not static";
        if (method.DeclaredAccessibility != Accessibility.Public)
            return $"method '{methodFqn}' is not public";
        if (nodeStatusSymbol == null)
            return "Fbt.NodeStatus could not be resolved; ensure Fbt.Kernel is referenced";
        if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, nodeStatusSymbol))
            return $"method '{methodFqn}' returns '{method.ReturnType.ToDisplayString()}' but AiPrimitiveTickCore requires Fbt.NodeStatus";

        if (method.Parameters.Length != 5)
            return $"method '{methodFqn}' has {method.Parameters.Length} parameter(s) but AiPrimitiveTickCore requires exactly 5 (ref Params, ref WorkingState, Fdp.Core.Entity, Fdp.Core.EntityRepository, float)";

        var fmt = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        // Param 0: ref Params — must match the target variable's TypeId.
        var p0 = method.Parameters[0];
        if (p0.RefKind != RefKind.Ref)
            return $"method '{methodFqn}' param 0 (Params) must be 'ref'; got '{p0.RefKind}'";
        string p0Type  = p0.Type.ToDisplayString(fmt).Replace('+', '.');
        string varType = (targetVar.Type?.TypeId ?? string.Empty).Replace('+', '.');
        if (!string.Equals(p0Type, varType, StringComparison.Ordinal))
            return $"method '{methodFqn}' param 0 type '{p0.Type.ToDisplayString()}' does not match variable '{expressionTargetField}' type '{targetVar.Type?.TypeId}'";

        // Param 1: ref WorkingState — projected from the partition slot (type not matched here).
        var p1 = method.Parameters[1];
        if (p1.RefKind != RefKind.Ref)
            return $"method '{methodFqn}' param 1 (WorkingState) must be 'ref'; got '{p1.RefKind}'";

        // Param 2: Fdp.Core.Entity self (by value).
        var p2 = method.Parameters[2];
        if (p2.RefKind != RefKind.None || p2.Type.ToDisplayString(fmt) != "Fdp.Core.Entity")
            return $"method '{methodFqn}' param 2 must be 'Fdp.Core.Entity self' (by value); got '{p2.RefKind} {p2.Type.ToDisplayString()}'";

        // Param 3: Fdp.Core.EntityRepository world (by value).
        var p3 = method.Parameters[3];
        if (p3.RefKind != RefKind.None || p3.Type.ToDisplayString(fmt) != "Fdp.Core.EntityRepository")
            return $"method '{methodFqn}' param 3 must be 'Fdp.Core.EntityRepository world' (by value); got '{p3.RefKind} {p3.Type.ToDisplayString()}'";

        // Param 4: float time (by value).
        var p4 = method.Parameters[4];
        if (p4.RefKind != RefKind.None || p4.Type.SpecialType != SpecialType.System_Single)
            return $"method '{methodFqn}' param 4 must be 'float time'; got '{p4.RefKind} {p4.Type.ToDisplayString()}'";

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
