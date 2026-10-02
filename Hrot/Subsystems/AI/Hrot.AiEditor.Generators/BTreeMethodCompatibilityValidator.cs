using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// Validates that every bound Action/Condition leaf in a BTree asset binds a method the bridge can call.
///
/// <para>⭐⭐ <c>CE-504</c> slice 4 — <b>one C# node signature.</b> A C# binding is a <c>[SharedAiAction]</c> /
/// <c>[SharedAiCondition]</c> method in one of the shared forms — <c>(ref P, Entity, EntityRepository)</c>,
/// <c>(ref P, ref WS, Entity, EntityRepository)</c>, <c>(Entity, EntityRepository)</c> — the HSM's own signature. ⛔ The
/// BTree-only <c>(ref P, ref BehaviorTreeState, ref TCtx)</c>, its stateful twin and the whole-block
/// <c>(ref TBB, ref BehaviorTreeState, ref TCtx, int)</c> are retired as asset bindings: such a method is reported, never
/// emitted. The blueprint call (<see cref="BTreeDelegateShapeDto.AiPrimitiveTickCore"/>) is a separate, generated contract.
/// 📄 <c>docs/blueprints/DESIGN_BTree_Node_Call_Shapes.md</c>.</para>
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
    private const string NodeStatusFqn = "Fbt.NodeStatus";

    /// <summary>
    /// Validates all reachable bound Action/Condition leaves in <paramref name="dto"/>.
    /// Returns <c>null</c> if all bindings are valid; otherwise returns a human-readable
    /// reason string to embed in a BTREE0002 diagnostic.
    /// </summary>
    /// <param name="blueprintSchemas">
    /// Optional <c>.bp.json</c>-derived schemas (see <see cref="GeneratedBlueprintSchemaCatalog"/>),
    /// used ONLY as a fallback for <see cref="BTreeDelegateShapeDto.AiPrimitiveTickCore"/> bindings
    /// whose method can't be resolved via Roslyn — the Blueprint generator is a sibling
    /// IIncrementalGenerator and its generated <c>{Name}_{Id:X8}_Bp</c> class is never visible to
    /// this compilation snapshot, even for a fully valid real blueprint composition (Option A).
    /// </param>
    internal static string? Validate(
        BehaviorTreeAssetDto dto, Compilation compilation,
        IReadOnlyList<GeneratedBlueprintSchema>? blueprintSchemas = null)
    {
        INamedTypeSymbol? nodeStatusSymbol = compilation.GetTypeByMetadataName(NodeStatusFqn);
        var sharedAi = SharedAiMethodResolver.Make(compilation);

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

            var (p, shape, kind) = node switch
            {
                BTreeActionNodeDto a    => (a.Action, a.DelegateShape, "Action"),
                BTreeConditionNodeDto c => (c.Condition, c.DelegateShape, "Condition"),
                _                       => (null, default(BTreeDelegateShapeDto), ""),
            };
            if (p != null && !string.IsNullOrEmpty(p.MethodFqn))
            {
                string? reason = CheckPayload(
                    p.MethodFqn!, shape, p.ExpressionTargetField, dto.Blackboard,
                    compilation, sharedAi, nodeStatusSymbol, blueprintSchemas);
                if (reason != null)
                    return $"{kind} leaf {node.VisualId:D} binds '{p.MethodFqn}': {reason}";
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
        Func<string, SharedAiMethodInfo?> sharedAi,
        INamedTypeSymbol? nodeStatusSymbol,
        IReadOnlyList<GeneratedBlueprintSchema>? blueprintSchemas)
    {
        switch (delegateShape)
        {
            case BTreeDelegateShapeDto.ThreeParamReusable:
            case BTreeDelegateShapeDto.ThreeParamReusableStateful:
                return CheckSharedWithParams(methodFqn, delegateShape, expressionTargetField, blackboard, compilation, sharedAi);

            // ⭐⭐ CE-504 C-2/C-3 — a shared param-less node (Entity, EntityRepository): it binds no variable.
            case BTreeDelegateShapeDto.NoParams:
                return sharedAi(methodFqn) is { HasParams: false }
                    ? null
                    : $"method '{methodFqn}' takes (Entity, EntityRepository) but is not marked [SharedAiAction]/[SharedAiCondition] (CE-504)";

            // I2/I3: AiPrimitiveTickCore composes a blueprint AiPrimitive as a host node — the generated TickCore
            //   (ref Params, ref WorkingState, Fdp.Core.Entity self, Fdp.Core.EntityRepository world, float time).
            case BTreeDelegateShapeDto.AiPrimitiveTickCore:
                return CheckAiPrimitiveTickCore(
                    methodFqn, expressionTargetField, blackboard,
                    compilation, nodeStatusSymbol, blueprintSchemas);

            default:
                return $"call shape {delegateShape} is not a bindable shape (CE-504)";
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-504</c> slice 4 — a binding with params: the plain <c>(ref P, Entity, EntityRepository)</c> or the stateful
    /// <c>(ref P, ref WS, Entity, EntityRepository)</c> shared form. Accepts when the method resolves, is public static, is a
    /// <c>[SharedAi*]</c> method of the form the shape names, and the bound variable IS its params type (the thunk projects the
    /// variable as that type, so a mismatch would be a silent type-pun). ⛔ The retired BTree-only forms
    /// (<c>ref BehaviorTreeState, ref TCtx</c>) are reported here by name.
    /// </summary>
    private static string? CheckSharedWithParams(
        string methodFqn,
        BTreeDelegateShapeDto shape,
        string? expressionTargetField,
        BlackboardBlockDto blackboard,
        Compilation compilation,
        Func<string, SharedAiMethodInfo?> sharedAi)
    {
        if (string.IsNullOrEmpty(expressionTargetField))
            return $"{shape} binding has no ExpressionTargetField — set the target variable in the editor";
        if (!blackboard.Managed)
            return $"{shape} binding requires a managed blackboard (Managed=true); got Managed=false";

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
            return $"{shape}: variable '{expressionTargetField}' not found in the managed blackboard block";

        IMethodSymbol? method = ResolveMethod(compilation, methodFqn);
        if (method == null)
            return $"method '{methodFqn}' could not be resolved in the compilation; ensure the declaring assembly is referenced";
        if (!method.IsStatic)
            return $"method '{methodFqn}' is not static";
        if (method.DeclaredAccessibility != Accessibility.Public)
            return $"method '{methodFqn}' is not public";

        bool stateful = shape == BTreeDelegateShapeDto.ThreeParamReusableStateful;
        var info = sharedAi(methodFqn);
        if (info == null || !info.HasParams || (info.WorkingStateTypeFqn != null) != stateful)
            return $"method '{methodFqn}' is not a [SharedAiAction]/[SharedAiCondition] method of the form " +
                   (stateful ? "(ref TParams, ref TWorkingState, Entity, EntityRepository)" : "(ref TParams, Entity, EntityRepository)") +
                   "; the BTree-only (…, ref BehaviorTreeState, ref TCtx) forms were retired (CE-504)";

        string want = info.ParamTypeId.Replace('+', '.');
        string have = (targetVar.Type?.TypeId ?? string.Empty).Replace('+', '.');
        return string.Equals(want, have, StringComparison.Ordinal)
            ? null
            : $"[SharedAi] method '{methodFqn}' takes 'ref {info.ParamTypeId}' but is bound to variable " +
              $"'{expressionTargetField}' of type '{targetVar.Type?.TypeId}'; bind a variable of the method's ref type (CE-417)";
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
        INamedTypeSymbol? nodeStatusSymbol,
        IReadOnlyList<GeneratedBlueprintSchema>? blueprintSchemas)
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
        {
            // Option A fallback: methodFqn may point at a REAL blueprint's generated TickCore whose
            // declaring class ({SanitizedName}_{BlueprintId:X8}_Bp) is produced by the sibling
            // Blueprint source generator in the SAME compilation — Roslyn generators cannot see each
            // other's generated output, so ResolveMethod will ALWAYS fail here for a genuine
            // composed-blueprint asset (T31's hand-written DemoAiPrimitiveNodes is unaffected — it's
            // real pre-existing source, so ResolveMethod above already succeeded for it).
            // Validate against the .bp.json schema instead of the (not-yet-visible) compiled symbol.
            if (TryValidateAsGeneratedBlueprintTickCore(methodFqn, targetVar, blueprintSchemas, out string? schemaReason))
                return schemaReason;

            return $"method '{methodFqn}' could not be resolved in the compilation; ensure the declaring assembly is referenced";
        }
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

    /// <summary>
    /// Option A fallback for <see cref="CheckAiPrimitiveTickCore"/>: when the bound method can't be
    /// resolved via Roslyn, checks whether it LOOKS like a generated blueprint's TickCore (naming
    /// convention <c>"{Ns}.{SanitizedName}_{BlueprintId:X8}_Bp.TickCore"</c> — see
    /// <c>AiPrimitiveEmitter.EmitClass</c>/<c>EmitTickCore</c>) and, if so, validates it against the
    /// matching <c>.bp.json</c> schema instead of a compiled symbol.
    /// </summary>
    /// <returns>
    /// <c>false</c> when <paramref name="methodFqn"/> doesn't match the generated-blueprint naming
    /// convention at all — the caller should fall back to the generic "method unresolved" error.
    /// <c>true</c> when it DOES match — the binding is now considered "handled": <paramref name="reason"/>
    /// is <c>null</c> for a valid composition, or a specific BTREE0002 reason otherwise. A recognized
    /// blueprint-shaped binding never silently falls back to the generic Roslyn message, so a
    /// genuinely broken reference (wrong hex id, missing asset, non-AiPrimitive blueprint) is reported
    /// precisely rather than as an opaque "could not be resolved".
    /// </returns>
    private static bool TryValidateAsGeneratedBlueprintTickCore(
        string methodFqn,
        BlackboardVariableDto targetVar,
        IReadOnlyList<GeneratedBlueprintSchema>? blueprintSchemas,
        out string? reason)
    {
        reason = null;

        const string tickCoreSuffix = ".TickCore";
        if (!methodFqn.EndsWith(tickCoreSuffix, StringComparison.Ordinal))
            return false;
        string classFqn = methodFqn.Substring(0, methodFqn.Length - tickCoreSuffix.Length);

        if (!GeneratedBlueprintSchemaCatalog.TryParseGeneratedClassRef(classFqn, out string sanitizedName, out int blueprintId))
            return false; // not shaped like a generated blueprint class — let the generic message stand

        if (blueprintSchemas == null || blueprintSchemas.Count == 0)
        {
            reason = $"method '{methodFqn}' matches the generated-blueprint TickCore naming convention " +
                      "but no *.bp.json AdditionalTexts were available to validate it against";
            return true;
        }

        var schema = GeneratedBlueprintSchemaCatalog.Find(blueprintSchemas, sanitizedName, blueprintId);
        if (schema == null)
        {
            reason = $"method '{methodFqn}' looks like a generated blueprint TickCore (class " +
                      $"'{sanitizedName}_{blueprintId:X8}_Bp') but no .bp.json asset with that sanitized " +
                      "name + BlueprintId was found — ensure the blueprint asset is included in the build";
            return true;
        }

        if (!schema.IsAiPrimitive)
        {
            reason = $"blueprint '{sanitizedName}' (BlueprintId 0x{blueprintId:X8}) is not an AiPrimitive " +
                      "(Dispatch != AiPrimitive) — AiPrimitiveTickCore composition requires an AiPrimitive blueprint";
            return true;
        }

        string expectedParamsTypeId = classFqn + "+Params";
        string varTypeId = targetVar.Type?.TypeId ?? string.Empty;
        if (!string.Equals(expectedParamsTypeId.Replace('+', '.'), varTypeId.Replace('+', '.'), StringComparison.Ordinal))
        {
            reason = $"variable's type '{varTypeId}' does not match the blueprint's generated Params type '{expectedParamsTypeId}'";
            return true;
        }

        reason = null; // valid — recognized generated-blueprint TickCore binding
        return true;
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
    internal static IMethodSymbol? ResolveMethod(Compilation compilation, string methodFqn)
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
