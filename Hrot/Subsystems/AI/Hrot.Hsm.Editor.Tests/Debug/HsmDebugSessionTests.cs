using Fdp.Core;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Debug;
using Hrot.Hsm.Editor.Debug;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Blueprints.Partitioning;
using System.Runtime.CompilerServices;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Debug;

public sealed class HsmDebugSessionTests
{
    private static Entity MakeEntity() => new Entity(1, 1);

    private static Guid AssetId => Guid.Parse("A1A1A1A1-0000-0000-0000-000000000001");

    private static HsmStateEntered MakeEnteredRecord(float t = 0f) =>
        new(MakeEntity(), AssetId, Guid.NewGuid(), t);

    private static HsmTransitionFired MakeFiredRecord(float t = 0f) =>
        new(MakeEntity(), AssetId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            EventId: 1, GuardResult: true, SyncGroupId: 0, SimulationTime: t);

    // -------------------------------------------------------------------------

    [Fact]
    public void Session_IsAttached_OnConstruction()
    {
        var session = new HsmDebugSession();
        session.IsAttached.Should().BeTrue();
    }

    [Fact]
    public void Session_IsNotPaused_OnConstruction()
    {
        var session = new HsmDebugSession();
        session.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void GetCurrentStateSnapshot_ReturnsNull()
    {
        var session = new HsmDebugSession();
        session.GetCurrentStateSnapshot().Should().BeNull();
    }

    [Fact]
    public void RecordTrace_StateEntered_AppearsInHistory()
    {
        var session = new HsmDebugSession();
        var record = MakeEnteredRecord(1.0f);

        session.RecordTrace(record);

        var history = session.GetRecentTraceHistory();
        history.Should().ContainSingle().Which.Should().Be(record);
    }

    [Fact]
    public void RecordTrace_TransitionFired_AppearsInHistory()
    {
        var session = new HsmDebugSession();
        var record = MakeFiredRecord(2.0f);

        session.RecordTrace(record);

        var history = session.GetRecentTraceHistory();
        history.Should().ContainSingle().Which.Should().Be(record);
    }

    [Fact]
    public void TraceHistory_CappedAt200()
    {
        var session = new HsmDebugSession();

        for (int i = 0; i < 250; i++)
            session.RecordTrace(MakeEnteredRecord(i));

        session.GetRecentTraceHistory(int.MaxValue).Count.Should().Be(200);
    }

    [Fact]
    public void GetRecentTraceHistory_RespectsMaxParameter()
    {
        var session = new HsmDebugSession();

        for (int i = 0; i < 50; i++)
            session.RecordTrace(MakeEnteredRecord(i));

        session.GetRecentTraceHistory(10).Count.Should().Be(10);
    }

    [Fact]
    public void GetRecentTraceHistory_ReturnsNewestRecords()
    {
        var session = new HsmDebugSession();

        for (int i = 0; i < 20; i++)
            session.RecordTrace(MakeEnteredRecord(i));

        var recent = session.GetRecentTraceHistory(5);
        recent.Select(r => r.SimulationTime).Should().Equal(15f, 16f, 17f, 18f, 19f);
    }

    [Fact]
    public void Detach_ClearsHistory()
    {
        var session = new HsmDebugSession();
        session.RecordTrace(MakeEnteredRecord());

        session.Detach();

        session.GetRecentTraceHistory().Should().BeEmpty();
    }

    [Fact]
    public void RecordTrace_StateEntered_FiresOnStateEnteredEvent()
    {
        var session = new HsmDebugSession();
        HsmStateEntered? received = null;
        session.OnStateEntered += e => received = e;

        var record = MakeEnteredRecord();
        session.RecordTrace(record);

        received.Should().Be(record);
    }

    [Fact]
    public void RecordTrace_TransitionFired_FiresOnTransitionFiredEvent()
    {
        var session = new HsmDebugSession();
        HsmTransitionFired? received = null;
        session.OnTransitionFired += e => received = e;

        var record = MakeFiredRecord();
        session.RecordTrace(record);

        received.Should().Be(record);
    }

    [Fact]
    public void RaiseBreakpointHit_SetsPausedState()
    {
        var session = new HsmDebugSession();
        var bp = new Breakpoint(new BreakpointId(1), AssetId, Guid.NewGuid(),
            HitCount: 0, Enabled: true, DisplayName: "test");
        var hit = new HsmBreakpointHit(bp, MakeEntity(), Guid.NewGuid(), null, 1.5f);

        session.RaiseBreakpointHit(hit);

        session.IsPaused.Should().BeTrue();
        session.PausedAt.Should().Be(bp);
        session.PausedOnEntity.Should().Be(hit.Self);
    }

    [Fact]
    public void RaiseBreakpointHit_FiresOnBreakpointHitEvent()
    {
        var session = new HsmDebugSession();
        HsmBreakpointHit? received = null;
        session.OnBreakpointHit += h => received = h;

        var bp = new Breakpoint(new BreakpointId(2), AssetId, Guid.NewGuid(),
            HitCount: 0, Enabled: true, DisplayName: "test");
        var hit = new HsmBreakpointHit(bp, MakeEntity(), Guid.NewGuid(), null, 2.0f);

        session.RaiseBreakpointHit(hit);

        received.Should().Be(hit);
    }

    [Fact]
    public void RaiseBreakpointHit_RaisesSessionStateChanged()
    {
        var session = new HsmDebugSession();
        int callCount = 0;
        session.OnSessionStateChanged += () => callCount++;

        var bp = new Breakpoint(new BreakpointId(3), AssetId, Guid.NewGuid(),
            HitCount: 0, Enabled: true, DisplayName: "test");
        var hit = new HsmBreakpointHit(bp, MakeEntity(), null, Guid.NewGuid(), 3.0f);

        session.RaiseBreakpointHit(hit);

        callCount.Should().Be(1);
    }

    [Fact]
    public void Pause_SetsPausedTrue()
    {
        var session = new HsmDebugSession();
        session.Pause();
        session.IsPaused.Should().BeTrue();
    }

    [Fact]
    public void Continue_ClearsPausedState()
    {
        var session = new HsmDebugSession();
        session.Pause();
        session.Continue();
        session.IsPaused.Should().BeFalse();
        session.PausedAt.Should().BeNull();
        session.PausedOnEntity.Should().BeNull();
    }

    [Fact]
    public void StepOver_FiresSessionStateChanged()
    {
        var session = new HsmDebugSession();
        int count = 0;
        session.OnSessionStateChanged += () => count++;
        session.StepOver();
        count.Should().Be(1);
    }

    [Fact]
    public void StepInto_FiresSessionStateChanged()
    {
        var session = new HsmDebugSession();
        int count = 0;
        session.OnSessionStateChanged += () => count++;
        session.StepInto();
        count.Should().Be(1);
    }

    [Fact]
    public void StepOut_FiresSessionStateChanged()
    {
        var session = new HsmDebugSession();
        int count = 0;
        session.OnSessionStateChanged += () => count++;
        session.StepOut();
        count.Should().Be(1);
    }

    [Fact]
    public void HeatmapMode_Off_RecordTrace_DoesNotIncrementCounters()
    {
        var session = new HsmDebugSession();
        var assetId = Guid.NewGuid();
        var record = new HsmStateEntered(
            new Entity(1, 1), assetId, Guid.NewGuid(), 0f);
        session.RecordTrace(record);
        session.GetStateEntryCounts(assetId).Should().BeNull();
    }

    [Fact]
    public void HeatmapMode_On_RecordTrace_StateEntered_IncrementsCounter()
    {
        var session = new HsmDebugSession();
        var stableId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var record = new HsmStateEntered(
            new Entity(1, 1), assetId, stableId, 0f);
        session.HeatmapModeActive = true;
        session.RecordTrace(record);
        session.RecordTrace(record);
        var counts = session.GetStateEntryCounts(assetId);
        counts.Should().NotBeNull();
        counts![stableId].Should().Be(2);
    }

    [Fact]
    public void ResetStateEntryCounts_ClearsAll()
    {
        var session = new HsmDebugSession();
        var stableId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var record = new HsmStateEntered(
            new Entity(1, 1), assetId, stableId, 0f);
        session.HeatmapModeActive = true;
        session.RecordTrace(record);
        session.ResetStateEntryCounts();
        session.GetStateEntryCounts(assetId)!.Should().BeEmpty();
    }

    [Fact]
    public void GetStateEntryCounts_NotAttached_ReturnsNull()
    {
        var session = new HsmDebugSession();
        session.HeatmapModeActive = true;
        session.Detach();
        session.GetStateEntryCounts(Guid.NewGuid()).Should().BeNull();
    }

    // ---- ECS Update() tests -----------------------------------------------

    /// <summary>
    /// ⚠⚠ <b>O7c-④d (2026-09-23): registering the TIER LADDER is not optional here.</b> The instance
    /// is an occurrence slot now, and a world that never registered the tier components silently has
    /// nowhere to put one — <c>RootHsmAccess</c> skips rather than throwing, so every assertion below
    /// would read a null snapshot and the failure would look like a decode bug.
    /// 📄 the trap is §31.16.8, filed as <c>CE-321</c>.
    /// </summary>
    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BehaviorState>();
        world.RegisterComponent<HsmTraceWorkingMemory1024>();
        BlueprintTierTable.RegisterAll(world);
        return world;
    }

