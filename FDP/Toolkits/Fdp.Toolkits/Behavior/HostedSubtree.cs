using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b>THE hosting call — one body, used by the generated orchestrator AND by hand-written
/// hosts.</b> <c>O4</c> / task <c>C1</c> — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §19.
///
/// <para>🔴 <b>What it replaces.</b> <c>BTreeOrchestratorEmitCore</c> emitted
/// <c>child.Tick(ref subBb, <b>ref state</b>, ref ctx)</c> at <c>:142</c> and <c>:171</c> — handing the
/// child the MASTER's <see cref="BehaviorTreeState"/>. One 64-byte struct, one
/// <c>RunningNodeIndex</c> ⇒ host and child share a cursor, and the host wins because its
/// <c>ExecuteAction</c> writes AFTER the hosting action returns. Rail
/// <c>HostedSubtreeCursorTests.O4_R1</c> measures it (§18).</para>
///
/// <para>⛔⛔ <b>Why a runtime helper and not a baked pointer.</b> §19.6 ③: a host written in C# is
/// not an <c>AdditionalText</c> and is INVISIBLE to the generator, and hand-written trees are
/// production here. ⇒ the hosting site supplies its key and the resolution happens at runtime;
/// the emitter's const-bake of that key is an optimisation, not the contract.</para>
///
/// <para>⭐ <b>Zero ExtDeps.</b> <see cref="BehaviorTreeState"/> is untouched — the slot holds the
/// existing 64-byte struct. No new delegate parameter, no <c>BTreeContext</c> field, no kernel
/// change, which is what keeps <c>O4</c> a valid stop-or-go gate (§4.1).</para>
/// </summary>
public static unsafe class HostedSubtree
{
    /// <summary>Payload bytes one hosted occurrence's CURSOR costs. ⚠ §19.4's sizing term — the base of the slot.</summary>
    public static int TreeStatePayloadSize => sizeof(BehaviorTreeState);

    /// <summary>
    /// ⭐ <c>CE-431</c> — the START WORD's offset in a hosted child's slot, right after the cursor.
    /// <c>0</c> = the next tick is a fresh START (run the pipeline); <c>1</c> = running.
    /// </summary>
    public static int StartWordOffset => sizeof(BehaviorTreeState);

    /// <summary>
    /// ⭐⭐ <c>CE-431</c> — where the child's own blackboard BLOCK begins in its slot:
    /// <c>[cursor][start word][block]</c>, 8-aligned. ⭐ The cursor stays at the BASE, so every existing
    /// cursor reader is correct by construction (<c>Q76</c> §12.1).
    /// </summary>
    public static int BlockOffset => (sizeof(BehaviorTreeState) + sizeof(int) + 7) & ~7;

    /// <summary>
    /// ⭐⭐ S5a (<c>DESIGN_Unified_Behaviour_Run</c> §4) — the child's BRAIN width, from ITS runner: a BTree cursor (64), an
    /// HSM instance (64/128/256), a blueprint's <c>Exec</c>. ⚠ An unresolved child counts as a BTree cursor, as before.
    /// </summary>
    public static int BrainBytesFor(BehaviorDefinition? childDef)
        => childDef?.Runner is { } runner ? runner.BrainBytes(childDef) : sizeof(BehaviorTreeState);

    /// <summary>⭐ S5a — the start word's offset for this child: right after its brain. ⭐ A BTree child: 64, as before.</summary>
    public static int StartWordOffsetFor(BehaviorDefinition? childDef) => BrainBytesFor(childDef);

    /// <summary>⭐ S5a — the child's block offset: <c>[brain][start word][block]</c>, 8-aligned. ⭐ A BTree child: 72, as before.</summary>
    public static int BlockOffsetFor(BehaviorDefinition? childDef) => (BrainBytesFor(childDef) + sizeof(int) + 7) & ~7;

