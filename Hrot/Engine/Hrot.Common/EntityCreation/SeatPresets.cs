using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Common.EntityCreation
{
    /// <summary>
    /// ⭐⭐ Weapon systems WS-4 (W-I, R-138; docs/designs/weapons/DESIGN_Weapon_Systems.md §4d, §6;
    /// STEER_Behaviors_To_Backend_Weapon_Systems_Crew_Presets) — a host type's SEAT PRESETS: every seat's <c>SeatDto.StartWith</c> (the M2 for
    /// its station, a rifleman for its gunner seat). <c>CreateEntityRequestSystem</c> fills them ONCE, on the node that services the host's
    /// creation: one spawn per preset beside the host's (the child-blueprint pattern), each carrying a <see cref="PresetSeatIntent"/>, and the
    /// host carrying <see cref="SeatPresetsFilled"/> — saved, so a load never fills them again.
    /// </summary>
    public static class SeatPresets
    {
        /// <summary>The presets of <paramref name="template"/> (seat, TKB type), in seat order; empty when it has none.</summary>
        public static List<(int Seat, long Type)> Of(TkbTemplate? template)
        {
            var presets = new List<(int, long)>();
            var carrier = template?.GetDescriptor<CarrierDefDto>();
            if (carrier == null) return presets;
            for (int i = 0; i < carrier.Seats.Count && i < PassengerBuffer.Capacity; i++)
                if (carrier.Seats[i].StartWith != 0) presets.Add((i, carrier.Seats[i].StartWith));
            return presets;
        }

        /// <summary>The presets to fill for a host of <paramref name="template"/> created with <paramref name="components"/>: none when the
        /// request carries <see cref="SeatPresetsFilled"/> (a scenario load of a host already filled), else <see cref="Of"/>.</summary>
        public static List<(int Seat, long Type)> ToFill(TkbTemplate? template, IReadOnlyList<object>? components)
        {
            if (components != null)
                foreach (var c in components)
                    if (c is SeatPresetsFilled) return new List<(int, long)>();
            return Of(template);
        }

        /// <summary>The force a host's preset crew takes: the request's <see cref="EntityInfo"/>, else its type's faction.</summary>
        public static ForceId ForceOf(TkbTemplate? template, IReadOnlyList<object>? components)
        {
            if (components != null)
                foreach (var c in components)
                    if (c is EntityInfo info) return info.ForceId;
            return template?.GetDescriptor<BehaviorProfileDto>()?.Faction ?? ForceId.Neutral;
        }
    }

    /// <summary>
    /// ⭐⭐ WS-4 (W-I) — boards each preset passenger (<see cref="PresetSeatIntent"/>) into its seat once its host is there: a weapon into
    /// its station, a soldier into his crew seat (<see cref="Embarkation.TryEmbarkAt"/>, the seat the TYPE names), then drops the intent.
    /// Runs on every node that builds the entity-creation pack; only the creating node holds an intent.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class SeatPresetSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;
        private readonly List<Entity> _done = new();

        public SeatPresetSystem(NetworkEntityMap entityMap) => _entityMap = entityMap;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<PresetSeatIntent>()) return;
            _done.Clear();
            foreach (var e in repo.Query().With<PresetSeatIntent>().Build())
            {
                var intent = repo.GetComponentRO<PresetSeatIntent>(e);
                if (!_entityMap.TryGetEntity(intent.HostNetworkId, out var host) || !repo.IsAlive(host)
                    || !repo.HasComponent<PassengerBuffer>(host)) continue;   // the host is not here yet
                var result = Embarkation.TryEmbarkAt(repo, e, host, intent.Seat);
                if (result is not (EmbarkResult.Applied or EmbarkResult.AlreadyAboard))
                    FdpLog<SeatPresetSystem>.Warn($"[SeatPresets] preset passenger {e} could not board seat {intent.Seat} of {host}: {result}");
                _done.Add(e);
            }
            foreach (var e in _done) repo.RemoveComponent<PresetSeatIntent>(e);
        }
    }
}
