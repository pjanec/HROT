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

        public System.Collections.Generic.IReadOnlyList<(int Id, string Name)> Named() => VerifyWorldNames.Named(_world());

        public string? NameOfEntityIndex(int index) => VerifyWorldNames.NameOfEntityIndex(_world(), index);

        public JsonObject? Components(int networkId) => Service.DumpEntity(networkId)?["Components"] as JsonObject;

        public JsonArray Shots() => Service.GetShots(int.MaxValue, null, null)["shots"] as JsonArray ?? new JsonArray();

        public JsonArray Blasts() => Service.GetDetonations(int.MaxValue)["detonations"] as JsonArray ?? new JsonArray();

        /// <summary>⭐ CE-4676 — the brain node's report log, as <c>GET /squad/reports</c> serves it.</summary>
        public bool CommanderHoldsClock() => Service.AnyCommanderHoldsClock();

        public JsonArray Reports() => Service.GetSquadReports(Fdp.Toolkit.Squad.Command.SquadReportLog.Capacity, null)["reports"] as JsonArray ?? new JsonArray();

        public string? SetComponent(int networkId, string component, JsonObject patch)
            => Service.EditEntityComponent(networkId, component, patch.DeepClone()).error;

        /// <summary>⭐ CE-3494 F3 — the <c>POST /orders</c> service, one item.</summary>
        public string? Order(int networkId, JsonObject order)
        {
            var item = (JsonObject)order.DeepClone();
            item["networkId"] = networkId;
            var (result, error) = Service.PostOrders(new JsonObject { ["orders"] = new JsonArray(item) });
            if (error != null) return error;
            var r = (result?["results"] as JsonArray)?[0];
            return r?["accepted"]?.GetValue<bool>() == true ? null : (r?["detail"] ?? r?["refusal"])?.ToString() ?? "refused";
        }

        /// <summary>⭐ CE-3494 F3 — the <c>POST /missions/{id}/resume</c> service (the command lands on the next step).</summary>
        public string? ResumePlan(int networkId) => Service.BeginResumeMission(networkId).error;

        /// <summary>⭐ CE-11061 — the <c>POST /entities/{id}/revive</c> service.</summary>
        public string? Revive(int networkId) => Service.ReviveEntity(networkId).error;

        /// <summary>⭐ S1e-2 (DESIGN_Simulated_Devices §5a) — the node's station the checks drive (possess · view · control), on the brain world.</summary>
        public IVerifyStation? Station => _service() is { } s ? s.ScriptedStation : null;

        /// <summary>⭐ S1e-2 — a unit's [UnitMemory] struct by name, read on the brain world (⛔ not <see cref="World"/> — the threaded editor's UI mirror).</summary>
        public JsonObject? Memory(int networkId, string type) => Service.UnitMemoryOf(networkId, type);

        /// <summary>⭐ S1e-2 — the HUD run a view bound to the source draws in the published gizmo frame.</summary>
        public int HudRun(long source, System.Collections.Generic.List<string> texts) => Service.HudRun(source, texts);
    }
}
