using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// HAJSON-B: Roslyn-based scanner for <c>[BTreeDeactivatorAttribute]</c>-annotated methods.
///
/// Scans all named types in the compilation for methods that carry
/// <c>[Fbt.BTreeDeactivatorAttribute]</c> whose <c>TargetAction</c> key matches a key
/// already registered by the bridge (as reported by
/// <see cref="BTreeBridgeEmitCore.CollectRegisteredActionKeys"/>).
///
/// Lives in <c>Hrot.AiEditor.Generators</c> (not in <c>Hrot.AiEditor.Persistence</c>)
/// because it depends on Roslyn — the persistence project is netstandard2.0 with no
/// Roslyn reference; the generators project already depends on Microsoft.CodeAnalysis.CSharp.
/// </summary>
internal static class BTreeDeactivatorScanner
{
    /// <summary>
    /// Scans <paramref name="compilation"/> for all methods annotated with
    /// <c>[Fbt.BTreeDeactivatorAttribute]</c> whose <c>TargetAction</c> matches one of the
    /// keys in <paramref name="registeredActionKeys"/>.
    ///
    /// Returns a list of <see cref="BTreeBridgeEmitCore.DeactivatorEntry"/> records, one
    /// per unique matching deactivator, deduped by <c>ActionKey</c> (first-match-wins).
    ///
    /// For 4-param deactivators <c>(ref TBB, ref BehaviorTreeState, ref TCtx, int)</c>: the
    /// emitter registers the method directly as a <c>NodeDeactivatorDelegate</c>.
    ///
    /// For 3-param deactivators <c>(ref TDto, ref BehaviorTreeState, ref TCtx)</c>: the
    /// emitter generates a wrapper lambda that projects <c>TDto</c> at the byte offset
    /// encoded in the key suffix after the last <c>@</c>, mirroring the managed action thunk.
    ///
    /// Methods with any other param count are silently skipped (forward-compat guard).
    /// </summary>
    internal static List<BTreeBridgeEmitCore.DeactivatorEntry> Scan(
        Compilation compilation,
        HashSet<string> registeredActionKeys)
    {
        var result = new List<BTreeBridgeEmitCore.DeactivatorEntry>();
        if (registeredActionKeys.Count == 0) return result;

        var seen = new HashSet<string>(StringComparer.Ordinal); // by ActionKey, first-wins

        // Resolve Fbt.BTreeDeactivatorAttribute once — if not present, nothing to do.
        INamedTypeSymbol? deactivatorAttrSymbol =
            compilation.GetTypeByMetadataName("Fbt.BTreeDeactivatorAttribute");
        if (deactivatorAttrSymbol == null) return result;

        // Walk all named types in the compilation (source + referenced assemblies).
        foreach (var typeSymbol in EnumerateAllNamedTypes(compilation))
        {
            foreach (var member in typeSymbol.GetMembers())
            {
                if (member is not IMethodSymbol method) continue;
                if (!method.IsStatic) continue;
                if (!method.ReturnsVoid) continue; // deactivators must return void

                // Find [BTreeDeactivatorAttribute] on this method.
                AttributeData? deactivatorAttr = null;
                foreach (var attr in method.GetAttributes())
                {
                    if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, deactivatorAttrSymbol))
                    {
                        deactivatorAttr = attr;
                        break;
                    }
                }
                if (deactivatorAttr == null) continue;

                // Extract TargetAction from the constructor argument.
                if (deactivatorAttr.ConstructorArguments.Length == 0) continue;
                string? targetAction = deactivatorAttr.ConstructorArguments[0].Value as string;
                if (string.IsNullOrEmpty(targetAction)) continue;

                // Does this TargetAction match a key registered by this asset?
                if (!registeredActionKeys.Contains(targetAction!)) continue;

                // Dedup: first match wins.
                if (!seen.Add(targetAction!)) continue;

                string methodFqn = method.ContainingType.ToDisplayString() + "." + method.Name;
                int paramCount = method.Parameters.Length;

                if (paramCount == 4)
                {
                    // 4-param FourParamFull: (ref TBB, ref BehaviorTreeState, ref TCtx, int)
                    // Matches NodeDeactivatorDelegate<TBB,TCtx> directly — register directly.
                    result.Add(new BTreeBridgeEmitCore.DeactivatorEntry
                    {
                        ActionKey      = targetAction!,
                        DeactivatorFqn = methodFqn,
                        ParamCount     = 4,
                        DtoTypeFqn     = null,
                        DtoByteOffset  = 0,
                    });
                }
                else if (paramCount == 3)
                {
                    // 3-param ThreeParamReusable: (ref TDto, ref BehaviorTreeState, ref TCtx)
                    // The bridge emits a wrapper lambda projecting TDto at the offset encoded in
                    // the key suffix (e.g. "...Action_MaintainEqsSensor@0" → offset 0).
                    int atPos = targetAction!.LastIndexOf('@');
                    if (atPos < 0) continue; // no offset suffix — unexpected shape, skip
                    string offsetStr = targetAction.Substring(atPos + 1);
                    if (!int.TryParse(offsetStr,
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out int dtoOffset)) continue;

                    // Param-0 type provides the TDto for the Unsafe.As cast in the wrapper.
                    var param0 = method.Parameters[0];
                    string dtoTypeFqn = "global::" + param0.Type.ToDisplayString(
                        new SymbolDisplayFormat(
                            globalNamespaceStyle:  SymbolDisplayGlobalNamespaceStyle.Omitted,
                            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces))
                        .Replace('+', '.');

                    result.Add(new BTreeBridgeEmitCore.DeactivatorEntry
                    {
                        ActionKey      = targetAction!,
                        DeactivatorFqn = methodFqn,
                        ParamCount     = 3,
                        DtoTypeFqn     = dtoTypeFqn,
                        DtoByteOffset  = dtoOffset,
                    });
                }
                // else: unexpected param count — forward-compat guard, skip silently.
            }
        }

        return result;
    }

    /// <summary>
    /// Enumerates all named types in the compilation (source types + referenced assembly types)
    /// using a DFS traversal of the global namespace tree.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(
        Compilation compilation)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(compilation.GlobalNamespace);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is INamedTypeSymbol named)
            {
                yield return named;
                foreach (var nested in named.GetTypeMembers())
                    stack.Push(nested);
            }
            else if (current is INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers())
                    stack.Push(member);
            }
        }
    }
}
