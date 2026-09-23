using System;
using System.Collections.Generic;
using Fbt.Runtime;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b>ONE PLACE THAT ANSWERS <i>"which interpreter runs the child in slot <c>K</c>?"</i></b> —
/// for every host, whatever brain it is. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.
///
/// <para>🔴🔴 <b>Why this exists at all, and it is a measured constraint rather than a preference.</b>
/// A generated orchestrator thunk is a <b>static method</b>: it receives a blackboard, a
/// <c>BTreeContext</c> and nothing else. 📐 Measured <c>2026-09-23</c>: there is <b>no ambient
/// <see cref="BehaviorRegistry"/></b> — no static instance, and nothing publishes one as a world
/// singleton (<c>EntityRepository.SetSingletonManaged&lt;BehaviorRegistry&gt;</c> has zero callers).
/// ⇒ a thunk CANNOT resolve its child by name at tick time. ⛔ That is precisely why both shipped
/// orchestrator arms called <c>{Child}.GetInterpreter()</c> — a method <b>defined nowhere</b>
/// (<c>CE-333</c> / <c>CE-335</c>): the emission needed an accessor that was never built, and because
/// nothing ever compiled the emitted text, nobody found out.</para>
///
/// <para>⭐⭐ <b>The fix is to resolve at REGISTRATION and bake the KEY.</b> A generated
/// <c>Register(beh, staging)</c> method holds the registry; the slot key is already baked into the
/// thunk because <c>HostedSubtree.Tick</c> needs it. ⇒ the key is the natural handle, and the thunk
/// stays trivial — which is the property <c>HsmParamBindings</c> also protects.</para>
///
/// <para>⭐ <b>One entry serves both hosts.</b> <c>BrainTickSystem</c>'s HSM state hosting
/// (<see cref="HsmHostedSubtrees"/>) and the BTree orchestrator's alias hosting resolve the SAME way
/// through this table, so there is one answer per slot rather than two resolution policies —
/// ruling 9. ⛔ The tables above it differ because the QUESTIONS differ (<i>"which states host?"</i>
/// vs <i>"which node hosts?"</i>); the ANSWER is shared.</para>
///
/// <para>⚠ <b>Startup-only</b>, like the registries beside it. Registration happens during the
/// <c>[BlueprintRegistrar]</c> scan; reads are lock-free thereafter.</para>
/// </summary>
public static class HostedChildren
{
    // tree-state slot key -> the child's interpreter, resolved once at registration.
    private static readonly Dictionary<int, Interpreter<byte, BTreeContext>> _bySlotKey = new();

    /// <summary>
    /// ⭐⭐ Resolves <paramref name="childName"/> through <paramref name="registry"/> and binds it to
    /// <paramref name="treeStateSlotKey"/>.
    ///
    /// <para>⛔ <b>An unresolvable child is SKIPPED, not thrown on</b>, and that is deliberate:
    /// registrars run in an arbitrary order, so the child's own registrar may not have run yet when
    /// the host's does. ⚠ The failure then surfaces at the hosting site as
    /// <see cref="Require"/>'s exception, which names both the key and the child — a far better
    /// message than <i>"registration order"</i> would have been.</para>
    ///
    /// <para>⭐ <b>Last writer wins by design.</b> Hot reload re-runs registrars, and the NEW
    /// interpreter is the one that must be reachable; a first-writer-wins table would pin a child
    /// from a collected assembly.</para>
    /// </summary>
    public static void Register(BehaviorRegistry registry, int treeStateSlotKey, string childName)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (string.IsNullOrWhiteSpace(childName)) return;

        if (!registry.TryGetId(childName, out int id)) return;
        if (!registry.TryGetDefinition(id, out var def)) return;
        if (def.BTreeInterpreter is not { } interpreter) return;   // an HSM child cannot be hosted this way

        _bySlotKey[treeStateSlotKey] = interpreter;
    }

    /// <summary>⭐ The child bound to this slot, or <c>false</c>.</summary>
    public static bool TryGet(int treeStateSlotKey, out Interpreter<byte, BTreeContext> interpreter)
        => _bySlotKey.TryGetValue(treeStateSlotKey, out interpreter!);

    /// <summary>
    /// ⭐⭐ The child bound to this slot. ⛔ <b>THROWS rather than returning null</b> — the same
    /// choice <c>HostedSubtree.Tick</c> makes for a missing slot (§19.6 ⑤): a host that silently
    /// does nothing reads as <i>"the subtree just fails"</i>, which is the exact silent miss this
    /// programme keeps paying for.
    /// </summary>
    public static Interpreter<byte, BTreeContext> Require(int treeStateSlotKey)
        => _bySlotKey.TryGetValue(treeStateSlotKey, out var interpreter)
            ? interpreter
            : throw new InvalidOperationException(
                $"No hosted child is bound to tree-state slot {treeStateSlotKey}. Either the child's " +
                "registrar did not run, or the child is not a BTree behaviour. The host's generated " +
                "Register() binds it with HostedChildren.Register(beh, key, childName).");

    /// <summary>⚠ Test seam — drops every binding. ⛔ Production never calls this.</summary>
    public static void ClearForTests() => _bySlotKey.Clear();
}
