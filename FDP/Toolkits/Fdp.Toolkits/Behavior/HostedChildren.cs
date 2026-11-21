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
        public BehaviorDefinition? ResolvedDefinition;   // CE-431: the child's block comes from its definition; S5a: any tier
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

        // ⭐⭐ S5c (U-9) — this EDGE may be the one that closes a cycle. Its host is whichever registered definition
        //   declares the slot; the cycle exists iff the child reaches that host again.
        foreach (string hostName in registry.GetRegisteredNames())
        {
            if (!registry.TryGetId(hostName, out int id) || !registry.TryGetDefinition(id, out var hostDef)) continue;
            if (!Declares(hostDef, treeStateSlotKey)) continue;
            try { ThrowOnCycleThrough(registry, hostName); }
            catch { lock (WriteLock) _bySlotKey.Remove(treeStateSlotKey); throw; }
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S5d — a binding made against a STAGING registry follows the merge into the LIVE one.</b> Quick reload runs
    /// every registrar against a throw-away staging <see cref="BehaviorRegistry"/> and then
    /// <see cref="BehaviorRegistry.MergeFrom"/>s it into the live registry. ⛔ A binding that kept the staging instance could
    /// only ever resolve a child registered in that SAME reload pass — 📌 measured: a reloaded host whose child was already
    /// live threw <i>"does not resolve"</i> on its first tick. ⭐ Called by <c>MergeFrom</c>; drops each re-pointed binding's
    /// cached definition so the live child is looked up afresh. ⚠ Startup/reload-only.
    /// </summary>
    internal static void Repoint(BehaviorRegistry from, BehaviorRegistry to)
    {
        if (ReferenceEquals(from, to)) return;
        lock (WriteLock)
            foreach (var binding in _bySlotKey.Values)
                if (ReferenceEquals(binding.Registry, from))
                {
                    binding.Registry = to;
                    binding.ResolvedDefinition = null;
                }
    }

    private static bool Declares(BehaviorDefinition def, int slotKey)
    {
        var slots = def.StatefulWorkingSlots;
        if (slots is null) return false;
        for (int i = 0; i < slots.Count; i++)
            if (slots[i].SlotKey == slotKey && HostedSubtree.IsTreeStateSlot(slots[i])) return true;
        return false;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S5c (<c>DESIGN_Unified_Behaviour_Run</c> U-9) — refuse a HOSTING CYCLE at registration.</b> Walks the real
    /// hosting edges (a definition's hosted slots → the child each is bound to, by name) from <paramref name="hostName"/>
    /// and throws, naming the ring, if a path returns to it. ⭐ Called when a definition registers AND when an edge binds:
    /// registration order is arbitrary, and whichever of the two lands last is the one that closes the cycle. ⛔ A cycle
    /// would otherwise recurse without a base case at the first tick (and at provisioning, which has a depth guard).
    /// ⚠ Startup-only; it allocates.
    /// </summary>
    internal static void ThrowOnCycleThrough(BehaviorRegistry registry, string hostName)
    {
        var path = new List<string> { hostName };
        Walk(registry, hostName, path);

        static void Walk(BehaviorRegistry registry, string name, List<string> path)
        {
            if (!registry.TryGetId(name, out int id) || !registry.TryGetDefinition(id, out var def)) return;
            var slots = def.StatefulWorkingSlots;
            if (slots is null || path.Count > HostedSubtree.MaxNestingDepth) return;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!HostedSubtree.IsTreeStateSlot(slots[i])) continue;
                if (!_bySlotKey.TryGetValue(slots[i].SlotKey, out var edge) || !ReferenceEquals(edge.Registry, registry)) continue;
                string child = edge.ChildName.Trim();
                if (string.Equals(child, path[0], StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"S5c: hosting cycle {string.Join(" → ", path)} → {child}. A behaviour may not host itself, directly or " +
                        "through its children — the run would nest without end. Break the ring in one of these assets.");
                if (path.Contains(child)) continue;   // a cycle not through the start; its own check reports it
                path.Add(child);
                Walk(registry, child, path);
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    /// <summary>⭐ The BTree interpreter of the child bound to this slot, or <c>false</c> (no binding, unresolved, or not a BTree).</summary>
    public static bool TryGet(int treeStateSlotKey, out Interpreter<byte, BTreeContext> interpreter)
    {
        interpreter = null!;
        if (!_bySlotKey.TryGetValue(treeStateSlotKey, out var binding)) return false;
        if (ResolveDefinition(binding)?.BTreeInterpreter is not { } resolved) return false;
        interpreter = resolved;
        return true;
    }

    /// <summary>
    /// ⭐⭐ The BTree interpreter of the child bound to this slot. ⛔ <b>THROWS rather than returning null</b> — the same
    /// choice <c>HostedSubtree.Tick</c> makes for a missing slot (§19.6 ⑤). ⚠ BTree-only; the tier-neutral form is
    /// <see cref="RequireDefinition"/>.
    /// </summary>
    public static Interpreter<byte, BTreeContext> Require(int treeStateSlotKey)
        => RequireDefinition(treeStateSlotKey).BTreeInterpreter
           ?? throw new InvalidOperationException(
               $"The hosted child bound to tree-state slot {treeStateSlotKey} is not a BTree behaviour.");

    /// <summary>
    /// ⭐⭐⭐ <b>S5a — the child bound to this slot, of ANY tier</b> (<c>DESIGN_Unified_Behaviour_Run</c> §4 S5a).
    /// ⛔ Throws rather than returning null. ⚠ <b>Two distinct failures, two messages</b>, because they have different
    /// fixes: no binding at all means the HOST's registrar never called <see cref="Register"/>; a binding that will not
    /// resolve means the CHILD is absent from the registry or has no runner (no brain tier).
    /// </summary>
    public static BehaviorDefinition RequireDefinition(int treeStateSlotKey)
    {
        if (!_bySlotKey.TryGetValue(treeStateSlotKey, out var binding))
            throw new InvalidOperationException(
                $"No hosted child is bound to tree-state slot {treeStateSlotKey}. The host's generated " +
                "Register() binds it with HostedChildren.Register(beh, key, childName).");

        return ResolveDefinition(binding)
            ?? throw new InvalidOperationException(
                $"The hosted child '{binding.ChildName}' bound to tree-state slot {treeStateSlotKey} does " +
                "not resolve: it is not registered in that BehaviorRegistry, or it has no brain tier to run on.");
    }

    /// <summary>
    /// ⚠ Resolve-and-cache. ⛔ A failed resolve is NOT cached — the child may simply not have been
    /// registered yet, which is the entire reason this is lazy.
    /// ⭐ S5a: any tier with a runner resolves (BTree, HSM, blueprint); it used to require a BTree interpreter.
    /// </summary>
    private static BehaviorDefinition? ResolveDefinition(Binding binding)
    {
        if (binding.ResolvedDefinition is { } cached) return cached;

        if (!binding.Registry.TryGetId(binding.ChildName, out int id)) return null;
        if (!binding.Registry.TryGetDefinition(id, out var def)) return null;
        if (def.Runner is null) return null;

        binding.ResolvedDefinition = def;
        return def;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-431</c> — the CHILD's definition, which is what sizes and seeds its block.
    /// ⚠ Same lazy resolve as <see cref="TryGet"/>, same reason (<c>CE-377</c>).
    /// </summary>
    public static bool TryGetDefinition(int treeStateSlotKey, out BehaviorDefinition definition)
    {
        definition = null!;
        if (!_bySlotKey.TryGetValue(treeStateSlotKey, out var binding)) return false;
        if (ResolveDefinition(binding) is not { } def) return false;
        definition = def;
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
