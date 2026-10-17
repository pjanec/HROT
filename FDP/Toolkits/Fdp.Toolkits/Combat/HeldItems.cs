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

        /// <summary>What <paramref name="man"/> holds on this node (the owner's or the replicated one); None when nothing or no posture.</summary>
        public static HeldItemKind Of(ISimulationView view, Entity man)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<WeaponPosture>()) return HeldItemKind.None;
            return view.IsAlive(man) && view.HasComponent<WeaponPosture>(man) ? view.GetComponentRO<WeaponPosture>(man).HeldItem : HeldItemKind.None;
        }

        /// <summary>Are <paramref name="man"/>'s binoculars up?</summary>
        public static bool BinocularsUp(ISimulationView view, Entity man) => Of(view, man) == HeldItemKind.Binoculars;

        /// <summary>HI-4 — <paramref name="observer"/>'s identification optics: <see cref="BinocularsOptics"/> while his binoculars are up, else 1.</summary>
        public static float IdentificationOptics(ISimulationView view, Entity observer) => BinocularsUp(view, observer) ? BinocularsOptics : 1f;

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
