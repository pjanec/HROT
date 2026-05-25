using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using FDP.Eqs;
using Fdp.Toolkit.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Parallel node policy constants used when building BTree Parallel nodes.
    /// </summary>
    internal static class Policy
    {
        /// <summary>Parallel succeeds when at least one child succeeds (selector-like).</summary>
        public const int RequireOne = 1;
        /// <summary>Parallel succeeds only when all children succeed (barrier-like).</summary>
        public const int RequireAll = 0;
    }

    /// <summary>
    /// Unmanaged blackboard memory for the <c>HideInCover_BT</c> behavior.
    /// Must use sequential layout for deterministic Blueprint offset generation.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HideInCoverBlackboard
    {
        /// <summary>EQS query parameters -- consumed by <c>EqsLifecycleNodes</c> actions.</summary>
        public EqsParams EqsConfig;

        /// <summary>Locomotion parameters -- consumed by <c>EqsCombatNodes</c> actions.</summary>
        public MoveToOptimalCoverParams MoveConfig;
    }

    /// <summary>
    /// Blackboard for the child-entity variant <c>HideInCover_BT_v2</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HideInCoverV2Blackboard
    {
        /// <summary>Spawn params including SensorConfig + ChildSlotIndex + SpawnedHandle (output).</summary>
        public EqsSpawnParams SpawnConfig;
        /// <summary>Locomotion params. SensorHandle set from SpawnConfig.SpawnedHandle at runtime.</summary>
        public MoveToOptimalCoverParams MoveConfig;
    }

    /// <summary>
    /// Fluent BTree definitions for high-level tactical behaviors.
    /// </summary>
    public static class TacticsNodes
    {
        /// <summary>
        /// HideInCover_BT -- agent seeks optimal cover when a threat is present.
        ///
        /// Tree structure:
        ///   ObserverSelector
        ///     [High] Sequence
        ///       Condition_HasTarget          (returns Failure if no live threat)
        ///       Parallel(RequireOne)
        ///         Action_MaintainEqsSensor   (resource owner -- always Running)
        ///         Sequence
        ///           Action_WaitForSensor     (polls buffer until IsReady)
        ///           Action_MoveToOptimalCover
        ///           Action_HoldPosition
        ///     [Low]
        ///       Action_Wander
        /// </summary>
        [BTreeDefinition("HideInCover_BT")]
        public static BTreeBuilder<HideInCoverBlackboard, BTreeContext> BuildHideInCoverTree()
        {
            return new BTreeBuilder<HideInCoverBlackboard, BTreeContext>()
                .ObserverSelector(obs => obs
                    .Sequence(seq => seq
                        .Condition(bb => bb.MoveConfig, EqsCombatNodes.Condition_HasTarget)
                        .Parallel(Policy.RequireOne, par => par
                            .Action(bb => bb.EqsConfig,  EqsLifecycleNodes.Action_MaintainEqsSensor)
                            .Sequence(tactics => tactics
                                .Action(bb => bb.EqsConfig,  EqsLifecycleNodes.Action_WaitForSensor)
                                .Action(bb => bb.MoveConfig, EqsCombatNodes.Action_MoveToOptimalCover)
                                .Action(bb => bb.MoveConfig, EqsCombatNodes.Action_HoldPosition)
                            )
                        )
                    )
                    .Action(bb => bb.MoveConfig, EqsCombatNodes.Action_Wander)
                );
        }

        /// <summary>
        /// Copies <c>SpawnConfig.SpawnedHandle</c> into <c>MoveConfig.SensorHandle</c> so
        /// <see cref="EqsCombatNodes.Action_MoveToOptimalCover"/> reads from the child sensor.
        /// Used only in <c>HideInCover_BT_v2</c>.
        /// </summary>
        public static NodeStatus BindSensorHandle(
            ref HideInCoverV2Blackboard bb,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int nodeIndex)
        {
            bb.MoveConfig.SensorHandle = bb.SpawnConfig.SpawnedHandle;
            return NodeStatus.Success;
        }

        /// <summary>
        /// HideInCover_BT_v2 -- child-entity variant of HideInCover_BT.
        ///
        /// Uses <see cref="EqsLifecycleNodes.Action_SpawnEqsSensorChild"/> to host the EQS
        /// query in a dedicated child entity rather than directly on the agent, enabling
        /// multiple concurrent sensors per agent.
        ///
        /// Tree structure:
        ///   ObserverSelector
        ///     [High] Sequence
        ///       Condition_HasTarget
        ///       Parallel(RequireOne)
        ///         Action_SpawnEqsSensorChild   (resource owner -- returns Success after spawn)
        ///         Sequence
        ///           Action_WaitForChildSensor  (polls child buffer until IsReady)
        ///           BindSensorHandle           (copies SpawnedHandle -> MoveConfig.SensorHandle)
        ///           Action_MoveToOptimalCover  (reads from child sensor via SensorHandle)
        ///           Action_HoldPosition
        ///     [Low]
        ///       Action_Wander
        /// </summary>
        [BTreeDefinition("HideInCover_BT_v2")]
        public static BTreeBuilder<HideInCoverV2Blackboard, BTreeContext> BuildHideInCoverV2Tree()
        {
            return new BTreeBuilder<HideInCoverV2Blackboard, BTreeContext>()
                .ObserverSelector(obs => obs
                    .Sequence(seq => seq
                        .Condition(bb => bb.MoveConfig, EqsCombatNodes.Condition_HasTarget)
                        .Parallel(Policy.RequireOne, par => par
                            .Action(bb => bb.SpawnConfig, EqsLifecycleNodes.Action_SpawnEqsSensorChild)
                            .Sequence(tactics => tactics
                                .Action(bb => bb.SpawnConfig, EqsLifecycleNodes.Action_WaitForChildSensor)
                                .Action(BindSensorHandle)
                                .Action(bb => bb.MoveConfig, EqsCombatNodes.Action_MoveToOptimalCover)
                                .Action(bb => bb.MoveConfig, EqsCombatNodes.Action_HoldPosition)
                            )
                        )
                    )
                    .Action(bb => bb.MoveConfig, EqsCombatNodes.Action_Wander)
                );
        }
    }
}
