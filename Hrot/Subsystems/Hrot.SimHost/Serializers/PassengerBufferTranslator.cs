using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Kernel;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Scenario;

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

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
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

            for (int i = 0; i < buffer.Count; i++)
            {
                Entity passenger = buffer.Passengers[i];
                if (passenger.IsNull || !repo.IsAlive(passenger))
                    continue;  // skip stale/null — not in the save map

                passengers.Add(resolver.Resolve(passenger));
            }

            return new Dictionary<string, object>
            {
                [Key] = new JsonObject
                {
                    ["Count"]      = buffer.Count,
                    ["Passengers"] = passengers,
                }
            };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw)) return;
            if (raw is not JsonObject obj) return;

            int count      = obj["Count"]?.GetValue<int>() ?? 0;
            var passengers = obj["Passengers"] as JsonArray;

            var buffer = new PassengerBuffer { Count = count };
            int filled = 0;

            if (passengers != null)
            {
                foreach (var item in passengers)
                {
                    if (filled >= count || filled >= PassengerBuffer.Capacity) break;

                    var guidStr = item?.GetValue<string>();
                    if (string.IsNullOrEmpty(guidStr))
                    {
                        filled++;
                        continue;
                    }

                    buffer.Passengers[filled] = resolver.Resolve(guidStr);
                    filled++;
                }
            }

            repo.SetComponent(entity, buffer);
        }

        public IEnumerable<string> GetOutputDomKeys() => Array.Empty<string>();
    }
}
