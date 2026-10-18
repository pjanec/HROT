using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Perception.Survey
{
    /// <summary>What <see cref="WorldSurveyChildSensor.Read"/> found: the answer, whether it is there yet, and whether it is too old to trust.</summary>
    public readonly record struct WorldSurveyRead(bool Ready, bool Stale, uint Version, float AgeSeconds, WorldSurveyPayload? Payload);

    /// <summary>
    /// ⭐ <c>CE-3249</c> S3 (R-2037, R-2038, DA-25) — a behaviour's <c>WorldSurvey</c> sensor: the ONE way to create, re-point, read and drop
    /// one. 📄 docs/DESIGN_World_Survey_Sensor.md §2–§3.
    /// <para>An ordinary sensor child (<see cref="EqsChildSensor"/>: part id, run stamp, destroyed with the run) whose answer family is
    /// the survey's — <see cref="WorldSurveyStatus"/> + <see cref="WorldSurveyAnswer"/>, never <see cref="EqsCognitiveBuffer"/>. Its
    /// <see cref="EqsSensor"/> is the transport (<see cref="TemplateId"/> routes it to the survey branch of the solver); its
    /// <see cref="WorldSurveyRequest"/> crosses the wire as <see cref="SensorConfigPayload"/> (WS-2). STANDING: created once, solved in the
    /// background, read without re-asking; new inputs go through <see cref="Configure"/> (a new epoch, the old answer cleared).</para>
    /// <para>⚠ Live world only (<see cref="EntityRepository"/>), as the danger-area sensor.</para>
    /// </summary>
    public static class WorldSurveyChildSensor
    {
        /// <summary>The survey's asset identity.</summary>
        public const string AssetId = "5c3249e0-0b12-4a6e-9a51-00000c324912";
        /// <summary>The template id its <see cref="EqsSensor.BlueprintId"/> carries.</summary>
        public static readonly uint TemplateId = EqsTemplateRegistry.BlueprintIdOf(Guid.Parse(AssetId));
        /// <summary>WS-8 — an answer older than this is reported stale (3 × the 1 s heartbeat).</summary>
        public const float StaleAfterSeconds = 3f;

        /// <summary>The current run's survey at <paramref name="siteId"/>/<paramref name="key"/> — created on the first call with
        /// <paramref name="request"/>. An existing one is returned as it is (use <see cref="Configure"/> to re-point it).</summary>
        public static Entity Ensure(EntityRepository world, Entity owner, int siteId, WorldSurveyRequest request, long key = 0)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            var existing = EqsChildSensor.Find(world, owner, siteId, key);
            if (!existing.IsNull) return existing;
            Register(world);
            var child = EqsChildSensor.Ensure<WorldSurveyStatus>(world, owner, siteId, Transport(request), key);
            if (child.IsNull) return child;
            world.AddComponent(child, new SensorTag { Kind = SensorModality.WorldSurvey });
            world.SetManagedComponent(child, new WorldSurveyAnswer());
            SetRequest(world, child, request);
            return child;
        }

        /// <summary>Re-point <paramref name="child"/> with a new request: a new epoch, its answer cleared. False when it is not a live
        /// survey.</summary>
        public static bool Configure(EntityRepository world, Entity child, WorldSurveyRequest request)
        {
            if (request is null || child.IsNull || !world.IsAlive(child) || !world.HasComponent<WorldSurveyStatus>(child)) return false;
            SetRequest(world, child, request);
            return EqsChildSensor.Refresh(world, child, Transport(request));
        }

        /// <summary>Drop the sensor (the solver stops; the run's teardown does the same on its own).</summary>
        public static void Release(EntityRepository world, Entity child) => EqsChildSensor.Destroy(world, child);

        /// <summary>The latest answer of <paramref name="child"/>, judged against <paramref name="nowSeconds"/> (the view's sim time).</summary>
        public static WorldSurveyRead Read(ISimulationView view, Entity child, double nowSeconds)
        {
            if (child.IsNull || !view.IsAlive(child) || !view.HasComponent<WorldSurveyStatus>(child)) return default;
            var status = view.GetComponentRO<WorldSurveyStatus>(child);
            if (!status.IsReady) return default;
            var payload = view.HasManagedComponent<WorldSurveyAnswer>(child) ? view.GetManagedComponentRO<WorldSurveyAnswer>(child).Payload : null;
            float age = (float)Math.Max(0.0, nowSeconds - status.LastUpdateTimeSeconds);
            return new WorldSurveyRead(true, age > StaleAfterSeconds, status.Version, age, payload);
        }

        /// <summary>The request as it rides the EQS sensor transport (the band is Normal).</summary>
        public static EqsSensor Transport(WorldSurveyRequest request) => new()
        {
            BlueprintId = TemplateId,
            Epoch       = 1,
            Priority    = (byte)EqsPriorityBand.Normal,
        };

        /// <summary>Register the survey's components (idempotent) — a brain host that never registered them still works.</summary>
        public static void Register(EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<WorldSurveyStatus>()) world.RegisterComponent<WorldSurveyStatus>();
            world.RegisterManagedComponent<WorldSurveyRequest>();
            world.RegisterManagedComponent<WorldSurveyAnswer>();
            world.RegisterManagedComponent<SensorConfigPayload>();
            if (!world.IsComponentTypeRegistered<SensorTag>()) world.RegisterComponent<SensorTag>();
        }

        private static void SetRequest(EntityRepository world, Entity child, WorldSurveyRequest request)
        {
            world.SetManagedComponent(child, request);
            world.SetManagedComponent(child, new SensorConfigPayload { Kind = WorldSurveyRequestJson.Kind, Json = WorldSurveyRequestJson.Encode(request) });
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3249</c> — one answer of a survey sensor, on its way to the brain. Published by the SOLVER (keyed by the unit's network id and
    /// the sensor's part id, <see cref="Observer"/> null — read by the egress and, in a fused world, by <see cref="WorldSurveyApplySystem"/>)
    /// and again by the brain's ingress (<see cref="Observer"/> = the local child sensor).
    /// </summary>
    [DataPolicy(DataPolicy.NoReplay)]
    public sealed class WorldSurveyResultEvent
    {
        /// <summary>The unit's network id (0 = a local-only sensor, keyed by its entity index).</summary>
        public long ParentNetworkId;
        /// <summary>The sensor child's part id (or its entity index when local-only).</summary>
        public int LocalChildIndex;
        /// <summary>The sensor's epoch at solve time — a stale answer is dropped.</summary>
        public uint Epoch;
        /// <summary>The solver's tick (0 is replaced by 1 when written, so the answer reads ready).</summary>
        public uint RefreshTick;
        /// <summary>The brain's child sensor, when the ingress resolved it; null on the solver's own publish.</summary>
        public Entity Observer = Entity.Null;
        /// <summary>The answer (<see cref="WorldSurveyPayload"/> as JSON — WS-11).</summary>
        public string PayloadJson = string.Empty;
        /// <summary><see cref="WorldSurveyRequestJson.Hash"/> of <see cref="PayloadJson"/>.</summary>
        public ulong ContentHash;
    }
}
