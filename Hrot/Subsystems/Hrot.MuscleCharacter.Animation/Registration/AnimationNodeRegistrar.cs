using System;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Fdp.Toolkit.Behavior;
using Hrot.MuscleCharacter.Animation.Nodes;

namespace Hrot.MuscleCharacter.Animation.Registration;

/// <summary>
/// Registrar for animation nodes as AiPrimitives enabling cross-subsystem reuse.
/// Registers all 11 Phase 5 animation nodes (9 action + 2 getter) for dispatch in:
/// - BTree context (via BTreeEvaluator)
/// - HSM context (via HsmActionExecutor)  
/// - Blueprint context (via BlueprintPrimitiveDispatcher)
/// Same node instance works in all 3 contexts.
/// (ANC-P5-07, DD-5 §11)
/// </summary>
[BlueprintRegistrar]
public static class AnimationNodeRegistrar
{
    // AiPrimitive ID range for animation nodes: 5001-5011
    // Stable IDs that must never change once published.
    private const int PlayMontage_AiId = 5001;
    private const int StopMontage_AiId = 5002;
    private const int EnqueueMontage_AiId = 5003;
    private const int ClearMontageQueue_AiId = 5004;
    private const int PlayMontageChain_AiId = 5005;
    private const int SetStance_AiId = 5006;
    private const int LookAtPoint_AiId = 5007;
    private const int LookAtEntity_AiId = 5008;
    private const int ReleaseLook_AiId = 5009;
    private const int GetMontageQueueProgress_AiId = 5010;
    private const int GetCurrentStance_AiId = 5011;

    /// <summary>
    /// Entry point for BlueprintRegistry hot-reload system.
    /// Registers all 11 animation nodes as AiPrimitives.
    /// </summary>
    public static void Register(BlueprintRegistryStaging staging, BehaviorRegistry behaviorReg)
    {
        // Register PlayMontageNode
        staging.Add(PlayMontage_AiId, new BlueprintDefinition
        {
            Name = "PlayMontageNode",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = ComputeStructureHash(typeof(PlayMontageNode)),
            StateSize = 0,
        });

        // Register StopMontageNode
        staging.Add(StopMontage_AiId, new BlueprintDefinition
        {
            Name = "StopMontageNode",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = ComputeStructureHash(typeof(StopMontageNode)),
            StateSize = 0,
        });

        // Register EnqueueMontageNode
        staging.Add(EnqueueMontage_AiId, new BlueprintDefinition
        {
            Name = "EnqueueMontageNode",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = ComputeStructureHash(typeof(EnqueueMontageNode)),
            StateSize = 0,
        });

        // Register ClearMontageQueueNode
        staging.Add(ClearMontageQueue_AiId, new BlueprintDefinition
        {
            Name = "ClearMontageQueueNode",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = ComputeStructureHash(typeof(ClearMontageQueueNode)),
            StateSize = 0,
        });

        // Register PlayMontageChainNode
        staging.Add(PlayMontageChain_AiId, new BlueprintDefinition
        {
            Name = "PlayMontageChainNode",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = ComputeStructureHash(typeof(PlayMontageChainNode)),
            StateSize = 0,
        });

        // Register SetStanceNode
        staging.Add(SetStance_AiId, new BlueprintDefinition
        {
            Name = "SetStanceNode",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = ComputeStructureHash(typeof(SetStanceNode)),
            StateSize = 0,
        });

        // Register LookAtPointNode
        staging.Add(LookAtPoint_AiId, new BlueprintDefinition
        {
            Name = "LookAtPointNode",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = ComputeStructureHash(typeof(LookAtPointNode)),
            StateSize = 0,
        });

        // Register LookAtEntityNode
        staging.Add(LookAtEntity_AiId, new BlueprintDefinition
        {
            Name = "LookAtEntityNode",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = ComputeStructureHash(typeof(LookAtEntityNode)),
            StateSize = 0,
        });

        // Register ReleaseLookNode
        staging.Add(ReleaseLook_AiId, new BlueprintDefinition
        {
            Name = "ReleaseLookNode",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = ComputeStructureHash(typeof(ReleaseLookNode)),
            StateSize = 0,
        });

        // Register GetMontageQueueProgressNode (getter)
        staging.Add(GetMontageQueueProgress_AiId, new BlueprintDefinition
        {
            Name = "GetMontageQueueProgressNode",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = ComputeStructureHash(typeof(GetMontageQueueProgressNode)),
            StateSize = 0,
        });

        // Register GetCurrentStanceNode (getter)
        staging.Add(GetCurrentStance_AiId, new BlueprintDefinition
        {
            Name = "GetCurrentStanceNode",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = ComputeStructureHash(typeof(GetCurrentStanceNode)),
            StateSize = 0,
        });
    }

    /// <summary>
    /// Compute a simple hash of the struct type for change detection.
    /// For now, uses sizeof as a basic hash; full versioning can be added later.
    /// </summary>
    private static ulong ComputeStructureHash(Type nodeType)
    {
        // Simple hash based on type name and size
        // In production, would use full field introspection for robustness
        unchecked
        {
            ulong hashCode = (ulong)(nodeType.FullName?.GetHashCode() ?? 0);
            hashCode = (hashCode << 32) | (uint)System.Runtime.InteropServices.Marshal.SizeOf(nodeType);
            return hashCode;
        }
    }
}
