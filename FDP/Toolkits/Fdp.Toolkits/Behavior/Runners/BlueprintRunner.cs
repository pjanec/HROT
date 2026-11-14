using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;

namespace Fdp.Toolkit.Behavior.Runners
{
    /// <summary>
    /// ⭐⭐ <b>S4 — the blueprint-behaviour runner</b> (<c>CE-446</c>, <c>Architect_Question_77</c>). The brain state is the
    /// generated <c>Exec</c> (latent cursor, When memory, suspended locals) in the root STATE slot, <c>BrainStateBytes</c>
    /// wide (S2, U-1); the kernel is the generated <c>BehaviorTick</c>; terminality is its returned status (<c>Q33</c>
    /// ruling 2: latent ≠ ended). ⭐ Moved verbatim from <c>BrainTickSystem</c>'s former blueprint arm.
    /// </summary>
    public sealed unsafe class BlueprintRunner : IBehaviorRunner
    {
        public static readonly BlueprintRunner Instance = new();
        private BlueprintRunner() { }

        public bool TryGetRootBrain(EntityRepository world, Entity self, BehaviorDefinition def,
                                    out byte* brain, out int brainBytes)
        {
            brain = null; brainBytes = 0;
            if (def.BlueprintTick == null)
            {
                // ⭐ CE-482: a definition that cannot run is a FAULT, not a silent skip.
                BehaviorFault.Raise(world, self, BehaviorFaultCode.NoDefinition,
                    $"Behavior '{def.Name}' has no blueprint tick.");
                return false;
            }

            // ⭐ S2 — RequireRootBytesRef throws (names the cause) rather than ticking a stand-in.
            if (def.BrainStateBytes > 0)
            {
                ref byte exec = ref RootStateAccess.RequireRootBytesRef(world, self, def.BrainStateBytes);
                brain = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref exec);
                brainBytes = def.BrainStateBytes;
            }
            return true;
        }

        public int BrainBytes(BehaviorDefinition def) => def.BrainStateBytes;

        public void Start(BehaviorDefinition def, byte* brain, int brainBytes)
        {
            if (brain != null) new System.Span<byte>(brain, brainBytes).Clear();
        }

        public NodeStatus Tick(ref BehaviorRunContext ctx, byte* brain, int brainBytes, ref byte block)
        {
            ref byte exec = ref brain != null
                ? ref System.Runtime.CompilerServices.Unsafe.AsRef<byte>(brain)
                : ref BehaviorBlock.None;
            var repo = ctx.World;
            return ctx.Definition.BlueprintTick!(ref block, ref exec, repo, ctx.Ecb!, ctx.Self,
                                                 repo.SimulationTime, ctx.DeltaTime, ctx.InstanceId, ctx.OccurrenceKey);
        }
    }
}