    /// <summary>The host behaviour the root HSM slot is keyed from — arbitrary, but NON-ZERO.</summary>
    private const int HostBehaviorHash = 0x0D6B5E55;

    /// <summary>
    /// ⭐⭐⭐ <b>Put an instance where the session now READS it — the entity's root HSM slot.</b>
    ///
    /// <para>⛔ This is not a type swap for <c>world.AddComponent(entity, brain)</c>. A component was
    /// addressed by TYPE and sized by the compiler; a slot is addressed by a KEY computed from
    /// <c>BehaviorState.ActiveBehaviorHash</c> and sized at attach. ⇒ three things the component
    /// version got for free now have to be arranged: a behaviour to key from, a store to live in,
    /// and a width. ⭐ That cost is the whole point — it is what lets the SAME entity hold a 64- or
    /// 256-byte machine, which <c>BrainHsm128</c> could never express.</para>
    /// </summary>
    private static unsafe void PlaceInstance(EntityRepository world, Entity entity, in HsmInstance128 instance)
    {
        EnsureHost(world, entity);
        fixed (HsmInstance128* src = &instance)
            AttachAndCopy(world, entity, (byte*)src, sizeof(HsmInstance128));
    }

    /// <summary>
    /// The behaviour the slot is keyed from, and a store big enough for the widest kernel tier.
    /// ⚠ Sized for 256 regardless of the instance actually placed, so the 64/256 rails exercise the
    /// DECODER rather than accidentally testing tier selection.
    /// </summary>
    private static unsafe void EnsureHost(EntityRepository world, Entity entity)
    {
        if (!world.HasComponent<BehaviorState>(entity))
            world.AddComponent(entity, new BehaviorState
            {
                ActiveBehaviorHash = HostBehaviorHash,
                BrainTier          = BehaviorConstants.BrainTierHsm,
                InstanceId         = 1,
            });

        if (BlueprintTierTable.Of(world, entity) is null)
        {
            var tier = BlueprintTierTable.Select(
                sizeof(HsmInstance256) + BlueprintBlackboardPartitions.SlotEntrySize, 1);
            tier.Add(world, entity);
            BlueprintBlackboardPartitions.Initialize(
                tier.Memory(world, entity), tier.TotalSize, (byte)tier.MaxSlots);
        }
    }

