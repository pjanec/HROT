using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // ⭐ CE-3123 (R-228) / CE-1022: a [GizmoProjector] now, on every host. The registrar passes the host's BehaviorRegistry
    //   (MapInteractionContext.Services) when it has one; without one the parameterless constructor is used and the behaviour
    //   line is left out — has data = can draw. 📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
    // ⭐ CE-1033 S5b (docs/DESIGN_Map_3D_Mode.md §3.6, M14): its lines are ROWS OF THE ENTITY CARD now, on the Labels layer, the
    //   same in 2-D and 3-D — it used to stack three loose world texts east of the entity. The id moved into the card's name row
    //   (EntityNameGizmo: "name #id"); the hit points sit under the bar as numbers (row 30), the behaviour below them (row 40).
    [GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity))]
    public sealed class EntityEditorLabelGizmo : IStatelessGizmo
    {
        /// <summary>The card rows (§3.6): the bar is 10, the name 20; ⛔ row 30 (the hit points as numbers) is retired — CE-1327.</summary>
        public const byte BehaviourRow = 40;
        private static readonly Rgba32 BehaviorColor = new Rgba32(255, 255,   0, 255);

        private readonly BehaviorRegistry? _behaviorRegistry;

        /// <summary>No behaviour names on this host: this gizmo adds nothing to the card.</summary>
        public EntityEditorLabelGizmo() { }

        public EntityEditorLabelGizmo(BehaviorRegistry behaviorRegistry)
        {
            _behaviorRegistry = behaviorRegistry ?? throw new ArgumentNullException(nameof(behaviorRegistry));
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<SimTransform>(entity))      return;
            if (!view.HasComponent<NetworkIdentity>(entity))   return;
            if (Fdp.Toolkit.Perception.PhysicalPresence.IsEnclosedPassenger(view, entity)) return;   // ⭐ CE-1058: no card for an unseen passenger
            if (Hrot.Common.Diagnostics.Gizmos.NotAUnit.Is(view, entity)) return;   // ⭐ CE-1165: no label for a cloud, a round, the camera

            long net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0) return;   // a card is keyed by the network id
            var card = draw.Card(net, DebugTraceLayers.Labels);

            // ⛔ CE-1327 — the hit points row (30) is gone: the health bar at the top of the card (row 10) shows them (user, 2026-10-25:
            //   "we can remove the HP line as we are showing the health bar at the top of the card").

            // Row 40: the active behaviour (yellow).
            if (_behaviorRegistry != null && Has<BehaviorState>(view, entity))
            {
                ref readonly var bs = ref view.GetComponentRO<BehaviorState>(entity);

                // ActiveBehaviorHash == 0 is the sentinel for "no active behavior" (e.g. before the
                // first assignment, or after a one-shot tree returns Success and deactivates). That is
                // a normal, empty state — render nothing. A non-zero hash that does NOT resolve to a
                // registered name is a genuine error (unregistered/skipped behavior) and stays "?".
                if (bs.ActiveBehaviorHash != 0)
                {
                    string text = _behaviorRegistry.TryGetName(bs.ActiveBehaviorHash, out string? behaviorName) && behaviorName != null
                        ? (behaviorName.Length > 20 ? behaviorName.Substring(0, 20) : behaviorName)
                        : "?";
                    card.Row(BehaviourRow).Text(text, BehaviorColor, 12f);
                }
            }
        }

        // ⭐ CE-3123 — on every host now, so a type the host never registered must read as "absent", not be asked of the world.
        private static bool Has<T>(ISimulationView view, Entity entity) where T : unmanaged =>
            (view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(entity);
    }
}
