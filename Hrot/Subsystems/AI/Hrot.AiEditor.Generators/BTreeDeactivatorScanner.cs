using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.Emit;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// HAJSON-B: Roslyn-based scanner for <c>[BTreeDeactivatorAttribute]</c>-annotated methods.
///
/// <para>⭐⭐ <c>CE-504</c> slice 4 — <b>a deactivator pairs with its action by METHOD, and is registered per BINDING.</b>
/// 📄 <c>docs/blueprints/DESIGN_BTree_Node_Call_Shapes.md</c> §5 slice 4.</para>
/// <list type="bullet">
///   <item>⭐ it is declared BESIDE its action (same type) and its <c>TargetAction</c> is the action's method FQN; a legacy
///     <c>@offset</c> suffix is tolerated and ignored. ⛔ It used to be matched against a hard-coded <c>"…@0"</c> key, so it
///     paired only with a binding whose variable happened to sit at offset 0.</item>
///   <item>⭐ it takes one of the shared forms — <c>(Entity, EntityRepository)</c>, <c>(ref P, Entity, EntityRepository)</c>,
///     <c>(ref P, ref WS, Entity, EntityRepository)</c> — and the bridge feeds it with the binding's own projection
///     (<see cref="BTreeBridgeEmitCore"/>). The curated route pairs by the same rule (<c>SharedNodeBinder.FindDeactivator</c>).</item>
///   <item>⛔ a retired form, or one its binding cannot feed, is an ERROR (the asset is skipped with <c>BTREE0002</c>) —
///     never a deactivator that silently stops firing.</item>
/// </list>
///
/// <para>Lives in <c>Hrot.AiEditor.Generators</c> (not in <c>Hrot.AiEditor.Persistence</c>) because it depends on Roslyn —
/// the persistence project is netstandard2.0 with no Roslyn reference.</para>
/// </summary>
internal static class BTreeDeactivatorScanner
{
    private const string EntityFqn = "Fdp.Core.Entity";
    private const string WorldFqn  = "Fdp.Core.EntityRepository";

