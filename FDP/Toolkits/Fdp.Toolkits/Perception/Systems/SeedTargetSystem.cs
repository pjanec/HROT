using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// ⭐ B-5 S3 (R-4137; 📄 docs/UX/DESIGN_Initial_State.md §3a) — applies <see cref="SeedTargetCommand"/>: a manually nominated target goes into
    /// the perceiver's <see cref="TargetMemory"/>. ONE applier on EVERY node (it is in the <c>Interactions</c> capability), gated by the
    /// entity — the node that owns the perceiver's <see cref="TargetMemory"/> (its brain) applies it; a station that does not own it sends the
    /// command across as an interaction (<c>InteractionCodecs.SeedTarget</c>) and applies nothing. Was the editor's private
    /// <c>EditorPerceptionSetupSystem</c> (retired): a seed sent from any other station was lost.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed unsafe class SeedTargetSystem : IEcsModuleSystem
    {
        /// <summary>Does THIS node own <paramref name="perceiver"/>'s <see cref="TargetMemory"/> (its brain's node)? The entity decides, not the node
        /// (R-4119): <c>HasAuthority&lt;TargetMemory&gt;</c>; an entity with no <see cref="NetworkAuthority"/> (one node, a headless harness) is owned here.
        /// ⭐ The ONE test — the system applies where it is true, the <c>SeedTarget</c> interaction codec sends where it is false.</summary>
        public static bool Owns(EntityRepository repo, Entity perceiver)
            => repo.IsAlive(perceiver) && repo.IsComponentTypeRegistered<TargetMemory>() && repo.HasComponent<TargetMemory>(perceiver)
               && (!(repo.IsComponentTypeRegistered<NetworkAuthority>() && repo.HasComponent<NetworkAuthority>(perceiver)) || repo.HasAuthority<TargetMemory>(perceiver));

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            if (view is not EntityRepository repo || !repo.Bus.IsRegistered<SeedTargetCommand>()) return;
            var cmds = repo.ReadEvents<SeedTargetCommand>();
            for (int i = 0; i < cmds.Length; i++)
            {
                ref readonly var cmd = ref cmds[i];
                if (!repo.IsAlive(cmd.Perceiver)) continue;

                bool hasMemory = repo.IsComponentTypeRegistered<TargetMemory>() && repo.HasComponent<TargetMemory>(cmd.Perceiver);
                // a purely local entity (no NetworkAuthority) that remembers nothing: nobody else could answer — say so here
                if (!hasMemory && !(repo.IsComponentTypeRegistered<NetworkAuthority>() && repo.HasComponent<NetworkAuthority>(cmd.Perceiver)))
                {
                    InteractionOutcomes.Refused(repo, cmd.RequestId, cmd.Perceiver, "it is not a unit that remembers targets");
                    continue;
                }
                // not this node's to apply: the perceiver's brain lives elsewhere (the egress already sent the command there, where it is answered)
                if (!hasMemory || !Owns(repo, cmd.Perceiver)) continue;

                if (!repo.IsAlive(cmd.Target) || !repo.HasComponent<SimTransform>(cmd.Target))
                {
                    InteractionOutcomes.Refused(repo, cmd.RequestId, cmd.Perceiver, "the target is gone, or has no position");
                    continue;
                }

                ref var mem = ref repo.GetComponentRW<TargetMemory>(cmd.Perceiver);
                ref readonly var xfm = ref repo.GetComponentRO<SimTransform>(cmd.Target);
                uint tick = repo.HasSingleton<GlobalTime>() ? (uint)repo.GetSingletonUnmanaged<GlobalTime>().FrameNumber : 0u;

                TargetMemory.AddOrUpdateTarget(ref mem, (long)cmd.Target.PackedValue, xfm.Position.X, xfm.Position.Y, cmd.ScoreBoost, tick);
                InteractionOutcomes.Applied(repo, cmd.RequestId, cmd.Perceiver);
            }
        }
    }
}
