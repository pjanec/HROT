using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐ <c>CE-3137</c> U-2 (<c>Q87</c> G, <c>R-237</c>) — a C# <c>[SharedAi*]</c> action or condition may END its parameter list with
/// <c>ref T</c> parameters of <c>[Fbt.Kernel.UnitMemory]</c> types:
/// <c>NodeStatus Act(ref P p, Entity self, EntityRepository world, ref FiringPositionMemory mem)</c>. They bind to the unit's
/// memory (<c>Fdp.Toolkit.Behavior.UnitMemory.Ref&lt;T&gt;(world, self)</c>) with no variable, and are stripped before the
/// call shape is classified (<see cref="BTreeCallShapes.WithoutUnitMemory"/>), so every existing shape keeps its meaning.
/// ⚠ TRAILING only: the group comes after <c>(Entity, EntityRepository)</c>; a unit-memory <c>ref</c> earlier in the list is read
/// as the params or working-state variable, as before.
/// </summary>
public static class UnitMemoryParams
{
    /// <summary>The attribute's metadata name — matched by name so this assembly needs no reference to <c>Fbt.Kernel</c>.</summary>
    public const string AttributeName = "Fbt.Kernel.UnitMemoryAttribute";

    /// <summary>The accessor the generated call goes through.</summary>
    public const string Accessor = "global::Fdp.Toolkit.Behavior.UnitMemory.Ref";

    /// <summary>True when <paramref name="t"/> is a struct marked <c>[UnitMemory]</c>.</summary>
    public static bool IsUnitMemoryType(Type? t)
    {
        if (t is null || !t.IsValueType) return false;
        try { return t.GetCustomAttributesData().Any(a => a.AttributeType.FullName == AttributeName); }
        catch (Exception) { return false; }
    }

    /// <summary>The method's parameters without the trailing unit-memory group (reflection).</summary>
    public static ParameterInfo[] Core(MethodInfo method)
    {
        var ps = method.GetParameters();
        int n = ps.Length;
        while (n > 0 && ps[n - 1].ParameterType.IsByRef && IsUnitMemoryType(ps[n - 1].ParameterType.GetElementType())) n--;
        return n == ps.Length ? ps : ps.Take(n).ToArray();
    }

    /// <summary>The C# argument suffix for <paramref name="typeFqns"/> (each <c>global::</c>-qualified): one
    /// <c>, ref UnitMemory.Ref&lt;T&gt;(world, self)</c> per type, in order; empty when there are none.</summary>
    public static string Args(IReadOnlyList<string>? typeFqns, string selfExpr, string worldExpr)
    {
        if (typeFqns is null || typeFqns.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var t in typeFqns) sb.Append($", ref {Accessor}<{t}>({worldExpr}, {selfExpr})");
        return sb.ToString();
    }
}
