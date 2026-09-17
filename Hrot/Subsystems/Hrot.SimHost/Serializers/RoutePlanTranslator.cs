using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common.Components;
using Hrot.Map.Definitions;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Custom scenario translator for the <see cref="RoutePlan"/> managed component.
    ///
    /// <para><b>⭐ Why this exists (BP-518).</b> Managed components reach a scenario only through an
    /// explicit <see cref="IEntityScenarioTranslator"/>, and this one was missing while
    /// <c>PersonalRouteRefTranslator</c> existed — so the LINK from a vehicle to its route persisted
    /// while the route's GEOMETRY did not. A saved scenario reloaded as a vehicle pointing at a route
    /// entity with ZERO waypoints, silently and with no error.</para>
    ///
    /// <para><b>⚠ Two things this must get right, both of which the mirror does not give for free:</b>
    /// <list type="number">
    ///   <item><see cref="RoutePlan.Waypoints"/> is an <c>IReadOnlyList</c> over a private list and is
    ///     mutable ONLY through <see cref="RoutePlan.Mutate"/> ⇒ it cannot be auto-deserialized, and
    ///     going through <c>Mutate</c> is also what keeps <see cref="RoutePlan.Version"/> correct.</item>
    ///   <item><see cref="RouteWaypoint.ExtensionJson"/> is nullable free-form JSON (per-waypoint AI
    ///     soft advice) and must survive the round trip, including staying <c>null</c>.</item>
    /// </list></para>
    ///
    /// <para>⛔ <b>Version is deliberately NOT persisted.</b> It is a local reactive stamp that
    /// <c>Mutate</c> maintains; writing it back would either fight <c>Mutate</c> or reintroduce the
    /// never-incremented-counter bug that <c>EditablePolyline.Version</c> already has. A freshly loaded
    /// route simply starts from the version <c>Mutate</c> gives it.</para>
    ///
    /// 📄 docs/designs/routes-1/ROUTES1-DESIGN.md §16 (the as-built gap), §4 (the mutation contract).
    /// </summary>
    public sealed class RoutePlanTranslator : IEntityScenarioTranslator
    {
        private const string Key = "RoutePlan";

        // RoutePlan holds value data only (waypoints + loop flag); no entity handles to resolve.
        public bool IsExtractionSafe => true;

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(RoutePlan));
            if (id >= 0)
                mask.SetBit(id);
            else
                mask.SetBit(HrotComponentIds.RoutePlan);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasManagedComponent<RoutePlan>(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver guidResolver)
        {
            var plan = ((ISimulationView)repo).GetManagedComponentRO<RoutePlan>(entity);
            var waypoints = new JsonArray();

            if (plan?.Waypoints != null)
            {
                foreach (var wp in plan.Waypoints)
                {
                    var node = new JsonObject
                    {
                        ["X"]           = wp.Position.X,
                        ["Y"]           = wp.Position.Y,
                        ["Z"]           = wp.Position.Z,
                        ["TargetSpeed"] = wp.TargetSpeed,
                    };

                    // ⚠ Only emit when present, so a null stays null on the way back rather than
                    //   becoming the string "null" or an empty object.
                    if (wp.ExtensionJson != null)
                        node["ExtensionJson"] = wp.ExtensionJson;

                    waypoints.Add(node);
                }
            }

            var obj = new JsonObject
            {
                ["Waypoints"] = waypoints,
                ["IsLoop"]    = plan?.IsLoop ?? false,
            };

            return new Dictionary<string, object> { [Key] = obj };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw) || raw is not JsonObject obj)
                return;

            var plan = new RoutePlan
            {
                IsLoop = obj["IsLoop"]?.GetValue<bool>() ?? false,
            };

            if (obj["Waypoints"] is JsonArray waypointArray)
            {
                // ⭐ THE MUTATION CONTRACT — the only legal way to fill the list, and what bumps Version.
                plan.Mutate(list =>
                {
                    foreach (var node in waypointArray)
                    {
                        if (node is not JsonObject wpObj) continue;

                        list.Add(new RouteWaypoint
                        {
                            Position = new Vector3(
                                wpObj["X"]?.GetValue<float>() ?? 0f,
                                wpObj["Y"]?.GetValue<float>() ?? 0f,
                                wpObj["Z"]?.GetValue<float>() ?? 0f),
                            TargetSpeed   = wpObj["TargetSpeed"]?.GetValue<float>() ?? 0f,
                            ExtensionJson = wpObj["ExtensionJson"]?.GetValue<string>(),
                        });
                    }
                });
            }

            repo.SetManagedComponent(entity, plan);
        }

        public IEnumerable<string> GetOutputDomKeys()
        {
            yield return Key;
        }
    }
}
