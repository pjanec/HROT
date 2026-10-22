using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Hrot.Editor.DebugApi;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4042</c> (docs/DESIGN_Scenario_Regression.md §1) — the editor as the verifier's world: every member a delegate onto what the
    /// debug service ALREADY does for <c>GET /entities/{id}</c>, <c>/combat/shots</c>, <c>/combat/detonations</c> and
    /// <c>POST /entities/{id}/component</c>, so a check reads and edits exactly what an HTTP client would. ⚠ That service exists only when a
    /// debug port is set — <c>--verify</c> sets one; without it a verify run fails with that reason, never silently.
    /// </summary>
    internal sealed class EditorVerifyWorld : IVerifyWorld
    {
        private const string NoService = "the verifier reads the world through the debug service — set HROT_DEBUG_API_PORT (--verify sets one)";
        private readonly Func<EntityRepository?> _world;
        private readonly Func<int, string?> _behaviourName;
        private readonly Func<DebugApiService?> _service;

        public EditorVerifyWorld(Func<EntityRepository?> world, IShotWorld units, Func<int, string?> behaviourName, Func<DebugApiService?> service)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            Units = units ?? throw new ArgumentNullException(nameof(units));
            _behaviourName = behaviourName ?? throw new ArgumentNullException(nameof(behaviourName));
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public EntityRepository? World => _world();
        public IShotWorld Units { get; }
        public string? BehaviourName(int hash) => _behaviourName(hash);

        private DebugApiService Service => _service() ?? throw new InvalidOperationException(NoService);

        public System.Collections.Generic.IReadOnlyList<(long Id, string Name)> Named()
        {
            var list = new System.Collections.Generic.List<(long, string)>();
            if (_world() is not { } w || !w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()) return list;
            foreach (var e in w.Query().With<EntityInfo>().With<Fdp.Toolkit.Replication.Components.NetworkIdentity>().Build())
            {
                var name = w.GetComponentRO<EntityInfo>(e).Name.ToString();
                if (name.Length > 0) list.Add((w.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value, name));
            }
            return list;
        }

        public string? NameOfEntityIndex(int index)
        {
            if (_world() is not { } w || index < 0 || index > w.MaxEntityIndex) return null;
            var e = w.GetEntityByIndex(index);
            if (!w.IsAlive(e) || !w.HasComponent<EntityInfo>(e)) return null;
            var name = w.GetComponentRO<EntityInfo>(e).Name.ToString();
            return name.Length > 0 ? name : null;
        }

        public JsonObject? Components(long networkId) => Service.DumpEntity(networkId)?["Components"] as JsonObject;

        public JsonArray Shots() => Service.GetShots(int.MaxValue, null, null)["shots"] as JsonArray ?? new JsonArray();

        public JsonArray Blasts() => Service.GetDetonations(int.MaxValue)["detonations"] as JsonArray ?? new JsonArray();

        public string? SetComponent(long networkId, string component, JsonObject patch)
            => Service.EditEntityComponent(networkId, component, patch.DeepClone()).error;
    }
}