    /// <summary>The 64-tier twin of <see cref="PlaceInstance"/>.</summary>
    private static unsafe void PlaceInstance64(EntityRepository world, Entity entity, in HsmInstance64 instance)
    {
        EnsureHost(world, entity);
        fixed (HsmInstance64* src = &instance)
            AttachAndCopy(world, entity, (byte*)src, sizeof(HsmInstance64));
    }

    /// <summary>The 256-tier twin of <see cref="PlaceInstance"/>.</summary>
    private static unsafe void PlaceInstance256(EntityRepository world, Entity entity, in HsmInstance256 instance)
    {
        EnsureHost(world, entity);
        fixed (HsmInstance256* src = &instance)
            AttachAndCopy(world, entity, (byte*)src, sizeof(HsmInstance256));
    }

    private static unsafe void AttachAndCopy(EntityRepository world, Entity entity, byte* src, int width)
    {
        byte* slot = RootHsmAccess.ResolveOrAttachRoot(
            world, entity, HostBehaviorHash, width, OccurrenceKind.Hsm, out _);
        Assert.True(slot != null, "the root HSM slot must attach or the fixture proves nothing");

        Assert.True(RootHsmAccess.TryGetInstance(world, entity, out _, out int attachedWidth));
        Assert.Equal(width, attachedWidth);   // the slot is EXACTLY the tier, not merely big enough

        Unsafe.CopyBlock(slot, src, (uint)width);
    }

