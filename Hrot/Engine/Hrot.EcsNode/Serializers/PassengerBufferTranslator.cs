using Fdp.Toolkit.Replication;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Custom scenario translator for <see cref="PassengerBuffer"/>.
    ///
    /// The <see cref="Fdp.Toolkit.Scenario.FdpAutoSerializer"/> cannot serialise
    /// <c>[InlineArray]</c>-based <see cref="PassengerSlots"/> elements as
    /// GUID-patched entity references: the inline-array backing field is private and
    /// the entity handles are written as raw packed values that become stale after a
    /// JSON round-trip.  This translator maps every passenger <see cref="Entity"/>
    /// through the <see cref="IGuidResolver"/> so the handles survive
    /// serialisation/deserialisation.
    /// </summary>
    public sealed class PassengerBufferTranslator : IEntityScenarioTranslator
    {
        private const string Key = "PassengerBuffer";

        // ── IEntityScenarioTranslator ─────────────────────────────────────────

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(PassengerBuffer));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<PassengerBuffer>(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            var buffer = repo.GetComponent<PassengerBuffer>(entity);
            var passengers = new JsonArray();
            var seats = new JsonArray();   // ⭐ WS-4 (W-I) — the seat of each, so a crew seat and a station come back as they were
            var modes = new JsonArray();   // ⭐ Initial state S1 (R-4137) — and how each rides (a casualty carried / dragged / on a stretcher)

            for (int i = 0; i < PassengerBuffer.Capacity; i++)   // ⭐ CE-2151 B-b — every seat (a slot IS a seat; holes are skipped)
            {
                Entity passenger = buffer.Passengers[i];
                if (passenger.IsNull || !repo.IsAlive(passenger))
                    continue;  // skip stale/null — not in the save map

                passengers.Add(resolver.Resolve(passenger));
                seats.Add(i);
                modes.Add((int)buffer.ModeOf(i));
            }

            var obj = new JsonObject
            {
                ["Count"]      = buffer.Count,
                ["Passengers"] = passengers,
                ["Seats"]      = seats,
                ["Modes"]      = modes,
            };
            if (!buffer.Assistant.IsNull && repo.IsAlive(buffer.Assistant))   // ⭐ S1 — the second man, by the same link as a passenger
                obj["Assistant"] = resolver.Resolve(buffer.Assistant);
            return new Dictionary<string, object> { [Key] = obj };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw)) return;
            if (raw is not JsonObject obj) return;

            int count      = obj["Count"]?.GetValue<int>() ?? 0;
            var passengers = obj["Passengers"] as JsonArray;
            var seats      = obj["Seats"] as JsonArray;   // ⭐ WS-4 — absent in an older save
            var modes      = obj["Modes"] as JsonArray;   // ⭐ S1 — absent in an older save

            var intent = new InitialPassengersIntent();

            if (passengers != null)
            {
                int filled = 0, index = -1;
                foreach (var item in passengers)
                {
                    index++;
                    if (filled >= count || filled >= PassengerBuffer.Capacity) break;

                    var guidStr = item?.GetValue<string>();
                    filled++;

                    if (string.IsNullOrEmpty(guidStr)) continue;

                    intent.PassengerNetworkIds.Add(FileLinks.FromGuidText(guidStr));
                    if (seats != null && index < seats.Count) intent.Seats.Add(seats[index]!.GetValue<int>());
                    if (modes != null && index < modes.Count) intent.Modes.Add((byte)modes[index]!.GetValue<int>());
                }
            }

            if (intent.Seats.Count != intent.PassengerNetworkIds.Count) intent.Seats.Clear();   // all seats or none
            if (intent.Modes.Count != intent.Seats.Count) intent.Modes.Clear();                // all modes or none (they ride with the seats)
            if (obj["Assistant"]?.GetValue<string>() is { Length: > 0 } assistant) intent.Assistant = FileLinks.FromGuidText(assistant);
            repo.SetManagedComponent(entity, intent);
        }

        public IEnumerable<string> GetOutputDomKeys() => Array.Empty<string>();
    }
}

