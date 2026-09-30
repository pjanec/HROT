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
    /// <summary>
    /// ⭐⭐⭐ <b>What a hosting site was TOLD, kept alongside what it RESOLVED TO.</b>
    /// ⛔ Keeping the <c>Registry</c> + <c>ChildName</c> — rather than only the interpreter — is what
    /// makes binding ORDER-INDEPENDENT; see <see cref="Register"/>'s remarks.
    /// </summary>
    private sealed class Binding
    {
        public required BehaviorRegistry Registry;
        public required string ChildName;
        public Interpreter<byte, BTreeContext>? Resolved;
        public BehaviorDefinition? ResolvedDefinition;   // CE-431: the child's block comes from its definition
    }

    // tree-state slot key -> what the host asked for, and the interpreter once it exists.
    private static readonly Dictionary<int, Binding> _bySlotKey = new();
    /// <summary>
    /// ⭐ <c>CE-442</c> — every WRITE takes this lock, so two registrars running at once (parallel test
    /// classes today) cannot corrupt the table. ⛔ Reads stay unlocked: registration finishes before the
    /// first tick, and the read side is on the per-tick path. Same rule as <c>HsmActionDispatcher</c>.
    /// </summary>
    private static readonly object WriteLock = new();


    /// <summary>
    /// ⭐⭐ Binds <paramref name="childName"/>, resolved through <paramref name="registry"/>, to
    /// <paramref name="treeStateSlotKey"/>.
    ///
    /// <para>⭐⭐⭐ <b>RESOLUTION IS LAZY, AND THAT IS THE WHOLE POINT — <c>CE-377</c>.</b>
    /// 🔴 <b>The measured hazard:</b> registrars run in an ARBITRARY order, so a host's registrar
    /// routinely runs before its child's. An eager resolve therefore SKIPPED the bind, and the node
    /// then threw at tick time — a failure that depends on reflection order and so appears and
    /// disappears between builds. 📐 This stopped being theoretical when <c>E6</c> wired the
    /// editor-authored route: <b>three shipped assets</b> (<c>BTreeRenderShowcase</c>,
    /// <c>CombatShowcase</c>, <c>T07_Subtree</c>) host <c>SampleScout</c>.</para>
    ///
    /// <para>⇒ ⭐ the entry records the REGISTRY and the NAME; the interpreter is looked up on first
    /// use and cached. By tick time every registrar has run, so the order cannot matter. ⛔ This is
    /// strictly better than a post-scan "resolve pending" pass, which would need a lifecycle hook
    /// that does not exist and would leave the same hazard for anything registering after it.</para>
    ///
    /// <para>⭐ <b>Last writer wins by design.</b> Hot reload re-runs registrars, and the NEW
    /// binding is the one that must be reachable; a first-writer-wins table would pin a child from a
    /// collected assembly. ⚠ Re-registering DROPS the cached interpreter, so a reloaded child is
    /// picked up rather than the stale one.</para>
    /// </summary>
    public static void Register(BehaviorRegistry registry, int treeStateSlotKey, string childName)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (string.IsNullOrWhiteSpace(childName)) return;

        lock (WriteLock) _bySlotKey[treeStateSlotKey] = new Binding { Registry = registry, ChildName = childName };
    }

    /// <summary>⭐ The child bound to this slot, or <c>false</c>.</summary>
    public static bool TryGet(int treeStateSlotKey, out Interpreter<byte, BTreeContext> interpreter)
    {
        interpreter = null!;
        if (!_bySlotKey.TryGetValue(treeStateSlotKey, out var binding)) return false;

        var resolved = Resolve(binding);
        if (resolved is null) return false;

        interpreter = resolved;
        return true;
    }

    /// <summary>
    /// ⭐⭐ The child bound to this slot. ⛔ <b>THROWS rather than returning null</b> — the same
    /// choice <c>HostedSubtree.Tick</c> makes for a missing slot (§19.6 ⑤): a host that silently
    /// does nothing reads as <i>"the subtree just fails"</i>, which is the exact silent miss this
    /// programme keeps paying for.
    ///
    /// <para>⚠ <b>Two distinct failures, two messages</b>, because they have different fixes: no
    /// binding at all means the HOST's registrar never called <see cref="Register"/>; a binding that
    /// will not resolve means the CHILD is absent from the registry or is not a BTree behaviour.</para>
    /// </summary>
    public static Interpreter<byte, BTreeContext> Require(int treeStateSlotKey)
    {
        if (!_bySlotKey.TryGetValue(treeStateSlotKey, out var binding))
            throw new InvalidOperationException(
                $"No hosted child is bound to tree-state slot {treeStateSlotKey}. The host's generated " +
                "Register() binds it with HostedChildren.Register(beh, key, childName).");

        return Resolve(binding)
            ?? throw new InvalidOperationException(
                $"The hosted child '{binding.ChildName}' bound to tree-state slot {treeStateSlotKey} does " +
                "not resolve: it is not registered in that BehaviorRegistry, or it is not a BTree " +
                "behaviour (an HSM child cannot be hosted this way).");
    }

    /// <summary>
    /// ⚠ Resolve-and-cache. ⛔ A failed resolve is NOT cached — the child may simply not have been
    /// registered yet, which is the entire reason this is lazy.
    /// </summary>
    private static Interpreter<byte, BTreeContext>? Resolve(Binding binding)
    {
        if (binding.Resolved is { } cached) return cached;

        if (!binding.Registry.TryGetId(binding.ChildName, out int id)) return null;
        if (!binding.Registry.TryGetDefinition(id, out var def)) return null;
        if (def.BTreeInterpreter is not { } interpreter) return null;   // an HSM child cannot be hosted this way

        binding.Resolved = interpreter;
        binding.ResolvedDefinition = def;
        return interpreter;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-431</c> — the CHILD's definition, which is what sizes and seeds its block.
    /// ⚠ Same lazy resolve as <see cref="TryGet"/>, same reason (<c>CE-377</c>).
    /// </summary>
    public static bool TryGetDefinition(int treeStateSlotKey, out BehaviorDefinition definition)
    {
        definition = null!;
        if (!_bySlotKey.TryGetValue(treeStateSlotKey, out var binding)) return false;
        if (Resolve(binding) is null) return false;
        definition = binding.ResolvedDefinition!;
        return true;
    }

    /// <summary>⭐ The registry a slot's child resolves through — for the curated-resolver check.</summary>
    internal static bool TryGetRegistry(int treeStateSlotKey, out BehaviorRegistry registry, out string childName)
    {
        registry = null!; childName = null!;
        if (!_bySlotKey.TryGetValue(treeStateSlotKey, out var binding)) return false;
        registry = binding.Registry; childName = binding.ChildName;
        return true;
    }

    /// <summary>⚠ Test seam — drops every binding. ⛔ Production never calls this.</summary>
    public static void ClearForTests() { lock (WriteLock) _bySlotKey.Clear(); }
}
