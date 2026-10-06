using System;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// ⭐⭐ <c>CE-3072</c> B0 (R-213) — a behaviour's danger-area sensor: the ONE way to create, re-point and drop one.
    /// 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.2 (the sensor form), §10.6 (this contract).
    /// <para>It is an ordinary sensor child (<see cref="EqsChildSensor"/>: part id allocated, run-stamped, destroyed when the run
    /// ends) whose answer is the AREA family's — it carries <see cref="DangerAreaCognitiveBuffer"/>, never
    /// <see cref="EqsCognitiveBuffer"/> — plus <see cref="SensorTag"/> (<see cref="SensorModality.DangerArea"/>, so
    /// <c>UnitSensors.Of</c> finds it) and <see cref="DangerAreaSensor"/> (its <see cref="DangerAreaSettings"/>). Its
    /// <see cref="EqsSensor"/> is the transport: <see cref="TemplateId"/> routes it to the <c>DangerAlongRoute</c> solver on the
    /// node holding the route (B3), the corridor rides <c>SearchRadius</c>, a <see cref="DangerRouteSource.ToPoint"/> end rides
    /// <c>ContextPoint1</c>.</para>
    /// <para>⚠ Live world only (<see cref="EntityRepository"/> — what every behaviour tick is handed): the extra components are
    /// added on the spot.</para>
    /// </summary>
    public static class DangerAreaChildSensor
    {
        /// <summary>The <c>DangerAlongRoute</c> query's asset identity.</summary>
        public const string AssetId = "d4a6e1c0-3072-4b0a-9d1e-da9ae0a10001";

        /// <summary>The template id its <see cref="EqsSensor.BlueprintId"/> carries (<see cref="EqsTemplateRegistry.BlueprintIdOf"/>).</summary>
        public static readonly uint TemplateId = EqsTemplateRegistry.BlueprintIdOf(Guid.Parse(AssetId));

        /// <summary>
        /// The current run's danger-area sensor at <paramref name="siteId"/>/<paramref name="key"/> — created on the first call
        /// with <paramref name="settings"/>. An existing one is returned as it is (use <see cref="Configure"/> to re-point it).
        /// </summary>
        public static Entity Ensure(EntityRepository world, Entity owner, int siteId, in DangerAreaSettings settings, long key = 0)
        {
            var existing = EqsChildSensor.Find(world, owner, siteId, key);
            if (!existing.IsNull) return existing;

            var child = EqsChildSensor.Ensure<DangerAreaCognitiveBuffer>(world, owner, siteId, Transport(settings), key);
            if (child.IsNull) return child;
            world.AddComponent(child, new SensorTag { Kind = SensorModality.DangerArea });
            world.AddComponent(child, new DangerAreaSensor
            {
                BlueprintId = TemplateId,
                RefreshIntervalSeconds = settings.RefreshSeconds,
                Settings = settings,
            });
            return child;
        }

        /// <summary>Re-point <paramref name="child"/> with new settings: a new epoch, and its answer cleared (an older answer is
        /// for the old route). False when it is not a live danger-area sensor.</summary>
        public static bool Configure(EntityRepository world, Entity child, in DangerAreaSettings settings)
        {
            if (child.IsNull || !world.IsAlive(child) || !world.HasComponent<DangerAreaSensor>(child)) return false;
            ref var s = ref world.GetComponentRW<DangerAreaSensor>(child);
            s.Settings = settings;
            s.RefreshIntervalSeconds = settings.RefreshSeconds;
            return EqsChildSensor.Refresh(world, child, Transport(settings));
        }

        /// <summary>Drop the sensor (the solver stops; the run's teardown does the same on its own).</summary>
        public static void Release(EntityRepository world, Entity child) => EqsChildSensor.Destroy(world, child);

        /// <summary>The settings as they ride the EQS sensor transport.</summary>
        public static EqsSensor Transport(in DangerAreaSettings settings)
        {
            bool toPoint = settings.RouteSource == DangerRouteSource.ToPoint;
            return new EqsSensor
            {
                BlueprintId      = TemplateId,
                Epoch            = 1,
                SearchRadius     = settings.CorridorHalfWidth,
                ContextPoint1    = toPoint ? settings.RoutePoint : default,
                ContextPointMask = toPoint ? EqsSensor.Point1Bit : (byte)0,
            };
        }
    }
}