    /// <summary>
    /// ⭐⭐ <c>CE-431</c> — which bytes of the HOST's block seed this site's child, baked by the host's
    /// registrar. <see cref="Length"/> must equal the child's <c>RootParamsAccess.InputBytes</c>.
    /// ⭐ <c>default</c> is UNBOUND: the child starts from its own authored defaults.
    /// </summary>
    public readonly record struct SiteBinding(int HostOffset, int Length)
    {
        /// <summary>⭐ The unbound site.</summary>
        public static SiteBinding Unbound => default;
        /// <summary><c>true</c> when a host variable feeds this site.</summary>
        public bool IsBound => Length > 0;
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-431</c> — a hosted child's full slot size: <c>[cursor][start word][block]</c>, the block
    /// sized from the CHILD's definition. ⚠ A child with no block still gets the start word.
    /// </summary>
    public static int SlotPayloadSizeFor(BehaviorDefinition? childDef)
    {
        int block = childDef is null ? 0 : RootParamsAccess.RootParamsBytes(childDef);
        int blockOffset = BlockOffsetFor(childDef);   // ⭐ S5a — after the CHILD's brain, whatever its tier
        return block > 0 ? blockOffset + block : blockOffset;
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-431</c> — the manifest as INGRESS must provision it: every hosted tree-state slot
    /// resized to <see cref="SlotPayloadSizeFor"/> its child, and its hash folded with the child's block
    /// type and size so a hot-reloaded child whose block changed is re-attached rather than reused.
    ///
    /// <para>⛔⛔ <b>Why here and not in the emitted manifest.</b> The host's manifest is built at
    /// registration, and registrars run in an arbitrary order — the child may not exist yet
    /// (<c>CE-377</c>); a hand-written host has no emitter at all. ⭐ By ingress every registrar has run,
    /// and nothing after ingress may grow the store (a structural change inside a tick).</para>
    ///
    /// <para>⚠ Returns the SAME list when it hosts nothing — the common case allocates nothing.</para>
    /// </summary>
    public static IReadOnlyList<StatefulSlotInfo> EffectiveSlots(IReadOnlyList<StatefulSlotInfo> slots)
    {
        if (slots is null) return slots!;
        StatefulSlotInfo[]? copy = null;
        List<StatefulSlotInfo>? nested = null;
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (!IsTreeStateSlot(s)) continue;

            HostedChildren.TryGetDefinition(s.SlotKey, out var childDef);
            copy ??= ToArray(slots);
            copy[i] = Sized(s, s.SlotKey, childDef);

            // ⭐⭐ S5b — the child's OWN hosted sites, recursively, under their nested keys: provisioning is recursive
            //   because a grandchild's slot cannot be attached later (a structural change inside a tick).
            AppendNested(ref nested, s.SlotKey, childDef, depth: 1);
        }
        if (nested is null) return copy ?? slots;
        var all = new List<StatefulSlotInfo>(copy ?? (IEnumerable<StatefulSlotInfo>)slots);
        all.AddRange(nested);
        return all;
    }

    /// <summary>⭐ S5b — the deepest hosting chain provisioning follows. A deeper one is a cycle the registration check missed.</summary>
    public const int MaxNestingDepth = 16;

    private static void AppendNested(ref List<StatefulSlotInfo>? nested, int parentKey, BehaviorDefinition? def, int depth)
    {
        var slots = def?.StatefulWorkingSlots;
        if (slots is null) return;
        for (int i = 0; i < slots.Count; i++)
        {
            var c = slots[i];
            if (!IsTreeStateSlot(c))
            {
                // ⭐ S5b step 2 — the hosted child's OWN working-state slots (its stateful nodes), under the child's
                //   occurrence key: its generated thunks resolve HostedKeyAt(ctx.OccurrenceKey, key), so each site gets its own.
                (nested ??= new List<StatefulSlotInfo>()).Add(c with { SlotKey = OccurrenceSlots.HostedKeyAt(parentKey, c.SlotKey) });
                continue;
            }
            if (depth >= MaxNestingDepth)
                throw new InvalidOperationException(
                    $"S5b: behaviour '{def!.Name}' hosts children more than {MaxNestingDepth} levels deep — a hosting cycle. " +
                    "Registration refuses cycles (S5c); reaching here means one slipped past it.");

            HostedChildren.TryGetDefinition(c.SlotKey, out var gc);
            int actual = OccurrenceSlots.HostedKeyAt(parentKey, c.SlotKey);
            (nested ??= new List<StatefulSlotInfo>()).Add(Sized(c, actual, gc));
            AppendNested(ref nested, actual, gc, depth + 1);
        }
    }

    /// <summary>
    /// ⭐ S5b — every hosted child definition under <paramref name="slots"/>, at every depth, ONE ENTRY PER OCCURRENCE (so a
    /// child at two sites appears twice). Ingress sizes the store from it: a child's lazily-attached occurrences cannot grow
    /// the store mid-tick, so their demand is counted up front. ⚠ Ingress-only — it allocates.
    /// </summary>
    public static List<BehaviorDefinition> HostedDescendants(IReadOnlyList<StatefulSlotInfo>? slots)
    {
        var found = new List<BehaviorDefinition>();
        Collect(found, slots, 0);
        return found;

        static void Collect(List<BehaviorDefinition> found, IReadOnlyList<StatefulSlotInfo>? slots, int depth)
        {
            if (slots is null || depth >= MaxNestingDepth) return;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!IsTreeStateSlot(slots[i]) || !HostedChildren.TryGetDefinition(slots[i].SlotKey, out var child)) continue;
                found.Add(child);
                Collect(found, child.StatefulWorkingSlots, depth + 1);
            }
        }
    }

    /// <summary>A hosted slot at <paramref name="key"/>, sized from its child and hashed so a reloaded child re-attaches.</summary>
    private static StatefulSlotInfo Sized(StatefulSlotInfo s, int key, BehaviorDefinition? childDef)
    {
        int size = SlotPayloadSizeFor(childDef);
        uint hash = s.StructureHash;
        unchecked
        {
            hash = (hash ^ (uint)size) * 16777619u;
            if (childDef?.BlackboardLayoutType is { } t)
                hash = (hash ^ TypeNameHash(t.FullName!)) * 16777619u;   // ⭐ CE-2033 — deterministic (was string.GetHashCode)
        }
        return s with { SlotKey = key, PayloadSize = size, StructureHash = hash };
    }

    private static StatefulSlotInfo[] ToArray(IReadOnlyList<StatefulSlotInfo> slots)
    {
        var a = new StatefulSlotInfo[slots.Count];
        for (int i = 0; i < a.Length; i++) a[i] = slots[i];
        return a;
    }

    /// <summary>
    /// ⭐⭐ Ticks <paramref name="child"/> against ITS OWN <see cref="BehaviorTreeState"/>, resolved
    /// from the entity's occurrence store by <paramref name="treeStateSlotKey"/>.
    ///
    /// <para>⭐ <b><c>D4</c> is in TWO halves and this is only the first</b> — see <see cref="Reset"/>.
    /// ⚠ A hosting action runs only when the host ENTERS it, so it can never observe the host
    /// ABANDONING a still-<c>Running</c> child. That is the <c>F14</c> case and it needs the
    /// deactivator.</para>
    ///
    /// <para>⛔⛔ <b>A missing slot is a HARD failure, not a silent one</b> — §19.6 ⑤. The hosting site
    /// owns its own identity now, so a wrong key would otherwise read as
    /// <i>"the subtree just fails"</i>: exactly the silent slot miss <c>A1</c> exists to kill.</para>
    /// </summary>
    public static NodeStatus Tick<TChildBb>(
        Interpreter<TChildBb, BTreeContext> child,
        ref TChildBb childBb,
        ref BTreeContext ctx,
        int treeStateSlotKey)
        where TChildBb : unmanaged
    {
        if (child is null) throw new ArgumentNullException(nameof(child));

        ref var state = ref ResolveState(ref ctx, treeStateSlotKey);

        var status = child.Tick(ref childBb, ref state, ref ctx);

        // ⭐ D4, HALF ONE — the child COMPLETED, so it starts fresh next entry.
        // ⚠ The interpreter's own cleanup already zeroes RunningNodeIndex on a non-Running result;
        //   this is the DEEPER reset — StackPointer, NodeIndexStack, LocalRegisters, InstanceFlags —
        //   which the kernel does not clear and which would otherwise leak into the next entry.
        // ⛔ It is NOT the F14 case: see Reset, which handles the child left RUNNING.
        if (status != NodeStatus.Running)
            state = default;

        return status;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-431</c> — THE hosting call: the child ticks against ITS OWN block, seeded at its
    /// start.</b> 📄 <c>Architect_Question_76</c> §11.7. Both hosts reach it: a BTree node through
    /// <c>OccurrenceSubtreeHost</c> and an HSM state through <c>HsmRunner.TickHostedChildren</c>.
    ///
    /// <para>🔴 <b>What it replaces — <c>TickFromContext</c> (<c>CE-362</c>).</b> That handed every child
    /// the ENTITY's root params region, re-probed every tick: a child read its host's bytes LIVE and
    /// had nowhere of its own. 🔒 User, <c>2026-09-29</c>: <i>"I need each behavior having its own
    /// allocated slot, and resolving behavior params (that are stored in host blackboard as variable)
    /// exactly once at starting the sub-behavior."</i></para>
    ///
    /// <para>⭐⭐ <b><paramref name="hostBlock"/> is the HOST's block</b> — the <c>bb</c> the host was
    /// itself ticked with — so a child of a child seeds from its PARENT, never from the entity's root.
    /// ⭐ A fresh START runs the pipeline (<see cref="StartChild"/>); completion and abandonment clear the
    /// start word so the NEXT entry is a fresh start again.</para>
    /// </summary>
    public static NodeStatus TickHosted(ref byte hostBlock, ref BTreeContext ctx, int treeStateSlotKey, SiteBinding binding)
    {
        // ⭐⭐⭐ S5a — the child may be ANY tier; its runner says how wide its brain is, how to start it and how to step it.
        var childDef = HostedChildren.RequireDefinition(treeStateSlotKey);
        var runner = childDef.Runner!;

        // ⭐⭐ S5b — the slot is the TEMPLATE key nested under the occurrence this host runs as (0 at the root ⇒ unchanged).
        int occurrenceKey = OccurrenceSlots.HostedKeyAt(ctx._occurrenceKey, treeStateSlotKey);
        byte* payload = ResolvePayload(ref ctx, occurrenceKey, out int payloadSize);
        int brainBytes = runner.BrainBytes(childDef);
        int blockBytes = RootParamsAccess.RootParamsBytes(childDef);
        int blockOffset = BlockOffsetFor(childDef);
        int needed = SlotPayloadSizeFor(childDef);
        if (payloadSize < needed)
            throw new InvalidOperationException(
                $"CE-431: hosted slot {treeStateSlotKey} for child '{childDef.Name}' holds {payloadSize} bytes " +
                $"but [brain][start][block] needs {needed}. It was provisioned from the raw manifest — " +
                "BehaviorIngressSystem must provision HostedSubtree.EffectiveSlots.");

        ref int start  = ref Unsafe.AsRef<int>(payload + brainBytes);
        ref byte block = ref blockBytes > 0 ? ref Unsafe.AsRef<byte>(payload + blockOffset) : ref BehaviorBlock.None;

        if (start == 0)
        {
            runner.Start(childDef, payload, brainBytes);
            ResetDescendants(ctx.World, ctx.Self, childDef, occurrenceKey, depth: 1);   // ⭐ S5b — a fresh start all the way down
            StartChild(treeStateSlotKey, childDef, payload + blockOffset, blockBytes, ref hostBlock, binding,
                       ctx.World, ctx.Self);
            start = 1;
        }

        // ⭐ U-10 — the child runs under its HOST's InstanceId, so its channels reset with the host.
        var run = new Runners.BehaviorRunContext
        {
            World = ctx.World, Self = ctx.Self, Definition = childDef, InstanceId = ctx._instanceId,
            Ecb = ((Fdp.ModuleHost.Abstractions.ISimulationView)ctx.World).GetCommandBuffer(), DeltaTime = ctx._deltaTime,
            OccurrenceKey = occurrenceKey,
        };
        var status = runner.Tick(ref run, payload, brainBytes, ref block);

        // ⭐ D4, HALF ONE + CE-431 — completed ⇒ the next entry is a fresh START.
        if (status != NodeStatus.Running)
        {
            new Span<byte>(payload, brainBytes).Clear();
            start = 0;
            ResetDescendants(ctx.World, ctx.Self, childDef, occurrenceKey, depth: 1);   // ⭐ S5b — its children end with it
        }
        return status;
    }

    /// <summary>
    /// ⭐⭐ S5d (<c>DESIGN_Unified_Behaviour_Run</c> §4) — <b>a blueprint behaviour's Run Behaviour node steps its child.</b> The
    /// same body every host uses (<see cref="TickHosted"/>); the BTree context a blueprint does not have is built here, on the
    /// stack, carrying the blueprint run's own occurrence key so the child nests under it.
    /// </summary>
    public static NodeStatus TickFromBlueprint(ref byte hostBlock, EntityRepository world, Entity self, float deltaTime,
                                               uint instanceId, int occurrenceKey, int siteKey)
        => TickFromBlueprint(ref hostBlock, world, self, deltaTime, instanceId, occurrenceKey, siteKey, SiteBinding.Unbound);

    /// <summary>
    /// ⭐⭐ S8 / <c>CE-2022</c> (<c>DESIGN_Unified_Behaviour_Run</c> "S8 design") — the same step, with the slice of the host's
    /// block that seeds the child at its start: the Behaviour Task's parameters (a host variable, P2), exactly the
    /// <see cref="SiteBinding"/> a BTree or HSM host passes (CE-431).
    /// </summary>
    public static NodeStatus TickFromBlueprint(ref byte hostBlock, EntityRepository world, Entity self, float deltaTime,
                                               uint instanceId, int occurrenceKey, int siteKey, SiteBinding binding)
    {
        var ctx = new BTreeContext
        {
            Self = self, World = world, _deltaTime = deltaTime, _frameCount = (int)world.SimulationTick,
            _floatParams = Array.Empty<float>(), _intParams = Array.Empty<int>(), _instanceId = instanceId,
            _occurrenceKey = occurrenceKey,
        };
        return TickHosted(ref hostBlock, ref ctx, siteKey, binding);
    }

    /// <summary>
    /// ⭐⭐ One hosted child's slot declaration, for a host's manifest. ⛔ <b>Role=State / Scope=Behavior with
    /// <c>WorkingStateType == typeof(BehaviorTreeState)</c></b> is the marker <see cref="IsTreeStateSlot"/> tests (the size is
    /// re-derived from the child at ingress, <see cref="EffectiveSlots"/>). ⭐ S5d: public so a blueprint registrar declares its
    /// Run Behaviour sites with the SAME shape a BTree host's plan does — one spelling, not two.
    /// </summary>
    public static StatefulSlotInfo SiteSlot(int key, string childName)
    {
        int size = TreeStatePayloadSize;
        return new StatefulSlotInfo(
            key, size, unchecked(TypeNameHash("Fbt.BehaviorTreeState") ^ (uint)size), typeof(BehaviorTreeState),
            childName + " (hosted)",
            (byte)Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotRole.State,
            (byte)Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotScope.Behavior);
    }

    /// <summary>The type-name hash the emitters bake — ⭐ CE-2033: THE one (<c>OccurrenceSlotKey.TypeNameHash</c>).</summary>
    private static uint TypeNameHash(string typeName) => Shared.OccurrenceSlotKey.TypeNameHash(typeName);

    /// <summary>
    /// ⭐⭐⭐ <c>CE-431</c>/<c>CE-443</c> — <b>the child's start pipeline, run at every START</b>: clear → bake →
    /// EITHER the default copy OR the child's resolver, which is handed the SOURCE.
    /// 📄 <c>DESIGN_Parameter_Model.md</c> §P.2 (<c>R-155</c>); "every start from empty" is <c>R-153</c>.
    ///
    /// <para>⭐ No resolver: stage 2 copies the BOUND host variable's bytes into the child's Input region, one
    /// <c>memcpy</c>. ⛔ A width mismatch THROWS: it can only mean the host's variable is not the child's
    /// Input struct.</para>
    ///
    /// <para>⭐ A resolver (a bound resolver asset, or a curated TYPED resolver — <c>CE-438</c>): the host
    /// variable's bytes are its source and nothing is copied for it. ⛔ A curated JSON-shaped resolver
    /// THROWS: a JSON parse cannot consume host bytes.</para>
    /// </summary>
    private static void StartChild(
        int treeStateSlotKey, BehaviorDefinition? childDef, byte* block, int blockBytes,
        ref byte hostBlock, SiteBinding binding, EntityRepository ctxWorld, Entity ctxSelf)
    {
        ResolveStageDelegate? resolve = childDef?.ResolveStage;
        if (childDef is not null
            && HostedChildren.TryGetRegistry(treeStateSlotKey, out var registry, out var childName)
            && registry.HasCuratedResolver(childName))
        {
            if (!registry.TryGetSourceResolver(childName, out var curated))
                throw new NotSupportedException(
                    $"CE-443: hosted child '{childName}' has a JSON-shaped [BehaviorResolver]. A hosted child's " +
                    "source is its host variable's BYTES, which a JSON parse cannot consume. Give the resolver " +
                    "the typed shape (in TAuthored authored, ref TBlock block, …) with an unmanaged TAuthored, " +
                    "or root-assign the behaviour.");
            resolve = curated;
        }

        if (blockBytes > 0)
        {
            new Span<byte>(block, blockBytes).Clear();          // stage 0 — from empty (R-153)
            childDef!.BakeDefaults?.Invoke(block, blockBytes);  // stage 1 — the child's authored defaults
        }

        if (resolve is null)
        {
            if (binding.IsBound) Supply(childDef!, block, blockBytes, ref hostBlock, binding);   // stage 2 — copy
            return;
        }

        // ⭐⭐ CE-443 — stage 2 IS the resolver, handed the source. ⚠ No shadow here: a throw leaves the start
        //   word 0, so the child never ticks on a half-resolved block and the next tick re-runs from empty.
        byte* source = null;
        int sourceBytes = 0;
        if (binding.IsBound)
        {
            if (!BehaviorBlock.Has(ref hostBlock))
                throw new InvalidOperationException(
                    $"CE-443: child '{childDef!.Name}' is bound to a host variable, but its host has no blackboard block.");
            source = (byte*)Unsafe.AsPointer(ref Unsafe.AddByteOffset(ref hostBlock, (nint)binding.HostOffset));
            sourceBytes = binding.Length;
        }
        if (blockBytes > 0)
            resolve(source, sourceBytes, block, blockBytes, ctxWorld, ctxSelf);
    }

    private static void Supply(BehaviorDefinition childDef, byte* block, int blockBytes,
                               ref byte hostBlock, SiteBinding binding)
    {

        int inputBytes = RootParamsAccess.InputBytes(childDef);
        if (binding.Length != inputBytes || blockBytes < inputBytes)
            throw new InvalidOperationException(
                $"CE-431: the host variable bound to child '{childDef.Name}' is {binding.Length} bytes but the " +
                $"child's Input region is {inputBytes}. The bound variable must be the child's Input struct.");
        if (!BehaviorBlock.Has(ref hostBlock))
            throw new InvalidOperationException(
                $"CE-431: child '{childDef.Name}' is bound to a host variable, but its host has no blackboard block.");

        byte* src = (byte*)Unsafe.AsPointer(ref Unsafe.AddByteOffset(ref hostBlock, (nint)binding.HostOffset));
        Buffer.MemoryCopy(src, block, blockBytes, binding.Length);   // stage 2 — supply
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>D4</c>, HALF TWO — the <c>F14</c> case: the host ABANDONS a child that is still
    /// <c>Running</c>.</b> Registered as the hosting node's <b>deactivator</b>.
    ///
    /// <para>🔴🔴 <b>Why <see cref="Tick"/> alone cannot do this.</b> A hosting action only runs when
    /// the host ENTERS it. If the host leaves the hosting node while the child is mid-tree — a
    /// sibling fails, a Selector moves on, the host resets — the hosting action is never called
    /// again, so nothing clears the child's cursor and the next entry RESUMES MID-TREE. ⚠ Sharing
    /// the master's state used to hide this: the host's own write clobbered the child every tick, so
    /// the reset was ACCIDENTAL. ⇒ own state removes it, and <c>F14</c> is the bill.</para>
    ///
    /// <para>⭐⭐ <b>And the hook already exists — zero ExtDeps.</b> <c>Interpreter.SweepExitedNodes</c>
    /// invokes a deactivator for any node that leaves the active path and is
    /// <c>IsResourceOwning</c>; <c>BTreeBuilder.Compile</c> sets that bit <b>automatically</b> for a
    /// node whose key has a registered deactivator. ⇒ registering this IS the opt-in.</para>
    /// </summary>
    public static void Reset(ref BTreeContext ctx, int treeStateSlotKey)
        => Reset(ctx.World, ctx.Self, treeStateSlotKey, ctx._occurrenceKey);

    /// <summary>
    /// ⭐⭐ <b><c>D4</c>, HALF TWO — the EXTERNAL form.</b> Same body, reached without a
    /// <see cref="BTreeContext"/>, because the reset does not always arrive through a tick.
    ///
    /// <para>🔴 <b><c>F14b</c> — why this overload exists.</b> <c>BehaviorIngressSystem</c> zeroes the
    /// host's <c>BrainBTreeState.State</c> on a behaviour change <b>without ticking</b>
    /// (<c>:163</c> on assign-by-name, <c>:235</c> on assign-by-hash). ⇒ <c>SweepExitedNodes</c> never
    /// runs, so the deactivator above never fires, and a hosted child that was <c>Running</c> keeps its
    /// cursor while the host restarts from the root. ⚠ The ingress has <c>(repo, entity)</c> and no
    /// context — hence the split. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §21.2.</para>
    /// </summary>
    public static void Reset(EntityRepository world, Entity self, int treeStateSlotKey, int parentOccurrenceKey = 0)
        => ResetAt(world, self, treeStateSlotKey, parentOccurrenceKey, depth: 0);

    /// <summary>
    /// ⭐⭐ S5b — the reset is RECURSIVE: a child that is abandoned or restarted takes its own hosted children with it, so a
    /// grandchild never resumes mid-run under a host that started over (I7).
    /// </summary>
    private static void ResetAt(EntityRepository world, Entity self, int templateKey, int parentOccurrenceKey, int depth)
    {
        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return;   // ⚠ torn down already — nothing to reset, and not an error

        int key = OccurrenceSlots.HostedKeyAt(parentOccurrenceKey, templateKey);
        HostedChildren.TryGetDefinition(templateKey, out var childDef);
        if (BlueprintBlackboardPartitions.TryGetSlotIndex(store, key, out int index))
        {
            ref var entry = ref BlueprintBlackboardPartitions.GetSlot(store, index);
            byte* payload = store + entry.PayloadOffset;
            // ⭐ S5a — zero the CHILD's brain at its own width (a BTree cursor, an HSM instance, a blueprint Exec).
            int brainBytes = Math.Min(BrainBytesFor(childDef), entry.PayloadSize);
            new Span<byte>(payload, brainBytes).Clear();
            // ⭐ CE-431 — an abandoned child's next entry is a fresh START.
            if (entry.PayloadSize >= brainBytes + sizeof(int))
                Unsafe.AsRef<int>(payload + brainBytes) = 0;
        }
        ResetDescendants(world, self, childDef, key, depth + 1);
    }

    private static void ResetDescendants(EntityRepository world, Entity self, BehaviorDefinition? def, int occurrenceKey, int depth)
    {
        var slots = def?.StatefulWorkingSlots;
        if (slots is null || depth > MaxNestingDepth) return;
        for (int i = 0; i < slots.Count; i++)
            if (IsTreeStateSlot(slots[i]))
                ResetAt(world, self, slots[i].SlotKey, occurrenceKey, depth);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The manifest test: is this slot a hosted occurrence's tree state?</b>
    ///
    /// <para>⭐ The emitter stamps <c>WorkingStateType = typeof(BehaviorTreeState)</c> on exactly the
    /// slots <c>BTreeBridgeEmitCore.CollectHostedTreeStateSlots</c> adds, and an authored
    /// <c>WorkingState</c> struct can never be that type — so the manifest itself says which slots
    /// carry a CURSOR rather than author state. ⛔ Nothing else in the manifest distinguishes them,
    /// and a caller re-spelling this test is how the two would drift.</para>
    ///
    /// <para>⚠ It is deliberately NARROW: an external reset must clear a hosted <i>cursor</i> and must
    /// NOT clear author working state, which survives a no-op re-assign on purpose
    /// (<c>AttachSlotsToMemory</c>'s idempotent arm).</para>
    /// </summary>
    public static bool IsTreeStateSlot(StatefulSlotInfo slot)
        => slot is not null && slot.WorkingStateType == typeof(BehaviorTreeState);

    /// <summary>
    /// The hosted occurrence's state bytes, as a <c>ref</c> into the entity's store.
    ///
    /// <para>⛔ <b>The reference is valid for the CALLING FRAME ONLY</b> — the seam's lifetime rule.
    /// Native ECS storage means a structural change can move the chunk, so it is resolved per tick
    /// and never cached.</para>
    /// </summary>
    private static ref BehaviorTreeState ResolveState(ref BTreeContext ctx, int treeStateSlotKey)
        => ref Unsafe.AsRef<BehaviorTreeState>(ResolvePayload(ref ctx, treeStateSlotKey, out _));

    /// <summary>The hosted slot's payload base and its allocated size. ⛔ Throws on a missing slot — §19.6 ⑤.</summary>
    private static byte* ResolvePayload(ref BTreeContext ctx, int treeStateSlotKey, out int payloadSize)
    {
        byte* store = OccurrenceStoreAccess.TryGetStore(ctx.World, ctx.Self, out _);

        if (store == null)
            throw new InvalidOperationException(
                $"Entity {ctx.Self} carries no occurrence store, so hosted subtree slot " +
                $"{treeStateSlotKey} cannot be resolved. The hosting site's slot must be declared in " +
                "the behaviour's stateful manifest so BehaviorIngressSystem provisions it.");

        if (!BlueprintBlackboardPartitions.TryGetSlotIndex(store, treeStateSlotKey, out int slotIndex))
            throw new InvalidOperationException(
                $"Entity {ctx.Self} has an occurrence store but no slot {treeStateSlotKey} for the " +
                "hosted subtree's BehaviorTreeState. Either the manifest does not declare it, or the " +
                "hosting site computed a different key than the one registered — see " +
                "OccurrenceSlots.TreeStateKeyFor.");

        ref var entry = ref BlueprintBlackboardPartitions.GetSlot(store, slotIndex);
        payloadSize = entry.PayloadSize;
        return store + entry.PayloadOffset;
    }
}
