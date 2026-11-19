using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (VA-S3, VA-3; 📄 docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §3 VA-3, §4) — THE light rule, on the node owning a
    /// vehicle's <c>dtVehicleLights</c> (its Brain group — the crew decides): each light group of its type (<see cref="VehicleLightsCatalog"/>) is on by
    /// discipline × darkness × engine — road lights (head / tail) under <see cref="LightDiscipline.Normal"/> when it is DARK (the weather's relative light below
    /// <see cref="DarkBelow"/>, the CE-3229 curve's full-light point, R-3017) and the engine runs (<see cref="HeatSource"/> Idle / Running); the convoy marker
    /// instead under <see cref="LightDiscipline.Convoy"/>; an aircraft's nav + anti-collision whenever its engine runs, day or night, unless blacked out / off.
    /// ⛔ Brake lights are never stored (a reader derives them from deceleration); ⏳ interior, searchlight (an order) and landing (near the ground) are not
    /// switched yet; ⏳ the discipline from an SOP / ROE / order (behaviors) — today it is authored, or ⭐ switched by a station (<see cref="LightsRequestEvent"/>, a
    /// request to this owner — applied at once, the mask with it). Evaluated every <see cref="StepSeconds"/>; writes only on a
    /// change. First sight ADDS the component with its final value (D-31). ⭐ Parallel contract: serial; writes only vehicles whose lights this node owns. 0 B
    /// per steady frame.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class LightRuleSystem : IEcsModuleSystem
    {
        /// <summary>NED's <c>dtVehicleLights</c> — the lights are published under this key (the Brain group). ⚠ The wire states it too (a rail pins both).</summary>
        public const long DescriptorOrdinal = 244;

        /// <summary>Relative light below which road lights go on (the CE-3229 curve is full at 0.3 — R-3017).</summary>
        public const float DarkBelow = 0.3f;

        /// <summary>The rule is evaluated this often (s of sim time).</summary>
        public const double StepSeconds = 0.5;

        private static readonly long LightsKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

        /// <summary>Light switches this node applied (rails, diagnostics).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of light requests applied")]
        public long Requests { get; private set; }

        /// <summary>True when this node writes <paramref name="vehicle"/>'s lights (the <c>dtVehicleLights</c> owner) — the transport's owner test too. ⛔ A ghost
        /// (a replica whose master has not arrived) is never ours.</summary>
        public static bool OwnsLights(EntityRepository repo, Entity vehicle)
        {
            if (!repo.IsAlive(vehicle)) return false;
            if (!(repo.IsComponentTypeRegistered<NetworkAuthority>() && repo.HasComponent<NetworkAuthority>(vehicle))
                && repo.GetLifecycleState(vehicle) == EntityLifecycle.Ghost) return false;
            return Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, vehicle, LightsKey);
        }
        private EntityQuery? _vehicles;
        private EntityRepository? _for;
        [Fdp.Core.WorldDerived("step-gate reset on world change (63); derived from sim time")]
        private long _lastStep = long.MinValue;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<VehicleLights>() || !repo.IsComponentTypeRegistered<TkbIdentity>()) return;
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(repo)) return;   // the recording holds what the owner wrote
            if (!ReferenceEquals(repo, _for) || _vehicles == null)
            {
                _for = repo;
                _vehicles = repo.Query().With<TkbIdentity>().With<SimTransform>().Build();
                _lastStep = long.MinValue;
            }
            if (!repo.HasSingletonUnmanaged<GlobalTime>() || !repo.HasSingletonManaged<ITkbDatabase>() || repo.GetSingletonManaged<ITkbDatabase>() is not { } tkb) return;
            double now = repo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime;
            bool sources = repo.IsComponentTypeRegistered<HeatSource>();
            if (repo.Bus.IsRegistered<LightsRequestEvent>()) ApplyRequests(repo, tkb, now, sources);
            long step = (long)Math.Floor(now / StepSeconds);
            if (step == _lastStep) return;
            _lastStep = step;

            foreach (var v in _vehicles!)
            {
                if (!tkb.TryGetByType(repo.GetComponentRO<TkbIdentity>(v).TkbType, out var type)) continue;
                var lights = VehicleLightsCatalog.Of(type);
                if (lights.Length == 0 || !Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, v, LightsKey)) continue;
                var discipline = repo.HasComponent<VehicleLights>(v) ? repo.GetComponentRO<VehicleLights>(v).Discipline : LightDiscipline.Normal;
                uint mask = Evaluate(repo, v, lights, discipline, sources);

                if (!repo.HasComponent<VehicleLights>(v))
                {
                    repo.AddComponent(v, new VehicleLights { OnMask = mask, Discipline = discipline, Since = now });   // its final value (D-31)
                    continue;
                }
                if (repo.GetComponentRO<VehicleLights>(v).OnMask == mask) continue;
                ref var rw = ref repo.GetComponentRW<VehicleLights>(v);
                rw.OnMask = mask;
                rw.Since = now;
            }
        }

        private static uint Evaluate(EntityRepository repo, Entity v, LightDto[] lights, LightDiscipline discipline, bool sources)
        {
            bool engine = sources && repo.HasComponent<HeatSource>(v) && repo.GetComponentRO<HeatSource>(v).State is HeatSourceState.Idle or HeatSourceState.Running;
            bool dark = Fdp.Toolkit.Perception.Sensors.PerceptionWeather.TrySample(repo, v, out var w) && w.RelativeLight < DarkBelow;
            return MaskOf(lights, discipline, dark, engine);
        }

        /// <summary>⭐ VA-3 — a station's switch: set the discipline of a vehicle whose lights this node owns, and its mask by the rule at once (added with its
        /// final value when the vehicle has no lights yet, D-31). Others' requests were carried to their owner by the transport.</summary>
        private void ApplyRequests(EntityRepository repo, ITkbDatabase tkb, double now, bool sources)
        {
            var events = repo.ReadEvents<LightsRequestEvent>();
            for (int e = 0; e < events.Length; e++)
            {
                ref readonly var req = ref events[e];
                var v = req.Vehicle;
                if (!OwnsLights(repo, v) || !repo.HasComponent<TkbIdentity>(v) || !tkb.TryGetByType(repo.GetComponentRO<TkbIdentity>(v).TkbType, out var type)) continue;
                var lights = VehicleLightsCatalog.Of(type);
                if (lights.Length == 0) continue;
                uint mask = Evaluate(repo, v, lights, req.Discipline, sources);
                Requests++;
                if (!repo.HasComponent<VehicleLights>(v))
                {
                    repo.AddComponent(v, new VehicleLights { OnMask = mask, Discipline = req.Discipline, Since = now });
                    continue;
                }
                ref var rw = ref repo.GetComponentRW<VehicleLights>(v);
                rw.Discipline = req.Discipline;
                if (rw.OnMask != mask) { rw.OnMask = mask; rw.Since = now; }
            }
        }

        /// <summary>⭐ The rule, pure (the test seam): which of <paramref name="lights"/> are on under <paramref name="discipline"/>, darkness and the engine.</summary>
        public static uint MaskOf(LightDto[] lights, LightDiscipline discipline, bool dark, bool engine)
        {
            uint mask = 0;
            if (!engine || discipline is LightDiscipline.Blackout or LightDiscipline.Off) return 0;
            for (int i = 0; i < lights.Length && i < 32; i++)
            {
                bool on = lights[i].Kind switch
                {
                    LightKind.Head or LightKind.Tail => dark && discipline == LightDiscipline.Normal,
                    LightKind.Convoy => dark && discipline == LightDiscipline.Convoy,
                    LightKind.Nav or LightKind.AntiCollision => true,
                    _ => false,   // brake derived; interior, searchlight, landing ⏳
                };
                if (on) mask |= 1u << i;
            }
            return mask;
        }
    }
}
