using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// Slice 2a-3 real-interpreter proof: a <b>standalone</b> Entity-scoped Role=State blackboard
/// variable ("rally", typed <see cref="SquadRallyState"/>) — declared on a host BTree but NOT bound
/// to any composed node's <c>WorkingStateTargetField</c> — gets a <see cref="StatefulSlotInfo"/>
/// manifest entry via <see cref="BTreeBridgeEmitCore.EmitStatefulWorkingSlotsArray"/>'s standalone-
/// variable pass, and <see cref="BehaviorIngressSystem.Execute"/> provisions that slot from the
/// manifest on <see cref="AssignBehaviorEvent"/> — no direct
/// <see cref="BlueprintBlackboardPartitions.TryAttach"/> call anywhere in this test (contrast
/// <see cref="T36_SharedStateGetSet_ProofTests"/>, which provisions directly because it has no host
/// BTree at all).
///
/// <para>
/// The host tree <c>T37_SharedStateManifestProvisioning</c> (committed .btree.json) composes the
/// REAL generated <c>SharedStateRallyDemo</c> blueprint (Assets/Blueprints/SharedStateRallyDemo.bp.json,
/// same demo T36 exercises) as a single Action node — mirroring T32's composed-generated-blueprint
/// pattern — and separately declares the standalone "rally" variable on its own Blackboard. The
/// composed node's generated <c>TickCore</c> body reads/writes "rally" purely through the Slice 2a-2
/// <c>GetShared</c>/<c>SetShared</c> blueprint-graph nodes (<see cref="Fdp.Toolkit.Blueprints.Partitioning.BlueprintSharedState"/>),
/// which is Entity-scoped and therefore independent of this node's own (empty, Node-scoped)
/// Params/WorkingState.
/// </para>
///
/// <para>
/// Ticking the real FastBTree <see cref="Interpreter{TBB,TContext}"/> built from
/// <c>T37_SharedStateManifestProvisioning.Registrar.g.cs</c> five times accumulates
/// <c>RallyCount</c> 1..5 in the manifest-provisioned "rally" slot — proving the whole chain: JSON
/// authoring → <c>EmitStatefulWorkingSlotsArray</c> standalone-variable pass → generated
/// <c>StatefulWorkingSlots</c> array → <c>BehaviorIngressSystem.ProvisionStatefulSlots</c> →
/// <c>BlueprintSharedState.TryGetShared</c>/<c>TrySetShared</c> inside the real generated
/// <c>TickCore</c>.
/// </para>
/// </summary>
public sealed class T37_SharedStateManifestProvisioning_ProofTests : IDisposable
{
    private readonly BehaviorRegistry  _registry  = new();
    private readonly BlueprintRegistry _blueprint = new();

