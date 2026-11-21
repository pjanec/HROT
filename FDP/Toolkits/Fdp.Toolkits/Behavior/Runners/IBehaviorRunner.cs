using Fbt;
using Fdp.Core;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Behavior.Runners
{
    /// <summary>
    /// ⭐⭐⭐ <b>S4 (<c>DESIGN_Unified_Behaviour_Run</c> §3, decision U-2) — ONE way to run a behaviour, per tier.</b>
    ///
    /// <para>⭐ A behaviour run is a pair of slots — the BRAIN STATE (a tree cursor, an HSM instance, a blueprint's
    /// <c>Exec</c>) and the BLOCK (its blackboard, <c>{In; St}</c>) — stepped by its tier's kernel. The runner owns
    /// everything tier-specific (which kernel, how the brain is located, pause, trace, how terminality is read);
    /// <c>BrainTickSystem</c> owns what every tier shares (the walk, the block and its reload restart, finish, fault).</para>
    ///
    /// <para>⭐ Runners are stateless singletons (<see cref="BehaviorRunners"/>) ⇒ nothing allocated per tick, and nothing
    /// managed to record: all run state lives in the two slots.</para>
    /// </summary>
    public unsafe interface IBehaviorRunner
    {
        /// <summary>
        /// Locate the ROOT brain state of <paramref name="self"/>. <c>false</c> = nothing to tick this frame — either
        /// the definition cannot run (a <see cref="Events.BehaviorFault"/> is raised, and the caller finishes the run)
        /// or the tier legitimately has no instance yet (HSM, §31.16.2).
        /// <para>⛔ A tier whose brain MUST exist throws instead of skipping (a stand-in cursor would restart every frame).</para>
        /// </summary>
        bool TryGetRootBrain(EntityRepository world, Entity self, BehaviorDefinition def,
                             out byte* brain, out int brainBytes);

        /// <summary>
        /// Step the run one frame. <paramref name="brain"/> may be null when the tier declares no brain bytes;
        /// <paramref name="block"/> is <see cref="BehaviorBlock.None"/> when the behaviour has no block.
        /// Returns <c>Running</c>, or the terminal status.
        /// </summary>
        NodeStatus Tick(ref BehaviorRunContext ctx, byte* brain, int brainBytes, ref byte block);

        /// <summary>⭐ S5a — how many brain-state bytes a run of <paramref name="def"/> needs (its hosted slot's first region).</summary>
        int BrainBytes(BehaviorDefinition def);

        /// <summary>
        /// ⭐ S5a — make <paramref name="brain"/> a FRESH run of <paramref name="def"/>: zeroed, and for an HSM initialised from its
        /// blob (which stamps the <c>MachineId</c> the kernel validates). Called by a host at every START of a hosted child; the
        /// root's equivalent is ingress's reset.
        /// </summary>
        void Start(BehaviorDefinition def, byte* brain, int brainBytes);

        /// <summary>
        /// ⭐ <c>CE-2116</c> — the host ABANDONS a still-running hosted child: release what its running steps hold (a BTree runs
        /// its active path's deactivators) BEFORE the host zeroes the brain. Default: nothing to release.
        /// </summary>
        void Abort(ref BehaviorRunContext ctx, byte* brain, int brainBytes, ref byte block) { }
    }

    /// <summary>⭐ S4 — what a runner is handed each tick. A stack value: no allocation.</summary>
    public ref struct BehaviorRunContext
    {
        public EntityRepository World;
        public Entity Self;
        public BehaviorDefinition Definition;
        /// <summary>The run's <c>BehaviorState.InstanceId</c> — a hosted child shares its host's (U-10).</summary>
        public uint InstanceId;
        /// <summary>⭐ S5b — the run's occurrence key: 0 at the root, its hosted slot's actual key when hosted.</summary>
        public int OccurrenceKey;
        public IEntityCommandBuffer? Ecb;
        public float DeltaTime;
    }

    /// <summary>⭐ S4 — the runner for each brain tier. ⭐ ONE answer, used by the root walk (and by hosting, S5).</summary>
    public static class BehaviorRunners
    {
        public static IBehaviorRunner? For(int brainTier) => brainTier switch
        {
            BehaviorConstants.BrainTierBTree     => BTreeRunner.Instance,
            BehaviorConstants.BrainTierHsm       => HsmRunner.Instance,
            BehaviorConstants.BrainTierBlueprint => BlueprintRunner.Instance,
            _ => null,
        };
    }
}