    [Fact]
    public void Update_WithNoRootHsmSlot_SnapshotRemainsNull()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();
        var sut    = new HsmDebugSession();

        sut.Update(world, entity);

        sut.GetCurrentStateSnapshot().Should().BeNull();
    }

    [Fact]
    public unsafe void Update_WithRootHsmSlot_SnapshotIsNotNull()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();
        var inst   = new HsmInstance128();
        inst.Header.Phase = InstancePhase.Activity;
        PlaceInstance(world, entity, inst);
        var sut = new HsmDebugSession();

        sut.Update(world, entity);

        sut.GetCurrentStateSnapshot().Should().NotBeNull();
    }

    [Fact]
    public unsafe void Update_WithRootHsmSlot_SnapshotHasCorrectPhase()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();
        var inst   = new HsmInstance128();
        inst.Header.Phase = InstancePhase.Activity;
        PlaceInstance(world, entity, inst);
        var sut = new HsmDebugSession();

        sut.Update(world, entity);

        sut.GetCurrentStateSnapshot()!.Phase.Should().Be(InstancePhase.Activity);
    }

    [Fact]
    public unsafe void Update_WithHsmTraceBuffer_PopulatesTraceHistory()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();
        var inst   = new HsmInstance128();
        inst.Header.Phase = InstancePhase.Idle;
        PlaceInstance(world, entity, inst);

        var mem = new HsmTraceWorkingMemory1024();
        mem.LastInstanceId = 1;
        // Write 3 StateEnter headers manually into the ring buffer.
        HsmTraceWorkingMemory1024* memPtr =
            (HsmTraceWorkingMemory1024*)Unsafe.AsPointer(ref mem);
        for (int i = 0; i < 3; i++)
        {
            var hdr = (TraceRecordHeader*)(memPtr->Buffer + mem.WritePos);
            *hdr = default;
            hdr->OpCode     = TraceOpCode.StateEnter;
            hdr->Timestamp  = (ushort)(i + 1);
            hdr->InstanceId = 1;
            mem.WritePos = (ushort)((mem.WritePos + HsmTraceWorkingMemory1024.RecordStride)
                                     % HsmTraceWorkingMemory1024.PayloadBytes);
            if (mem.RecordCount < HsmTraceWorkingMemory1024.CapacityRecords)
                mem.RecordCount++;
        }
        world.AddComponent(entity, mem);
        var sut = new HsmDebugSession();

        sut.Update(world, entity);

        sut.GetRecentTraceHistory(10).Should().HaveCount(3);
    }

    // ---- BPF-023: active-state decoding ----------------------------------

    [Fact]
    public unsafe void Update_WithRootHsmSlot_ActiveLeafIds_DecodedViaMetadata()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var inst  = new HsmInstance128();
        inst.Header.Phase     = InstancePhase.Activity;
        inst.ActiveLeafIds[0] = 1;
        inst.ActiveLeafIds[1] = 2;
        // ⚠ O7c-①: HsmInstance128 has FOUR leaf slots. A default 0 is a VALID leaf id, so the two
        //   this test does not use must carry the 0xFFFF sentinel or the count assertion below sees 4.
        inst.ActiveLeafIds[2] = 0xFFFF;
        inst.ActiveLeafIds[3] = 0xFFFF;
        PlaceInstance(world, entity, inst);

        var stableA = new Guid("aa000000-0000-0000-0000-000000000001");
        var stableB = new Guid("bb000000-0000-0000-0000-000000000002");
        var assetId = new Guid("cc000000-0000-0000-0000-000000000001");

        var metadata = new MachineMetadata();
        metadata.StateStableIds[1] = stableA;
        metadata.StateStableIds[2] = stableB;

        var sut = new HsmDebugSession();
        sut.SetMetadata(assetId, metadata);
        sut.Update(world, entity);

        var snap = sut.GetCurrentStateSnapshot();
        snap.Should().NotBeNull();
        snap!.AssetId.Should().Be(assetId);
        snap.ActiveLeafStableIds.Should().HaveCount(2);
        snap.ActiveLeafStableIds.Should().Contain(stableA);
        snap.ActiveLeafStableIds.Should().Contain(stableB);
    }

    [Fact]
    public unsafe void Update_WithRootHsmSlot_0xFFFF_NotIncludedInActiveLeaves()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var inst  = new HsmInstance128();
        inst.Header.Phase     = InstancePhase.Activity;
        inst.ActiveLeafIds[0] = 5;
        inst.ActiveLeafIds[1] = 0xFFFF; // empty slot
        inst.ActiveLeafIds[2] = 0xFFFF; // O7c-①: 128 has four
        inst.ActiveLeafIds[3] = 0xFFFF;
        PlaceInstance(world, entity, inst);

        var stableA  = new Guid("dd000000-0000-0000-0000-000000000005");
        var metadata = new MachineMetadata();
        metadata.StateStableIds[5] = stableA;

        var sut = new HsmDebugSession();
        sut.SetMetadata(Guid.NewGuid(), metadata);
        sut.Update(world, entity);

        var snap = sut.GetCurrentStateSnapshot();
        snap!.ActiveLeafStableIds.Should().HaveCount(1);
        snap.ActiveLeafStableIds[0].Should().Be(stableA);
    }

    // ---- BPF-024: StepOut uses Activity-phase predicate ------------------

    [Fact]
    public unsafe void StepOut_does_not_pause_while_in_Entry_phase()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var inst  = new HsmInstance128();
        inst.Header.Phase = InstancePhase.Entry;
        PlaceInstance(world, entity, inst);

        var spy = new SpyCoordinator();
        var sut = new HsmDebugSession(spy);
        sut.StepOut();
        sut.Update(world, entity);

        // Still in Entry phase -- StepOut must NOT request a pause yet.
        spy.PauseRequested.Should().BeFalse();
    }

    [Fact]
    public unsafe void StepOut_pauses_when_Activity_phase_reached()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var inst  = new HsmInstance128();
        inst.Header.Phase = InstancePhase.Activity;
        PlaceInstance(world, entity, inst);

        var spy = new SpyCoordinator();
        var sut = new HsmDebugSession(spy);
        sut.StepOut();
        sut.Update(world, entity);

        spy.PauseRequested.Should().BeTrue();
    }

    [Fact]
    public unsafe void StepOver_pauses_when_MicroStep_changes()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var inst  = new HsmInstance128();
        inst.Header.Phase     = InstancePhase.Entry;
        inst.Header.MicroStep = 1;
        PlaceInstance(world, entity, inst);

        var spy = new SpyCoordinator();
        var sut = new HsmDebugSession(spy);
        sut.StepOver(); // captures MicroStep=1

        inst.Header.MicroStep = 2;
        PlaceInstance(world, entity, inst);
        sut.Update(world, entity);

        spy.PauseRequested.Should().BeTrue();
    }

    // ---- BPF-010: event-queue, timer-slot and history-slot decoding ------

    /// <summary>
    /// ⛔⛔ <b>Re-homed onto the 128 tier by <c>O7c</c>-① (2026-09-22), and the LAYOUT is genuinely
    /// different — this is not a type swap.</b> <c>HsmInstance64</c> keeps ONE shared event slot at
    /// <c>EventBuffer[0]</c>; <c>HsmInstance128</c> keeps an INTERRUPT slot at <c>[0..23]</c> and a
    /// ring from <c>[24]</c>, and <c>DecodeEventQueue128</c> reads
    /// <c>InterruptSlotUsed + EventCount</c>. It also has 4 timer slots and 8 history slots where 64
    /// has 2 and 2, so every unused slot must carry its sentinel or it decodes as a live entry.
    /// </summary>
    [Fact]
    public unsafe void HsmSnapshot_DecodeEventQueueTimerSlotsHistorySlots_From128ByteSlot()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var childSid = new Guid("ee000000-0000-0000-0000-000000000007");
        var assetId  = new Guid("ff000000-0000-0000-0000-000000000001");

        var inst  = new HsmInstance128();
        inst.Header.Phase = InstancePhase.Activity;
        // ⚠ 4 leaf slots, all empty — a default 0 decodes as leaf id 0, not as "absent".
        for (int i = 0; i < 4; i++) inst.ActiveLeafIds[i] = 0xFFFF;

        // One event, in the RING (not the interrupt slot): EventBuffer + 24.
        inst.InterruptSlotUsed = 0;
        inst.EventCount = 1;
        var ev = new HsmEvent { EventId = 99, Priority = EventPriority.Normal };
        *(HsmEvent*)(inst.EventBuffer + 24) = ev;

        // One timer slot active; the other three stay 0 == inactive.
        inst.TimerDeadlines[0] = 150u;

        // One history slot with recorded child (flat index 7); the other seven empty.
        inst.HistorySlots[0] = 7;
        for (int i = 1; i < 8; i++) inst.HistorySlots[i] = 0xFFFF;

        PlaceInstance(world, entity, inst);

        var metadata = new MachineMetadata();
        metadata.StateStableIds[7] = childSid;

        var sut = new HsmDebugSession();
        sut.SetMetadata(assetId, metadata);
        sut.Update(world, entity);

        var snap = sut.GetCurrentStateSnapshot();
        snap.Should().NotBeNull();

        snap!.EventQueue.Should().HaveCount(1);
        snap.EventQueue[0].EventId.Should().Be(99);
        snap.EventQueue[0].QueuePosition.Should().Be(0);

        snap.TimerSlots.Should().HaveCount(1);
        snap.TimerSlots[0].SlotIndex.Should().Be(0);
        snap.TimerSlots[0].RemainingTicks.Should().Be(150f);

        snap.HistorySlots.Should().HaveCount(1);
        snap.HistorySlots[0].SlotIndex.Should().Be(0);
        snap.HistorySlots[0].RecordedChildStableId.Should().Be(childSid);
    }

    // ---- O7c-④d: the OTHER two tiers, decodable for the first time ---------

    /// <summary>
    /// ⭐⭐⭐ <b><c>O7_R50</c> — A 64-BYTE MACHINE DECODES AS 64, NOT AS 128.</b>
    ///
    /// <para>🔴 <b>This rail could not have been written before ④d.</b> The snapshot read
    /// <c>BrainHsm128</c>, so the only instance the session could ever see was 128 bytes wide — the
    /// component WAS the width. ⇒ "decodes the right tier" was not a property anything could fail.
    /// ⭐ The slot makes the width a runtime value, and that is what turns it into a testable claim.
    /// 📄 §31.19.</para>
    ///
    /// <para>⚠ <b>The two tiers genuinely differ, which is why this is not a type swap.</b>
    /// <c>HsmInstance64</c> has <b>2</b> leaf slots, <b>2</b> timers, <b>2</b> history slots and ONE
    /// shared event slot at <c>EventBuffer[0]</c> with NO interrupt reservation.
    /// <c>HsmInstance128</c> has 4 / 4 / 8 and an interrupt slot followed by a ring from
    /// <c>[24]</c>. ⛔ Decoding a 64-byte instance with the 128 arm reads 64 bytes PAST the end of
    /// its slot — into whatever occurrence was attached next.</para>
    /// </summary>
    [Fact]
    public unsafe void O7_R50_A64ByteMachineIsDecodedOnThe64Arm_O7c4d()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var childSid = new Guid("11000000-0000-0000-0000-000000000003");
        var inst = new HsmInstance64();
        inst.Header.Phase = InstancePhase.Activity;
        inst.ActiveLeafIds[0] = 4;
        inst.ActiveLeafIds[1] = 0xFFFF;

        // ONE event, in the SINGLE SHARED SLOT at EventBuffer[0] — the 64 tier has no interrupt slot.
        inst.EventCount = 1;
        *(HsmEvent*)inst.EventBuffer = new HsmEvent { EventId = 77, Priority = EventPriority.Normal };

        inst.TimerDeadlines[0] = 42u;
        inst.HistorySlots[0] = 3;
        inst.HistorySlots[1] = 0xFFFF;

        PlaceInstance64(world, entity, inst);

        var metadata = new MachineMetadata();
        metadata.StateStableIds[4] = new Guid("11000000-0000-0000-0000-000000000004");
        metadata.StateStableIds[3] = childSid;

        var sut = new HsmDebugSession();
        sut.SetMetadata(Guid.NewGuid(), metadata);
        sut.Update(world, entity);

        var snap = sut.GetCurrentStateSnapshot();
        snap.Should().NotBeNull();

        // ⭐ ONE leaf, not two — the 128 arm would have read four slots, and slots 2/3 of a 64-byte
        //   instance are somebody else's bytes.
        snap!.ActiveLeafStableIds.Should().HaveCount(1);

        // ⭐⭐ THE DECIDING ASSERTION: the event is at EventBuffer[0]. The 128 arm reads the ring at
        //   EventBuffer + 24 and would report EventId 0, or nothing at all.
        snap.EventQueue.Should().HaveCount(1);
        snap.EventQueue[0].EventId.Should().Be(77);

        snap.TimerSlots.Should().HaveCount(1);
        snap.TimerSlots[0].RemainingTicks.Should().Be(42f);

        snap.HistorySlots.Should().HaveCount(1);
        snap.HistorySlots[0].RecordedChildStableId.Should().Be(childSid);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>O7_R51</c> — A 256-BYTE MACHINE SHOWS ALL EIGHT REGIONS.</b>
    ///
    /// <para>⭐ The other end of the same argument. A machine with more than four orthogonal regions
    /// was <b>unrepresentable</b> before <c>O7c</c>: it needed a <c>BrainHsm256</c> component, a new
    /// <c>GlobalComponentIds</c> entry and a new tick registration (§9.4 rules that out explicitly).
    /// ⇒ the decoder arm this exercises had no way to be reached, which is exactly why it did not
    /// exist until this slice wrote it.</para>
    ///
    /// <para>⚠ Eight leaves, 8 timers, 16 history slots and a ring of 5 after the interrupt slot —
    /// every count differs from the 128 tier's.</para>
    /// </summary>
    [Fact]
    public unsafe void O7_R51_A256ByteMachineDecodesAllEightRegions_O7c4d()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var inst = new HsmInstance256();
        inst.Header.Phase = InstancePhase.Activity;
        for (int r = 0; r < 8; r++) inst.ActiveLeafIds[r] = (ushort)(10 + r);

        // The interrupt slot AND two ring entries — a shape the 128 arm cannot produce.
        inst.InterruptSlotUsed = 1;
        inst.EventCount = 2;
        *(HsmEvent*)inst.EventBuffer = new HsmEvent { EventId = 500, Priority = EventPriority.Interrupt };
        *(HsmEvent*)(inst.EventBuffer + 24) = new HsmEvent { EventId = 501 };
        *(HsmEvent*)(inst.EventBuffer + 24 + sizeof(HsmEvent)) = new HsmEvent { EventId = 502 };

        // History slot 15 exists only on this tier — the 128 arm stops at 8.
        for (int i = 0; i < 16; i++) inst.HistorySlots[i] = 0xFFFF;
        inst.HistorySlots[15] = 10;

        PlaceInstance256(world, entity, inst);

        var metadata = new MachineMetadata();
        for (int r = 0; r < 8; r++)
            metadata.StateStableIds[(ushort)(10 + r)] =
                new Guid($"22000000-0000-0000-0000-0000000000{10 + r:X2}");

        var sut = new HsmDebugSession();
        sut.SetMetadata(Guid.NewGuid(), metadata);
        sut.Update(world, entity);

        var snap = sut.GetCurrentStateSnapshot();
        snap.Should().NotBeNull();

        // ⭐⭐ EIGHT — the number the 128-only decoder could never report.
        snap!.ActiveLeafStableIds.Should().HaveCount(8);

        snap.EventQueue.Should().HaveCount(3);
        snap.EventQueue[0].EventId.Should().Be(500);   // the interrupt slot comes first
        snap.EventQueue[1].EventId.Should().Be(501);
        snap.EventQueue[2].EventId.Should().Be(502);

        // ⭐ Slot 15 — beyond the 128 tier's eight history slots.
        snap.HistorySlots.Should().HaveCount(1);
        snap.HistorySlots[0].SlotIndex.Should().Be(15);
    }
}

file sealed class SpyCoordinator : AiTracerCoordinator
{
    public bool PauseRequested;
    public override void RequestStepOneTick() { }
    public override void RequestPause()       => PauseRequested = true;
    public override void RequestContinue()    { }
}
