using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ <c>CE-3144</c> — <c>GET /entities/{networkId}/memory</c>: a unit's RAW <see cref="TargetMemory"/>, every slot.
    /// ⚠ <c>GET /entities/{id}</c> shows memory through the scenario translator, which drops heard (anonymous) slots and
    /// entities it cannot map — so it cannot answer <i>"did the shot reach the brain?"</i>. This route can.
    /// <para>Read it on the BRAIN perspective (<c>Scenario</c> on a cluster) — memory is written there.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        /// <summary><c>GET /entities/{networkId}/memory</c>.</summary>
        public unsafe JsonNode GetEntityMemory(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };
            if (!_world.IsComponentTypeRegistered<TargetMemory>() || !_world.HasComponent<TargetMemory>(unit))
                return new JsonObject { ["networkId"] = networkId, ["count"] = 0, ["hasMemory"] = false };

            ref readonly var m = ref _world.GetComponentRO<TargetMemory>(unit);
            var slots = new JsonArray();
            int count = System.Math.Min(m.Count, PerceptionConstants.MaxTrackedTargets);
            fixed (long* ids = m.EntityIds)
            fixed (float* x = m.PositionsX, y = m.PositionsY, z = m.PositionsZ, fresh = m.Freshness, radius = m.Radius)
            fixed (uint* seen = m.LastSeenTick)
            fixed (byte* modal = m.Modalities, anon = m.Anonymous, cls = m.SourceClass)
            {
                for (int i = 0; i < count; i++)
                {
                    var slot = new JsonObject
                    {
                        ["id"]           = ids[i],
                        ["anonymous"]    = anon[i] != 0,
                        ["position"]     = new JsonArray(Round(x[i]), Round(y[i]), Round(z[i])),
                        ["freshness"]    = Round(fresh[i]),
                        ["radius"]       = Round(radius[i]),
                        ["lastSeenTick"] = seen[i],
                        ["modalities"]   = modal[i],
                        ["sourceClass"]  = cls[i],
                    };
                    if (anon[i] == 0)
                    {
                        var e = new Entity((ulong)ids[i]);
                        slot["alive"] = !e.IsNull && _world.IsAlive(e);
                        if (!e.IsNull && _entityMap.TryGetNetworkId(e, out long net)) slot["networkId"] = net;
                    }
                    slots.Add(slot);
                }
            }

            return new JsonObject
            {
                ["networkId"]       = networkId,
                ["hasMemory"]       = true,
                ["count"]           = count,
                ["changeEpoch"]     = m.ChangeEpoch,
                ["anonymousSerial"] = m.AnonymousSerial,
                ["slots"]           = slots,
            };
        }
    }
}
