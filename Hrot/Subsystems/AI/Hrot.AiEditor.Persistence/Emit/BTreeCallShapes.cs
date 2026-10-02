using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>One parameter of a node method, as both Roslyn and reflection can describe it.</summary>
public readonly struct CallParam
{
    public CallParam(string typeFqn, bool isRef) { TypeFqn = typeFqn; IsRef = isRef; }
    /// <summary>The parameter type's full name WITHOUT <c>global::</c> (nested types may use <c>.</c> or <c>+</c>).</summary>
    public string TypeFqn { get; }
    /// <summary>True for <c>ref</c>/<c>in</c>/<c>out</c>.</summary>
    public bool IsRef { get; }
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-504</c> C-1 — a BTree node's call shape is DERIVED from the method it binds, never persisted.</b>
/// 📄 <c>docs/blueprints/DESIGN_BTree_Node_Call_Shapes.md</c> §4 C-1, §5 slice 1.
///
/// <para>🔴 <b>Why.</b> The shape was a second, hand-maintained fact about the method: the editor never set the stateful or
/// whole-blackboard value (a C# pick left it <c>ThreeParamReusable</c>), so a stateful method picked in the inspector was
/// silently skipped by the generator (<c>BTREE0002</c>). Each shape is a distinct parameter list, so the method says it.</para>
///
/// <para>⭐ <b>ONE rule, two signature sources:</b> the generator hands it the Roslyn signature
/// (<c>BTreeJsonGenerator</c>); the editor and tests hand it reflection (<see cref="ReflectionSignatures"/>). A blueprint
/// binding needs no signature — its id makes it the blueprint call.</para>
/// </summary>
public static class BTreeCallShapes
{
    private const string BehaviorTreeState = "Fbt.BehaviorTreeState";
    private const string Entity            = "Fdp.Core.Entity";
    private const string EntityRepository  = "Fdp.Core.EntityRepository";

    /// <summary>The shape a method's parameter list implies, or null when it matches none (the validator then skips it).</summary>
    public static BTreeDelegateShapeDto? FromSignature(IReadOnlyList<CallParam> ps)
    {
        switch (ps.Count)
        {
            case 3 when ps[0].IsRef && Is(ps[1], BehaviorTreeState, true) && ps[2].IsRef:
                return BTreeDelegateShapeDto.ThreeParamReusable;                         // (ref P, ref BTS, ref Ctx)
            case 3 when ps[0].IsRef && Is(ps[1], Entity, false) && Is(ps[2], EntityRepository, false):
                return BTreeDelegateShapeDto.ThreeParamReusable;                         // [SharedAi*] (ref P, Entity, Repo) — CE-417 3b
            case 4 when ps[0].IsRef && Is(ps[1], BehaviorTreeState, true) && ps[2].IsRef && !ps[3].IsRef && ps[3].TypeFqn == "System.Int32":
                return BTreeDelegateShapeDto.FourParamFull;                              // the kernel NodeLogicDelegate
            case 4 when ps[0].IsRef && ps[1].IsRef && Is(ps[2], BehaviorTreeState, true) && ps[3].IsRef:
                return BTreeDelegateShapeDto.ThreeParamReusableStateful;                 // (ref P, ref WS, ref BTS, ref Ctx)
            case 5 when ps[0].IsRef && ps[1].IsRef && Is(ps[2], Entity, false) && Is(ps[3], EntityRepository, false)
                        && !ps[4].IsRef && ps[4].TypeFqn == "System.Single":
                return BTreeDelegateShapeDto.AiPrimitiveTickCore;                        // TickCore(ref P, ref WS, Entity, Repo, float)
            default:
                return null;
        }
    }

    /// <summary>The shape of one binding: a blueprint id ⇒ the blueprint call; else the method's signature; else null.</summary>
    public static BTreeDelegateShapeDto? Classify(
        BehaviorActionBindingDto? binding, Func<string, IReadOnlyList<CallParam>?> signatureOf)
        => binding is null ? null : Classify(binding.BlueprintAssetId, binding.MethodFqn, signatureOf);

    /// <summary>The same rule over the two fields it reads — so the editor model (a different binding type) uses it too.</summary>
    public static BTreeDelegateShapeDto? Classify(
        Guid blueprintAssetId, string? methodFqn, Func<string, IReadOnlyList<CallParam>?> signatureOf)
    {
        if (blueprintAssetId != Guid.Empty) return BTreeDelegateShapeDto.AiPrimitiveTickCore;
        if (string.IsNullOrEmpty(methodFqn)) return null;
        return signatureOf(methodFqn!) is { } ps ? FromSignature(ps) : null;
    }

    /// <summary>
    /// Sets every Action/Condition node's in-memory <c>DelegateShape</c> from its binding. An unclassifiable binding keeps
    /// the default (<c>ThreeParamReusable</c>), which the validator then reports as <c>BTREE0002</c> — the same outcome a
    /// mismatched persisted shape had. <returns>How many nodes were classified.</returns>
    /// </summary>
    public static int Apply(BehaviorTreeAssetDto dto, Func<string, IReadOnlyList<CallParam>?> signatureOf)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));
        int n = 0;
        foreach (var node in dto.Nodes)
        {
            switch (node)
            {
                case BTreeActionNodeDto a when Classify(a.Action, signatureOf) is { } sa:
                    a.DelegateShape = sa; n++; break;
                case BTreeConditionNodeDto c when Classify(c.Condition, signatureOf) is { } sc:
                    c.DelegateShape = sc; n++; break;
            }
        }
        return n;
    }

    /// <summary>
    /// A signature source over loaded assemblies, by FQN (<c>Namespace.Type.Method</c>, nested types with <c>+</c> or
    /// <c>.</c>). ⚠ Not for the generator (an analyzer must not reflect); the generator passes Roslyn's view. A later
    /// assembly wins, so a hot-reloaded copy shadows the original.
    /// </summary>
    public static Func<string, IReadOnlyList<CallParam>?> ReflectionSignatures(IEnumerable<Assembly> assemblies)
    {
        var asms = assemblies.ToArray();
        var cache = new Dictionary<string, IReadOnlyList<CallParam>?>(StringComparer.Ordinal);
        return fqn =>
        {
            if (cache.TryGetValue(fqn, out var hit)) return hit;
            IReadOnlyList<CallParam>? found = null;
            int dot = fqn.LastIndexOf('.');
            if (dot > 0)
            {
                string typeName = fqn.Substring(0, dot), method = fqn.Substring(dot + 1);
                for (int i = asms.Length - 1; i >= 0 && found is null; i--)
                {
                    var type = FindType(asms[i], typeName);
                    var mi = type?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                  .FirstOrDefault(m => m.Name == method);
                    if (mi != null) found = Describe(mi);
                }
            }
            cache[fqn] = found;
            return found;
        };
    }

    /// <summary>Every assembly loaded in this process — the editor's and the tests' source.</summary>
    public static Func<string, IReadOnlyList<CallParam>?> LoadedAssemblySignatures()
        => ReflectionSignatures(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic));

    private static Type? FindType(Assembly asm, string name)
    {
        try
        {
            var t = asm.GetType(name, throwOnError: false);
            if (t != null) return t;
            // a nested type written with '.' instead of '+': try each split from the right
            for (int i = name.LastIndexOf('.'); i > 0; i = name.LastIndexOf('.', i - 1))
            {
                string candidate = name.Substring(0, i) + "+" + name.Substring(i + 1).Replace('.', '+');
                t = asm.GetType(candidate, throwOnError: false);
                if (t != null) return t;
            }
        }
        catch (Exception) { /* a partially loadable assembly answers nothing */ }
        return null;
    }

    private static IReadOnlyList<CallParam> Describe(MethodInfo mi)
        => mi.GetParameters()
             .Select(p => p.ParameterType.IsByRef
                 ? new CallParam(p.ParameterType.GetElementType()!.FullName ?? "", true)
                 : new CallParam(p.ParameterType.FullName ?? "", false))
             .ToArray();

    private static bool Is(CallParam p, string fqn, bool isRef) => p.IsRef == isRef && p.TypeFqn == fqn;
}
