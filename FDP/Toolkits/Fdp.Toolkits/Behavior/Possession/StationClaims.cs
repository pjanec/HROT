using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Possession
{
    /// <summary>Why a station may not take a unit (<see cref="StationClaims.ForPossess"/>).</summary>
    public enum ClaimRefusal : byte
    {
        None = 0,
        /// <summary>Another LIVE station holds the unit (<see cref="PossessedBy"/>).</summary>
        HeldByAnotherStation,
        /// <summary>The unit carries a player of another side (a vehicle another side's player sits in).</summary>
        OtherSide,
    }

    /// <summary>
    /// ⭐⭐ <c>R-4096</c> (<c>docs/DESIGN_Entity_Control.md</c> C-3′) — THE ONE RULE of what a player STATION may take from another: it never
    /// takes a unit or a seat another LIVE station holds (not on the same side, not on another); on the same side it may board the same
    /// vehicle into a FREE seat; another side's player's vehicle it may not board at all. ⭐ One rule, read by the station's own answer
    /// (<c>EntityOrders.Possess</c> — HTTP 409, the menu), by the brain's arbiter (<see cref="PossessionSystem"/> — two stations may race), by
    /// the use key (<see cref="OperatorUseActions"/>) and by seat switching. It reads ONLY replicated state (<see cref="PossessedBy"/>,
    /// <c>EntityInfo.ForceId</c>, <see cref="PassengerBuffer"/>, health), so a station predicting and a brain deciding agree.
    /// <para>⚠ A station's SIDE is the side of the unit it holds (lean recorded in C-3′): a station holding nothing has no side, so the
    /// other-side test cannot refuse it — only the held-by-another test applies. When it holds several (its soldier Riding and the vehicle
    /// it drives) its Riding soldier — the human's own body — names the side.</para>
    /// </summary>
    public static class StationClaims
    {
        /// <summary>The side <paramref name="unit"/> belongs to (<c>EntityInfo.ForceId</c>; <see cref="ForceId.Neutral"/> when it has none).</summary>
        public static ForceId ForceOf(EntityRepository repo, Entity unit)
            => repo.IsComponentTypeRegistered<EntityInfo>() && repo.IsAlive(unit) && repo.HasComponent<EntityInfo>(unit)
                ? repo.GetComponentRO<EntityInfo>(unit).ForceId : ForceId.Neutral;

        /// <summary>
        /// The LIVE station that holds <paramref name="unit"/> and is not <paramref name="station"/>; false when nobody does, when this
        /// station holds it itself, or when the held unit is killed (a killed holder frees it — its release is on its way). A null
        /// <paramref name="station"/> (a unit no station holds, e.g. an AI man using the key) counts every holder as another.
        /// </summary>
        public static bool TryHeldByOther(EntityRepository repo, Entity unit, long? station, out long holder)
        {
            holder = 0;
            if (PossessionSystem.HolderOf(repo, unit) is not long h || (station is long s && h == s) || !Live(repo, unit)) return false;
            holder = h;
            return true;
        }

        /// <summary>The side of <paramref name="station"/>: the side of the unit it holds (its Riding soldier first); false when it holds nothing.</summary>
        public static bool TrySideOf(EntityRepository repo, long station, out ForceId side)
        {
            side = ForceId.Neutral;
            if (!repo.IsComponentTypeRegistered<PossessedBy>()) return false;
            bool found = false;
            foreach (var unit in repo.Query().With<PossessedBy>().Build())
            {
                if (!repo.IsAlive(unit) || !Live(repo, unit)) continue;
                var held = repo.GetComponentRO<PossessedBy>(unit);
                if (held.StationNodeId != station) continue;
                side = ForceOf(repo, unit);
                found = true;
                if (held.Mode == PossessionMode.Riding) return true;   // the human's own body names the side
            }
            return found;
        }

        /// <summary>
        /// Does <paramref name="vehicle"/> carry — or is it driven by — a player of ANOTHER side than <paramref name="side"/>? The
        /// candidates are the vehicle itself and every man in its passenger list, each held by a station other than
        /// <paramref name="station"/>; that station's side is <see cref="TrySideOf"/>.
        /// </summary>
        public static bool OtherSideAboard(EntityRepository repo, Entity vehicle, ForceId side, long? station, out long holder)
        {
            holder = 0;
            if (!repo.IsAlive(vehicle)) return false;
            if (OtherSide(repo, vehicle, side, station, out holder)) return true;
            if (!repo.IsComponentTypeRegistered<PassengerBuffer>() || !repo.HasComponent<PassengerBuffer>(vehicle)) return false;
            ref readonly var list = ref repo.GetComponentRO<PassengerBuffer>(vehicle);
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                var aboard = list.Passengers[i];
                if (!aboard.IsNull && repo.IsAlive(aboard) && OtherSide(repo, aboard, side, station, out holder)) return true;
            }
            return false;
        }

        private static bool OtherSide(EntityRepository repo, Entity unit, ForceId side, long? station, out long holder)
            => TryHeldByOther(repo, unit, station, out holder) && TrySideOf(repo, holder, out var theirs) && theirs != side;

        /// <summary>
        /// May <paramref name="station"/> possess <paramref name="unit"/>? <see cref="ClaimRefusal.HeldByAnotherStation"/> when another live
        /// station holds it (<paramref name="holder"/>); <see cref="ClaimRefusal.OtherSide"/> when it carries a player of another side than the
        /// station's (<paramref name="holder"/> is that player's station). A station that holds nothing has no side and is not refused for it.
        /// </summary>
        public static ClaimRefusal ForPossess(EntityRepository repo, Entity unit, long station, out long holder)
        {
            if (TryHeldByOther(repo, unit, station, out holder)) return ClaimRefusal.HeldByAnotherStation;
            if (TrySideOf(repo, station, out var mine) && OtherSideAboard(repo, unit, mine, station, out holder)) return ClaimRefusal.OtherSide;
            holder = 0;
            return ClaimRefusal.None;
        }

        /// <summary>The brain's own test for releasing a killed holder — ONE definition (<see cref="PossessionSystem.Alive"/>).</summary>
        private static bool Live(EntityRepository repo, Entity unit) => PossessionSystem.Alive(repo, unit);
    }
}