    /// <summary>
    /// The deactivators paired with the methods <paramref name="dto"/> binds, one per action method. <paramref name="error"/>
    /// is non-null (and the result empty) when a paired deactivator uses a retired form or cannot be fed by a binding.
    /// </summary>
    internal static List<BTreeBridgeEmitCore.DeactivatorEntry> Scan(
        Compilation compilation, BehaviorTreeAssetDto dto, out string? error)
    {
        error = null;
        var result = new List<BTreeBridgeEmitCore.DeactivatorEntry>();

        INamedTypeSymbol? attrSymbol = compilation.GetTypeByMetadataName("Fbt.BTreeDeactivatorAttribute");
        if (attrSymbol == null) return result;

        var bindings = Bindings(dto);
        var done     = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (node, binding, shape) in bindings)
        {
            string fqn = binding.MethodFqn!;
            if (!done.Add(fqn)) continue;

            var action = BTreeMethodCompatibilityValidator.ResolveMethod(compilation, fqn);
            if (action == null) continue; // the compatibility validator already reports an unresolvable method

            IMethodSymbol? deactivator = FindBeside(action, fqn, attrSymbol);
            if (deactivator == null) continue;

            var entry = Classify(deactivator, out string? formError);
            if (entry == null)
            {
                error = formError;
                return new List<BTreeBridgeEmitCore.DeactivatorEntry>();
            }
            entry.TargetMethodFqn = fqn;

            // Every binding of this method must be able to feed the deactivator.
            foreach (var (otherNode, otherBinding, otherShape) in bindings)
            {
                if (!string.Equals(otherBinding.MethodFqn, fqn, StringComparison.Ordinal)) continue;
                string? feedError = CheckFeed(entry, otherBinding, otherShape, dto, compilation);
                if (feedError != null)
                {
                    error = $"leaf {otherNode.VisualId:D}: {feedError}";
                    return new List<BTreeBridgeEmitCore.DeactivatorEntry>();
                }
            }
            result.Add(entry);
        }
        return result;
    }

    private static List<(BTreeNodeDto Node, BehaviorActionBindingDto Binding, BTreeDelegateShapeDto Shape)> Bindings(
        BehaviorTreeAssetDto dto)
    {
        var list = new List<(BTreeNodeDto, BehaviorActionBindingDto, BTreeDelegateShapeDto)>();
        foreach (var n in dto.Nodes)
        {
            var (b, shape) = n switch
            {
                BTreeActionNodeDto a    => (a.Action, a.DelegateShape),
                BTreeConditionNodeDto c => (c.Condition, c.DelegateShape),
                _                       => (null, default(BTreeDelegateShapeDto)),
            };
            if (b != null && !string.IsNullOrEmpty(b.MethodFqn)) list.Add((n, b, shape));
        }
        return list;
    }

    /// <summary>⭐ <c>CE-3082</c> D2 — the FQN of the <c>[BTreeDeactivator]</c> beside the shared action <paramref name="actionFqn"/>,
    /// or null. The HSM generator binds it as a state's OnExit (<c>HsmDeactivatorExits</c>).</summary>
    internal static string? DeactivatorFqnOf(Compilation compilation, string actionFqn)
    {
        INamedTypeSymbol? attrSymbol = compilation.GetTypeByMetadataName("Fbt.BTreeDeactivatorAttribute");
        if (attrSymbol == null) return null;
        var action = BTreeMethodCompatibilityValidator.ResolveMethod(compilation, actionFqn);
        if (action == null) return null;
        var d = FindBeside(action, actionFqn, attrSymbol);
        return d == null ? null : d.ContainingType.ToDisplayString() + "." + d.Name;
    }

    private static IMethodSymbol? FindBeside(IMethodSymbol action, string actionFqn, INamedTypeSymbol attrSymbol)
    {
        foreach (var member in action.ContainingType.GetMembers())
        {
            if (member is not IMethodSymbol m || !m.IsStatic) continue;
            foreach (var attr in m.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrSymbol)) continue;
                if (attr.ConstructorArguments.Length == 0 || attr.ConstructorArguments[0].Value is not string target) continue;
                if (string.Equals(TargetMethod(target), actionFqn.Replace('+', '.'), StringComparison.Ordinal)) return m;
            }
        }
        return null;
    }

    /// <summary>The method a target names — the same rule as <c>SharedNodeBinder.DeactivatorTargetMethod</c>.</summary>
    internal static string TargetMethod(string target)
    {
        int at = target.IndexOf('@');
        return (at < 0 ? target : target.Substring(0, at)).Replace('+', '.');
    }

    private static BTreeBridgeEmitCore.DeactivatorEntry? Classify(IMethodSymbol d, out string? error)
    {
        error = null;
        string fqn = d.ContainingType.ToDisplayString() + "." + d.Name;
        var ps = d.Parameters;
        int n = ps.Length;
        bool tail = n >= 2 && d.ReturnsVoid
                    && ps[n - 2].RefKind == RefKind.None && ps[n - 2].Type.ToDisplayString() == EntityFqn
                    && ps[n - 1].RefKind == RefKind.None && ps[n - 1].Type.ToDisplayString() == WorldFqn;
        if (tail && n == 2)
            return new BTreeBridgeEmitCore.DeactivatorEntry
                { DeactivatorFqn = fqn, Form = BTreeBridgeEmitCore.DeactivatorForm.NoParams };
        if (tail && n == 3 && ps[0].RefKind == RefKind.Ref)
            return new BTreeBridgeEmitCore.DeactivatorEntry
            {
                DeactivatorFqn = fqn, Form = BTreeBridgeEmitCore.DeactivatorForm.Plain, ParamsTypeFqn = TypeFqn(ps[0].Type),
            };
        if (tail && n == 4 && ps[0].RefKind == RefKind.Ref && ps[1].RefKind == RefKind.Ref)
            return new BTreeBridgeEmitCore.DeactivatorEntry
            {
                DeactivatorFqn = fqn, Form = BTreeBridgeEmitCore.DeactivatorForm.Stateful,
                ParamsTypeFqn = TypeFqn(ps[0].Type), WorkingStateTypeFqn = TypeFqn(ps[1].Type),
            };

        error = $"deactivator '{fqn}' uses a retired form; a deactivator takes (Entity, EntityRepository), " +
                "(ref TParams, Entity, EntityRepository) or (ref TParams, ref TWorkingState, Entity, EntityRepository) " +
                "and returns void (CE-504)";
        return null;
    }

    private static string? CheckFeed(
        BTreeBridgeEmitCore.DeactivatorEntry d, BehaviorActionBindingDto b, BTreeDelegateShapeDto shape,
        BehaviorTreeAssetDto dto, Compilation compilation)
    {
        if (d.Form == BTreeBridgeEmitCore.DeactivatorForm.NoParams) return null;

        bool hasParams = shape == BTreeDelegateShapeDto.Plain || shape == BTreeDelegateShapeDto.Stateful;
        if (!hasParams)
            return $"deactivator '{d.DeactivatorFqn}' takes params, but '{b.MethodFqn}' is bound as {shape}; use (Entity, EntityRepository) (CE-504)";

        string varType = string.Empty;
        foreach (var v in dto.Blackboard.Variables)
            if (string.Equals(v.Name, b.ExpressionTargetField, StringComparison.Ordinal)) { varType = v.Type?.TypeId ?? string.Empty; break; }
        if (!SameType(d.ParamsTypeFqn, varType))
            return $"deactivator '{d.DeactivatorFqn}' takes 'ref {d.ParamsTypeFqn}' but '{b.MethodFqn}' is bound to '{b.ExpressionTargetField}' of type '{varType}' (CE-504)";

        if (d.Form == BTreeBridgeEmitCore.DeactivatorForm.Stateful)
        {
            if (shape != BTreeDelegateShapeDto.Stateful)
                return $"deactivator '{d.DeactivatorFqn}' takes a working state, but '{b.MethodFqn}' is not stateful (CE-504)";
            var info = SharedAiMethodResolver.Make(compilation)(b.MethodFqn!);
            if (info?.WorkingStateTypeFqn != null && !SameType(d.WorkingStateTypeFqn, info.WorkingStateTypeFqn))
                return $"deactivator '{d.DeactivatorFqn}' takes 'ref {d.WorkingStateTypeFqn}' but '{b.MethodFqn}' works on '{info.WorkingStateTypeFqn}' (CE-504)";
        }
        return null;
    }

    private static bool SameType(string? a, string? b)
        => string.Equals(Norm(a), Norm(b), StringComparison.Ordinal);

    private static string Norm(string? t)
        => (t ?? string.Empty).Replace("global::", string.Empty).Replace('+', '.');

    private static string TypeFqn(ITypeSymbol t)
        => t.ToDisplayString(new SymbolDisplayFormat(
               globalNamespaceStyle:   SymbolDisplayGlobalNamespaceStyle.Omitted,
               typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces))
           .Replace('+', '.');
}