    public void Dispose() => _registry.Clear();

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BehaviorState>();
        world.RegisterComponent<BrainBlackboard>();
        world.RegisterComponent<BrainBTreeState>();
        // ⭐ B4: register from the LADDER, not a hand-list. ⛔ This was three explicit
        //   RegisterComponent calls and it did NOT know about the 256 tier — 11 tests
        //   failed with "Component BlueprintBlackboard256 is not registered" the moment
        //   O3b added one. Production never had the bug: it registers from the table.
        BlueprintTierTable.RegisterAll(world);
        return world;
    }

    /// <summary>
    /// FNV-1a-32("rally") — Entity scope excludes assetId/nodeVisualId per
    /// <see cref="BTreeBridgeEmitCore.ComputeStatefulSlotKey(Guid, Hrot.AiEditor.Persistence.WorkingStateScope, Guid, string)"/>,
    /// so this is the SAME key the emitted manifest entry carries and the SAME key
    /// <see cref="BlueprintSharedState"/> computes at read/write time.
    /// </summary>
    private static int RallySlotKey => BTreeBridgeEmitCore.ComputeStatefulSlotKey(
        Guid.Empty, Hrot.AiEditor.Persistence.WorkingStateScope.Entity, Guid.Empty, "rally");

    private static uint ExpectedHash<T>() where T : unmanaged
        => unchecked(StatefulBTreeActionBinder.ComputeTypeNameHash(typeof(T).FullName ?? string.Empty)
                      ^ (uint)Marshal.SizeOf<T>());

    private BehaviorDefinition RegisterAndGetDefinition()
    {
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(typeof(DemoAiPrimitiveNodes).Assembly, staging, _registry);
        _blueprint.CommitStaging(staging);

        _registry.TryGetId("T37_SharedStateManifestProvisioning", out int id)
            .Should().BeTrue("T37 must self-register via its generated [BlueprintRegistrar]");
        _registry.TryGetDefinition(id, out var def).Should().BeTrue();
        return def;
    }

    [Fact]
    public void StandaloneRallyVariable_EmitsManifestEntry_MatchingBlueprintSharedStateExpectedHash()
    {
        var def = RegisterAndGetDefinition();

        def.StatefulWorkingSlots.Should().NotBeNull(
            "the standalone 'rally' variable alone must be enough to trigger StatefulWorkingSlots emission");

        var rallyEntry = def.StatefulWorkingSlots!.Should()
            .ContainSingle(s => s.SlotKey == RallySlotKey,
                "the standalone-variable pass in EmitStatefulWorkingSlotsArray must emit exactly one " +
                "entry for 'rally' even though no node binds WorkingStateTargetField to it")
            .Subject;

        rallyEntry.PayloadSize.Should().Be(Marshal.SizeOf<SquadRallyState>());
        rallyEntry.StructureHash.Should().Be(ExpectedHash<SquadRallyState>(),
            "the manifest's StructureHash formula (ComputeTypeNameHash(typeId) ^ Marshal.SizeOf<T>()) must " +
            "match BlueprintSharedState.TryGetShared/TrySetShared's expected-hash guard bit-for-bit, or " +
            "GetShared/SetShared would treat the manifest-provisioned slot as drifted and always return false");
        rallyEntry.WorkingStateType.Should().Be(typeof(SquadRallyState));
        rallyEntry.NodeLabel.Should().Be("rally");
        rallyEntry.Role.Should().Be(1, "BlackboardVariableRole.State == 1");
        rallyEntry.Scope.Should().Be(2, "WorkingStateScope.Entity == 2");
    }

    [Fact]
    public void RallySlot_ProvisionedFromManifest_ThenGetSharedSetShared_AccumulatesRallyCount_AcrossRealTicks()
    {
        var def = RegisterAndGetDefinition();
        var interpreter = def.BTreeInterpreter!;
        interpreter.Should().NotBeNull("T37 is a BTree behavior");

        var world  = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        // Assign -> BehaviorIngressSystem reads def.StatefulWorkingSlots and provisions BOTH the
        // composed node's own (Node-scoped, empty) slot AND the standalone Entity-scoped "rally"
        // slot -- no direct BlueprintBlackboardPartitions.TryAttach call anywhere in this test.
        var ingress = new BehaviorIngressSystem(_registry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = "T37_SharedStateManifestProvisioning",
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);

        (OccurrenceStoreAccess.HasStore(world, entity))
            .Should().BeTrue("BehaviorIngressSystem must provision a partition tier for the manifest's slots");

        RallySlotIsAttached(world, entity).Should().BeTrue(
            "ProvisionStatefulSlots must have attached the 'rally' Entity-scoped slot FROM THE MANIFEST " +
            "before the first tick — proving the standalone variable's manifest entry is what provisions it");

        var ctx = new BTreeContext { Self = entity, World = world };
        NodeStatus Tick()
        {
            ref byte bb = ref global::Fdp.Toolkit.Behavior.RootParamsAccess.RootRef(world, entity);   // P4-②: the ROOT PARAMS SLOT base, exactly as BTreeTickSystem hands it to the interpreter
            var state  = new BehaviorTreeState();
            return interpreter.Tick(ref bb, ref state, ref ctx);
        }

        // Root -> single composed Action node whose blueprint graph is EventEntry -> GetShared("rally")
        // -> Increment -> SetShared("rally") -> Return(Success) -- unconditionally Success every tick.
        for (int tick = 1; tick <= 5; tick++)
        {
            Tick().Should().Be(NodeStatus.Success,
                $"tick {tick}: the composed SharedStateRallyDemo node's Return(Success) is unconditional");

            ReadRallyCount(world, entity).Should().Be(tick,
                $"tick {tick}: GetShared must read what the PREVIOUS tick's SetShared wrote into the SAME " +
                "manifest-provisioned Entity-scoped slot -- proving the manifest path (not direct TryAttach) " +
                "is what the real generated TickCore's GetShared/SetShared round-trip through.");
        }

        world.Dispose();
    }

    private static unsafe bool RallySlotIsAttached(EntityRepository world, Entity entity)
    {
        int slotKey = RallySlotKey;
        // ⭐ B4: was THREE arms over the tier trio and knew nothing about the 256 tier.
        //   OccurrenceStoreAccess is the seam production uses for exactly this.
        byte* mem = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        return mem != null && BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out _);
    }

    private static unsafe int ReadRallyCount(EntityRepository world, Entity entity)
    {
        int slotKey = RallySlotKey;
        // ⭐ B4: was THREE arms over the tier trio and knew nothing about the 256 tier.
        //   OccurrenceStoreAccess is the seam production uses for exactly this.
        byte* mem = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        if (mem != null)
        {
            BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off).Should().BeTrue();
            return Unsafe.AsRef<SquadRallyState>(mem + off).RallyCount;
        }
        throw new InvalidOperationException("entity has no BlueprintBlackboard* tier — slot cannot be read");
    }
}
