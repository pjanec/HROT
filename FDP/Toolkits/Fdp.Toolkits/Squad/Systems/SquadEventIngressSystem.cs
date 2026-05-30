using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// Translates per-member ECS state changes into squad-level <see cref="PhaseEvent"/>s
    /// that can be fed to <see cref="PhaseSequencer.Advance"/>.
    ///
    /// Four detection sources:
    /// <list type="bullet">
    ///   <item><see cref="PhaseEventKind.ShotFired"/> — member WeaponState.Ammo decreased.</item>
    ///   <item><see cref="PhaseEventKind.FarSideReached"/> — member NavigationStatus.Result == Arrived
    ///     and IntentId matches <see cref="FarSideIntentId"/>.</item>
    ///   <item><see cref="PhaseEventKind.BoundComplete"/> — same pattern, <see cref="BoundIntentId"/>.</item>
    ///   <item><see cref="PhaseEventKind.DefiladeReached"/> — same pattern, <see cref="DefiladeIntentId"/>.</item>
    /// </list>
    /// TimerFallback is NOT emitted here; <see cref="PhaseSequencer.Advance"/> handles it internally.
    /// </summary>
    public unsafe sealed class SquadEventIngressSystem
    {
        /// <summary>NavigationIntent.IntentId that signals far-side arrival. 0 = disabled.</summary>
        public uint FarSideIntentId;
        /// <summary>NavigationIntent.IntentId that signals bound completion. 0 = disabled.</summary>
        public uint BoundIntentId;
        /// <summary>NavigationIntent.IntentId that signals defilade reached. 0 = disabled.</summary>
        public uint DefiladeIntentId;

        // Per-member previous ammo snapshot (roster-slot indexed).
        private PrevAmmoArray _prevAmmo;

        [InlineArray(16)]
        private struct PrevAmmoArray
        {
#pragma warning disable CS0169
            private int _element;
#pragma warning restore CS0169
        }

        /// <summary>
        /// Scans all roster members and appends detected <see cref="PhaseEvent"/>s to
        /// <paramref name="events"/>. Caller feeds the span to
        /// <see cref="PhaseSequencer.Advance"/> afterward.
        /// </summary>
        /// <param name="repo">Active ECS repository.</param>
        /// <param name="commander">Commander entity (must carry UnitRoster).</param>
        /// <param name="events">Output list; append-only.</param>
        public void Run(EntityRepository repo, Entity commander, IList<PhaseEvent> events)
        {
            if (!repo.HasComponent<UnitRoster>(commander)) return;
            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);

            var prevAmmoSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<PrevAmmoArray, int>(ref _prevAmmo), 16);

            for (int m = 0; m < roster.Count; m++)
            {
                var member = new Entity((ulong)roster.SubordinateEntities[m]);

                // ── ShotFired ────────────────────────────────────────────────
                if (repo.HasComponent<WeaponState>(member))
                {
                    int currentAmmo = repo.GetComponentRO<WeaponState>(member).Ammo;
                    if (currentAmmo < prevAmmoSpan[m])
                        events.Add(new PhaseEvent(PhaseEventKind.ShotFired));
                    prevAmmoSpan[m] = currentAmmo;
                }

                // ── NavigationStatus events ─────────────────────────────────
                if (repo.HasComponent<NavigationStatus>(member))
                {
                    ref readonly var navStatus = ref repo.GetComponentRO<NavigationStatus>(member);
                    if (navStatus.Result == NavigationResult.Arrived)
                    {
                        if (FarSideIntentId  != 0 && navStatus.IntentId == FarSideIntentId)
                            events.Add(new PhaseEvent(PhaseEventKind.FarSideReached));
                        if (BoundIntentId    != 0 && navStatus.IntentId == BoundIntentId)
                            events.Add(new PhaseEvent(PhaseEventKind.BoundComplete));
                        if (DefiladeIntentId != 0 && navStatus.IntentId == DefiladeIntentId)
                            events.Add(new PhaseEvent(PhaseEventKind.DefiladeReached));
                    }
                }
            }
        }
    }
}
