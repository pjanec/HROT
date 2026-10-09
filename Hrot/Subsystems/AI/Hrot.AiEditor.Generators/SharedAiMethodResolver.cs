using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.AiEditor.Persistence.Emit;
using Microsoft.CodeAnalysis;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// ⭐⭐ <c>CE-417</c> B-2 — "is this FQN a <c>[SharedAiAction]</c> / <c>[SharedAiCondition]</c> method, and what does it take?",
/// answered from the Roslyn compilation — the only thing that knows (the attribute lives on the method symbol).
/// ⭐ One resolver for both asset generators, cached per compilation (an asset binds one method at many sites).
/// </summary>
internal static class SharedAiMethodResolver
{
    private const string SharedAiActionAttr    = "Fbt.Kernel.SharedAiActionAttribute";
    private const string SharedAiConditionAttr = "Fbt.Kernel.SharedAiConditionAttribute";
    private const string WritesChannelAttr     = "Fbt.Kernel.WritesChannelAttribute";
    // ⭐ CE-3082 D2 — a [BTreeDeactivator] is callable as an ACTION (an HSM state's OnExit; its void return is a status
    //   nothing reads, as every HSM action's is).
    private const string DeactivatorAttr       = "Fbt.BTreeDeactivatorAttribute";

    public static Func<string, SharedAiMethodInfo?> Make(Compilation compilation)
    {
        var cache = new Dictionary<string, SharedAiMethodInfo?>(StringComparer.Ordinal);
        return fqn =>
        {
            if (cache.TryGetValue(fqn, out var hit)) return hit;
            SharedAiMethodInfo? result = null;
            int dot = fqn.LastIndexOf('.');
            if (dot > 0)
            {
                var type = compilation.GetTypeByMetadataName(fqn.Substring(0, dot));
                if (type != null)
                {
                    foreach (var m in type.GetMembers(fqn.Substring(dot + 1)).OfType<IMethodSymbol>())
                    {
                        bool isAction = false, isCondition = false;
                        var writes = new List<int>();
                        foreach (var a in m.GetAttributes())
                        {
                            string? n = a.AttributeClass?.ToDisplayString();
                            if (n == SharedAiActionAttr || n == DeactivatorAttr) isAction = true;
                            else if (n == SharedAiConditionAttr) isCondition = true;
                            else if (n == WritesChannelAttr && a.ConstructorArguments.Length > 0
                                     && a.ConstructorArguments[0].Value is int kind) writes.Add(kind);
                        }
                        if (!isAction && !isCondition) continue;
                        // ⭐ CE-504 C-2 — the three shared forms, told apart by the leading ref parameters:
                        //   (ref P, Entity, Repo) · (ref P, ref WS, Entity, Repo) · (Entity, Repo).
                        // ⭐ CE-3137 U-2 — a TRAILING group of `ref T` unit-memory parameters binds to the unit's memory: strip it
                        //   (recording the types, in order) and classify the rest.
                        var all = m.Parameters;
                        int end = all.Length;
                        while (end > 0 && all[end - 1].RefKind == RefKind.Ref && IsUnitMemory(all[end - 1].Type)) end--;
                        var unitMemory = new List<string>();
                        for (int u = end; u < all.Length; u++)
                            unitMemory.Add(all[u].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                        var ps = all.Take(end).ToArray();
                        int refs = 0;
                        while (refs < ps.Length && ps[refs].RefKind == RefKind.Ref) refs++;
                        if (refs > 2 || ps.Length != refs + 2) continue;
                        var p = refs > 0 ? ps[0].Type : null;
                        result = new SharedAiMethodInfo(
                            p?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "",
                            p is null ? "" : TypeIdOf(p),
                            isCondition && !isAction,
                            m.ReturnType.SpecialType == SpecialType.System_Boolean,
                            writes,
                            refs == 2 ? ps[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null,
                            unitMemory);
                        break;
                    }
                }
            }
            cache[fqn] = result;
            return result;
        };
    }

    /// <summary>⭐ <c>CE-3137</c> U-2 — the type carries <c>[Fbt.Kernel.UnitMemory]</c>.</summary>
    public static bool IsUnitMemory(ITypeSymbol t)
        => t.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == UnitMemoryParams.AttributeName);

    /// <summary>A type as a blackboard <c>TypeId</c>: namespace-qualified, <c>+</c> between nested types.</summary>
    public static string TypeIdOf(ITypeSymbol t)
    {
        var parts = new List<string>();
        for (var c = t; c != null; c = c.ContainingType) parts.Insert(0, c.Name);
        string ns = t.ContainingNamespace == null || t.ContainingNamespace.IsGlobalNamespace
            ? "" : t.ContainingNamespace.ToDisplayString() + ".";
        return ns + string.Join("+", parts);
    }

    /// <summary>The message both generators report when a bound variable is not the method's <c>ref</c> type.</summary>
    public static string DescribeMismatch(SharedAiBindings.Entry e)
        => $"{e.Site} binds '{e.MethodFqn}' to variable '{e.VariableName}' of type '{e.VariableTypeId}', but the method takes " +
           $"'ref {e.Method.ParamTypeId}'. Bind a variable of that type (CE-417: a C# action reads its bound variable in place).";
}
