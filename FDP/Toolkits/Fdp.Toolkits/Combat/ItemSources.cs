using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-3235</c> (B-13, AW-8; docs/DESIGN_Civilians_And_Crowd_Control.md §7.10 PU-7) — the AREAS a hand takes an item from without
    /// limit (a stone source): an authored area entity (an <c>EditablePolyline</c> + a <see cref="SimTransform"/>, like a Weather Region)
    /// whose TKB type carries an <see cref="ItemSourceDto"/>. A library, no system: a source is unlimited, so there is nothing to arbitrate —
    /// the behaviour calls <see cref="Refill"/> on the man's brain node when its bend is done.
    /// </summary>
    public static class ItemSources
    {
        /// <summary>The item source whose area holds <paramref name="point"/> (XY), if any.</summary>
        public static bool At(EntityRepository world, Vector3 point, out ItemSourceDto source, out Entity area)
        {
            source = null!; area = Entity.Null;
            if (!world.HasSingletonManaged<ITkbDatabase>() || world.GetSingletonManaged<ITkbDatabase>() is not { } db
                || !world.IsComponentTypeRegistered<TkbIdentity>()) return false;
            var p = new Vector2(point.X, point.Y);
            foreach (var e in world.Query().With<TkbIdentity>().With<SimTransform>().Build())
            {
                if (!Weather.WeatherField.TryOutline(world, e, out _)) continue;
                if (!db.TryGetByType(world.GetComponentRO<TkbIdentity>(e).TkbType, out var t) || t?.GetDescriptor<ItemSourceDto>() is not { Ammo: not 0 } s)
                    continue;
                if (!Weather.WeatherField.AreaEntityContains(world, e, p)) continue;   // ⭐ the ONE area-entity containment (Player Join S2 uses it too; this file's private copy is gone)
                source = s; area = e;
                return true;
            }
            return false;
        }

        /// <summary>
        /// ⭐ Top <paramref name="man"/>'s hands up to the source's <see cref="ItemSourceDto.UpTo"/> when he stands in one; returns how many
        /// he took (0 = not in a source, already full, or no room in his hands).
        /// </summary>
        public static int Refill(EntityRepository world, Entity man)
        {
            if (!world.IsAlive(man) || !world.HasComponent<SimTransform>(man)) return 0;
            if (!At(world, world.GetComponentRO<SimTransform>(man).Position, out var source, out _)) return 0;
            int need = source.UpTo - PersonHands.Rounds(world, man, source.Ammo);
            return need > 0 && PersonHands.Give(world, man, source.Ammo, need) ? need : 0;
        }
    }
}
