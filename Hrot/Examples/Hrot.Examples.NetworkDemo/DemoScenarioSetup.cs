using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Hrot.Common.Infrastructure;
using Hrot.Map.Common;

namespace Hrot.Examples.NetworkDemo;

/// <summary>
/// Composition-time setup for the SDK Onboarding "Patrol and Engage" demo scenario.
///
/// <para>
/// All methods are stateless. Doctrine registration uses data-driven DoctrineRegistry
/// entries; entity spawning publishes SpawnEntityCommand onto the event bus so the
/// kernel's NetworkSpawningSystem handles the ECS work.
/// </para>
///
/// <para>
/// In a production integration the Brain node would receive a MissionControlRequest
/// over DDS (from the ExCon subsystem) to activate a doctrine on the spawned entity.
/// This demo shows the direct registration path for self-contained SDK evaluation.
/// </para>
/// </summary>
public static class DemoScenarioSetup
{
    // Doctrine identifier for the "PatrolAndEngage" behavior tree.
    public const int PatrolAndEngageDoctrineId = 1001;

    /// <summary>
    /// Registers demo doctrines into the shared DoctrineRegistry.
    ///
    /// <para>
    /// In a full deployment, doctrine BTree blobs are compiled from JSON via
    /// TreeCompiler.CompileFromJson and wired with typed ActionRegistry delegates
    /// (e.g. Condition_HasTarget, Action_AimAndFire, Action_Wander from CgfNodes).
    /// The registry is then forwarded to CgfLogicPack for use by CognitiveRuntimeModule.
    /// </para>
    /// </summary>
    public static void RegisterDoctrines(DoctrineRegistry registry)
    {
        // No pre-registered doctrines for this standalone demo.
        // Entities receive their doctrine assignment via MissionControlRequest (DDS)
        // from the ExCon subsystem at runtime.
        //
        // To register a BTree doctrine directly, use:
        //   var blob = TreeCompiler.CompileFromJson(PatrolAndEngageJson);
        //   var interp = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg);
        //   registry.Register(PatrolAndEngageDoctrineId, "PatrolAndEngage",
        //       new DoctrineDefinition { BTreeInterpreter = interp, ... });
    }

    /// <summary>
    /// Spawns the demo patrol vehicle from the Brain node.
    ///
    /// <para>
    /// The NedReplicationModule routes the WorldPos and NavigationStatus descriptors
    /// to the first available MuscleGround node via the DeferredTakeOwnership protocol.
    /// After propagation, the Brain retains Health and NavigationIntent authority while
    /// the Muscle gains SimTransform authority.
    /// </para>
    /// </summary>
    public static void SpawnEntities(HrotNodeContext context)
    {
        long networkId = context.IdAllocator?.AllocateId()
            ?? throw new InvalidOperationException(
                "IdAllocator is required on the Brain node for entity spawning. " +
                "Ensure HrotNodeBuilder.WithNetworkFactory(factory) was called with " +
                "a non-null DdsParticipant before Build().");

        context.World.Bus.PublishManaged(new SpawnEntityCommand
        {
            NetworkId   = networkId,
            TkbType     = TkbEntityTypes.Tank_M1Abrams,
            OwnerNodeId = context.NodeId,
            InitType    = ReliableInitType.AllPeers,
            RequestId   = Guid.Empty,
        });

        Console.WriteLine($"[NetworkDemo] Brain spawned patrol entity networkId={networkId} " +
                          $"(tkbType={TkbEntityTypes.Tank_M1Abrams})");
    }
}
