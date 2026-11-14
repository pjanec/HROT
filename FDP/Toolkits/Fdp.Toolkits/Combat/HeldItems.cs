using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-3234</c> (civilians B-12, AW-14; docs/DESIGN_Civilians_And_Crowd_Control.md §7.14) — the one writer and the readers of what a
    /// person holds in his hand (<see cref="WeaponPosture.HeldItem"/>, replicated with the posture). Weapon / Throwable name the posture's
    /// <see cref="WeaponPosture.ActiveMount"/> held up; Phone / Binoculars are pocket items of his type (<see cref="PocketItemsDto"/>).
    /// </summary>
    public static class HeldItems
    {
        /// <summary>HI-4 — binoculars up: the visual sensor's range is multiplied by this …</summary>
        public const float BinocularsRangeFactor = 2f;

        /// <summary>… and its field of view narrows to this (degrees).</summary>
        public const float BinocularsFovDegrees = 10f;

        /// <summary>HI-4 — binoculars up: the identification optics (<see cref="SeeingConditions.Optics"/>).</summary>
        public const float BinocularsOptics = 4f;

        /// <summary>
        /// HI-2 — <paramref name="man"/> takes <paramref name="kind"/> in his hand (None empties it). Call it on the node that owns his brain
        /// (and so <c>dtWeaponPosture</c>). False — nothing changes — when he has no posture, or not the thing: a phone or binoculars his type
        /// does not carry, a throwable with no hands, a weapon with none.
        /// </summary>
        public static bool Set(EntityRepository world, Entity man, HeldItemKind kind)
        {
            if (!world.IsAlive(man) || !world.IsComponentTypeRegistered<WeaponPosture>() || !world.HasComponent<WeaponPosture>(man)) return false;
            if (!Has(world, man, kind)) return false;
            world.GetComponentRW<WeaponPosture>(man).HeldItem = kind;
            return true;
        }

        /// <summary>Does <paramref name="man"/> have <paramref name="kind"/> to hold?</summary>
        public static bool Has(EntityRepository world, Entity man, HeldItemKind kind)
        {
            switch (kind)
            {
                case HeldItemKind.None: return true;
                case HeldItemKind.Phone:
                case HeldItemKind.Binoculars:
                    if (kind == HeldItemKind.Binoculars && Leads(world, man)) return true;   // ⭐ CE-1224
                    if (!CombatTkb.TryGetTemplate(world, man, out var t) || t.GetDescriptor<PocketItemsDto>() is not { } pockets) return false;
                    return kind == HeldItemKind.Phone ? pockets.Phone : pockets.Binoculars;
                case HeldItemKind.Throwable:
                    return PersonHands.MountIndex(world, man) >= 0;
                case HeldItemKind.Weapon:
                    if (world.IsComponentTypeRegistered<WeaponState>() && world.HasComponent<WeaponState>(man)) return true;
                    Span<Entity> controlled = stackalloc Entity[WeaponSystems.MaxControlled];
                    return WeaponSystems.Controlled(world, man, controlled) > 0;
                default: return false;
            }
        }

        /// <summary>
        /// ⭐ <c>CE-1224</c> (ui note §4, Entity Control §8k B-1) — a LEADER carries binoculars whatever his type: leaders are not types in
        /// any catalogue but designations in a unit (the BDC squad's leader, a fire-team leader, a vehicle's commander), so the rule reads his
        /// <see cref="Fdp.Core.CommandHierarchy.UnitSubordinate.Designation"/>. A type's <see cref="PocketItemsDto.Binoculars"/> still gives
        /// them to anyone else (the mortar crew).
        /// </summary>
        public static bool Leads(EntityRepository world, Entity man)
            => world.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitSubordinate>()
               && world.HasComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>(man)
               && world.GetComponentRO<Fdp.Core.CommandHierarchy.UnitSubordinate>(man).Designation
                   is Fdp.Core.CommandHierarchy.TacticalDesignation.Commander
                   or Fdp.Core.CommandHierarchy.TacticalDesignation.SquadLeader
                   or Fdp.Core.CommandHierarchy.TacticalDesignation.TeamLeader;

        /// <summary>What <paramref name="man"/> holds on this node (the owner's or the replicated one); None when nothing or no posture.</summary>
        public static HeldItemKind Of(ISimulationView view, Entity man)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<WeaponPosture>()) return HeldItemKind.None;
            return view.IsAlive(man) && view.HasComponent<WeaponPosture>(man) ? view.GetComponentRO<WeaponPosture>(man).HeldItem : HeldItemKind.None;
        }

        /// <summary>Are <paramref name="man"/>'s binoculars up?</summary>
        public static bool BinocularsUp(ISimulationView view, Entity man) => Of(view, man) == HeldItemKind.Binoculars;

        /// <summary>HI-4 — <paramref name="observer"/>'s identification optics: <see cref="BinocularsOptics"/> while his binoculars are up;
        /// ⭐ <c>CE-3254</c> (BR-9) his scope's magnification while he aims a scoped weapon; else 1.</summary>
        public static float IdentificationOptics(ISimulationView view, Entity observer)
            => BinocularsUp(view, observer) ? BinocularsOptics : ScopeUp(view, observer, out float magnification, out _) ? magnification : 1f;

        /// <summary>⭐ <c>CE-3254</c> — a scope's field of view when its mount does not state one (degrees).</summary>
        public const float ScopeFallbackFovDegrees = 6f;

        /// <summary>
        /// ⭐ <c>CE-3254</c> (BR-9, DA-13) — is <paramref name="man"/> looking through a SCOPE: AIMED (<see cref="WeaponPostureKind.Aimed"/>) with
        /// an own mount (<see cref="WeaponPosture.ActiveMount"/> &lt; 32) whose <see cref="WeaponMountDto.ScopeMagnification"/> &gt; 1 — the binocular
        /// rule on a weapon: <paramref name="magnification"/> and <paramref name="fovDegrees"/> are what <c>VisualPerception.Optics</c> and the
        /// identification read. Lowered (Ready, Idle) he looks with his naked eye.
        /// </summary>
        public static bool ScopeUp(ISimulationView view, Entity man, out float magnification, out float fovDegrees)
        {
            magnification = 1f; fovDegrees = 0f;
            if (view is not EntityRepository world || !world.IsComponentTypeRegistered<WeaponPosture>()
                || !world.IsAlive(man) || !world.HasComponent<WeaponPosture>(man)) return false;
            ref readonly var posture = ref world.GetComponentRO<WeaponPosture>(man);
            if (posture.Kind != WeaponPostureKind.Aimed || posture.ActiveMount >= 32) return false;
            if (CombatTkb.MountOf(world, man, posture.ActiveMount) is not { ScopeMagnification: > 1f } mount) return false;
            magnification = mount.ScopeMagnification;
            fovDegrees = mount.ScopeFovDegrees > 0f ? mount.ScopeFovDegrees : ScopeFallbackFovDegrees;
            return true;
        }

        /// <summary>
        /// HI-5 — the pocket item <paramref name="man"/> shows in his hand, as an item an observer judges (a small non-lethal object); false
        /// when he holds none (a weapon or a throwable is judged as its mount already).
        /// </summary>
        public static bool Shows(ISimulationView view, Entity man, out ItemAppearance item)
        {
            var kind = Of(view, man);
            if (kind is HeldItemKind.Phone or HeldItemKind.Binoculars)
            {
                item = new ItemAppearance(ItemLook.SmallObject, ItemAppearances.DefaultSize(ItemLook.SmallObject), lethal: false, reachM: 0f);
                return true;
            }
            item = default;
            return false;
        }
    }
}
