using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ Capability components C1 (docs/DESIGN_Capability_Components.md §4.1, CC-1) — the ITEM facts: what a thing can be mounted on, how many men
    /// carry it and how it slows its carrier. Its presence MAKES the entity an item (<see cref="Items.IsMountable"/>): a thing that can be carried,
    /// mounted, boarded, manned, dropped. Split out of <see cref="WeaponSystemParams"/> (which keeps the FIRE facts); written by the same
    /// <see cref="WeaponSystemTkbTranslator"/> from the same <c>WeaponSystemDto</c> (a device later writes it from its own DTO). A type fact: never
    /// saved, never replicated.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Mountable)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Mountable
    {
        /// <summary><see cref="Fdp.Toolkit.Tkb.Domain.MountKinds"/> flags — where it can be mounted.</summary>
        public byte MountKinds;
        /// <summary><see cref="WeaponPortability"/>.</summary>
        public byte Portable;
        /// <summary>The carrier's speed factor while it carries this.</summary>
        public float CarrySpeedFactor;

        public static Mountable From(WeaponSystemDto d) => new()
        {
            MountKinds = (byte)d.MountKinds, Portable = (byte)d.Portable, CarrySpeedFactor = d.CarrySpeedFactor,
        };
    }

    /// <summary>⭐ CC-1 — the ONE item predicate (docs/DESIGN_Capability_Components.md §3): is the entity something that can be carried / mounted / boarded?</summary>
    public static class Items
    {
        /// <summary>Is <paramref name="e"/> an item (alive, <see cref="Mountable"/> registered and present)? Same shape as <c>WeaponSystems.IsWeaponSystem</c>.</summary>
        public static bool IsMountable(EntityRepository world, Entity e)
            => !e.IsNull && world.IsAlive(e) && world.IsComponentTypeRegistered<Mountable>() && world.HasComponent<Mountable>(e);

        /// <summary>The same test for a read-only view (a system's tick-start view).</summary>
        public static bool IsMountable(ISimulationView view, Entity e)
            => view is EntityRepository world ? IsMountable(world, e) : !e.IsNull && view.IsAlive(e) && view.HasComponent<Mountable>(e);
    }
}
