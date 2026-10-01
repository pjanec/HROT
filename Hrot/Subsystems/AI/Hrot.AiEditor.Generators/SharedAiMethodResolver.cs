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
                            if (n == SharedAiActionAttr) isAction = true;
                            else if (n == SharedAiConditionAttr) isCondition = true;
                            else if (n == WritesChannelAttr && a.ConstructorArguments.Length > 0
                                     && a.ConstructorArguments[0].Value is int kind) writes.Add(kind);
                        }
                        if (!isAction && !isCondition) continue;
                        if (m.Parameters.Length == 0 || m.Parameters[0].RefKind != RefKind.Ref) continue;
                        var p = m.Parameters[0].Type;
                        result = new SharedAiMethodInfo(
                            p.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            TypeIdOf(p),
                            isCondition && !isAction,
                            m.ReturnType.SpecialType == SpecialType.System_Boolean,
                            writes);
                        break;
                    }
                }
            }
            cache[fqn] = result;
            return result;
        };
    }

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
