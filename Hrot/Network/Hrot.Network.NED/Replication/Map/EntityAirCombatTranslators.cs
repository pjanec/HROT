using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>⭐ <c>CE-10000</c> (air lane) — an entity reference on the wire: its network id, 0 when none, dead or not networked.</summary>
    internal static class AirCombatWire
    {
        public static int NetIdOf(ISimulationView view, Entity e)
            => !e.IsNull && view.IsAlive(e) && view.HasComponent<NetworkIdentity>(e) ? view.GetComponentRO<NetworkIdentity>(e).Value : 0;

        public static float Round(float v, float step) => MathF.Round(v / step) * step;
    }

    /// <summary>⭐ <c>CE-10000</c> (AC-2; docs/designs/air-attack/DESIGN_Air_Combat.md §4b) — publishes a fighter's <see cref="AirCombatSteer"/> from the
    /// owner of its <c>dtAirCombatSteer</c> (the Brain group) on change; the target and the arena go as network ids.</summary>
    public sealed class EntityAirCombatSteerEgressTranslator : IDescriptorTranslator
    {
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityAirCombatSteer> _instances;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityAirCombatSteer>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtAirCombatSteer;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityAirCombatSteerEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityAirCombatSteer>(participant) : null, static key => new EntityAirCombatSteer { EntityId = (int)key });

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<AirCombatSteer>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<AirCombatSteer>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                var sample = ToSample(view, netId, in view.GetComponentRO<AirCombatSteer>(entity));
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        internal static EntityAirCombatSteer ToSample(ISimulationView view, int netId, in AirCombatSteer c) => new()
        {
            EntityId = netId, Manoeuvre = (byte)c.Manoeuvre, Lapse = (byte)c.Lapse,
            Target = AirCombatWire.NetIdOf(view, c.Target), Arena = AirCombatWire.NetIdOf(view, c.Arena),
            PointX = c.Point.X, PointY = c.Point.Y, PointZ = c.Point.Z, Throttle = c.Throttle, FloorAgl = c.FloorAgl, MaxG = c.MaxG,
        };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>⭐ <c>CE-10000</c> (AC-2, AC-9a) — publishes a fighter's <see cref="FighterPilotState"/> from the owner of its <c>dtFighterPilotState</c>
    /// (the Muscle group), on a change of its ROUNDED values (0.1 g, 0.05 throttle, 1 m, 1°, 1 m/s, 0.1 s) — so a steady turn is a few samples a second.</summary>
    public sealed class EntityFighterPilotStateEgressTranslator : IDescriptorTranslator
    {
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityFighterPilotState> _instances;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityFighterPilotState>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtFighterPilotState;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityFighterPilotStateEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityFighterPilotState>(participant) : null, static key => new EntityFighterPilotState { EntityId = (int)key });

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<FighterPilotState>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<FighterPilotState>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                var sample = ToSample(netId, in view.GetComponentRO<FighterPilotState>(entity));
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        internal static EntityFighterPilotState ToSample(int netId, in FighterPilotState s) => new()
        {
            EntityId = netId,
            GLoad = AirCombatWire.Round(s.GLoad, 0.1f), Throttle = AirCombatWire.Round(s.Throttle, 0.05f),
            GcasActive = s.GcasActive, GcasCount = s.GcasCount, MinAgl = MathF.Min(AirCombatWire.Round(s.MinAgl, 1f), 1e9f),
            Inbound = s.Inbound, ThreatBearingDeg = AirCombatWire.Round(s.ThreatBearingDeg, 1f), ThreatSeeker = (byte)s.ThreatSeeker,
            ThreatClosureMps = AirCombatWire.Round(s.ThreatClosureMps, 1f), ThreatTtiS = AirCombatWire.Round(s.ThreatTtiS, 0.1f),
            LastDitchAt = s.LastDitchAt, Defeated = s.Defeated, SpeedMps = AirCombatWire.Round(s.SpeedMps, 1f),
        };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>⭐ <c>CE-10000</c> (AC-10) — publishes an <see cref="AirArena"/> from the owner of its <c>dtAirArena</c> (its creator's Brain) on change.</summary>
    public sealed class EntityAirArenaEgressTranslator : IDescriptorTranslator
    {
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityAirArena> _instances;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityAirArena>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtAirArena;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityAirArenaEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityAirArena>(participant) : null, static key => new EntityAirArena { EntityId = (int)key });

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<AirArena>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<AirArena>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                var sample = ToSample(netId, in view.GetComponentRO<AirArena>(entity));
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        internal static EntityAirArena ToSample(int netId, in AirArena a) => new()
        {
            EntityId = netId, CentreX = a.Centre.X, CentreY = a.Centre.Y, CentreZ = a.Centre.Z, RadiusM = a.RadiusM, FloorAglM = a.FloorAglM,
            CeilingAglM = a.CeilingAglM, MaxShotRangeM = a.MaxShotRangeM, StalemateSeconds = a.StalemateSeconds, LapsesOff = a.LapsesOff, LastHitAt = a.LastHitAt,
        };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-10000</c> — the three air-combat ingresses share one shape: an unknown entity is dropped (the sample is resent on change and is
    /// TransientLocal), the recorded owner never takes its own sample back, the component is added with its final value (D-31).</summary>
    public abstract class AirCombatIngressBase<TSample> : IDescriptorTranslator, IDiscardableIngress where TSample : struct
    {
        private readonly DdsReader<TSample>? _reader;
        protected readonly NetworkEntityMap EntityMap;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<TSample>();
        public abstract long DescriptorOrdinal { get; }
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        protected AirCombatIngressBase(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<TSample>(participant) : null;
            EntityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public int DiscardIngress() => _reader.DiscardAll();

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                ReceivedSampleCount++;
                Apply(sample.Data, cmd, view);
            }
        }

        /// <summary>Applies one sample (the test seam).</summary>
        internal void Apply(in TSample d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!EntityMap.TryGetEntity(KeyOf(in d), out var entity) || !view.IsAlive(entity)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo && AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(DescriptorOrdinal, 0))) return;
            Write(entity, in d, cmd);
        }

        protected abstract int KeyOf(in TSample d);
        protected abstract void Write(Entity entity, in TSample d, IEntityCommandBuffer cmd);

        protected Entity Resolve(int netId) => netId != 0 && EntityMap.TryGetEntity(netId, out var e) ? e : Entity.Null;

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }

    /// <summary>⭐ <c>CE-10000</c> (AC-2) — applies a fighter's <see cref="AirCombatSteer"/> on the node that flies it (and every other).</summary>
    public sealed class EntityAirCombatSteerIngressTranslator : AirCombatIngressBase<EntityAirCombatSteer>
    {
        public EntityAirCombatSteerIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap) : base(participant, entityMap) { }
        public override long DescriptorOrdinal => (long)EDescriptorType.dtAirCombatSteer;
        protected override int KeyOf(in EntityAirCombatSteer d) => d.EntityId;
        protected override void Write(Entity entity, in EntityAirCombatSteer d, IEntityCommandBuffer cmd) => cmd.AddComponent(entity, new AirCombatSteer
        {
            Manoeuvre = (Manoeuvre)d.Manoeuvre, Lapse = (PilotLapse)d.Lapse, Target = Resolve(d.Target), Arena = Resolve(d.Arena),
            Point = new Vector3(d.PointX, d.PointY, d.PointZ), Throttle = d.Throttle, FloorAgl = d.FloorAgl, MaxG = d.MaxG,
        });
    }

    /// <summary>⭐ <c>CE-10000</c> (AC-2, AC-9a) — applies a fighter's <see cref="FighterPilotState"/> on every node that does not fly it (its brain reads it).</summary>
    public sealed class EntityFighterPilotStateIngressTranslator : AirCombatIngressBase<EntityFighterPilotState>
    {
        public EntityFighterPilotStateIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap) : base(participant, entityMap) { }
        public override long DescriptorOrdinal => (long)EDescriptorType.dtFighterPilotState;
        protected override int KeyOf(in EntityFighterPilotState d) => d.EntityId;
        protected override void Write(Entity entity, in EntityFighterPilotState d, IEntityCommandBuffer cmd) => cmd.AddComponent(entity, new FighterPilotState
        {
            GLoad = d.GLoad, Throttle = d.Throttle, GcasActive = d.GcasActive, GcasCount = d.GcasCount, MinAgl = d.MinAgl, Inbound = d.Inbound,
            ThreatBearingDeg = d.ThreatBearingDeg, ThreatSeeker = (GuidanceKind)d.ThreatSeeker, ThreatClosureMps = d.ThreatClosureMps,
            ThreatTtiS = d.ThreatTtiS, LastDitchAt = (SimInstant)d.LastDitchAt, Defeated = d.Defeated, SpeedMps = d.SpeedMps,
        });
    }

    /// <summary>⭐ <c>CE-10000</c> (AC-10) — applies an <see cref="AirArena"/> on every node that did not create it.</summary>
    public sealed class EntityAirArenaIngressTranslator : AirCombatIngressBase<EntityAirArena>
    {
        public EntityAirArenaIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap) : base(participant, entityMap) { }
        public override long DescriptorOrdinal => (long)EDescriptorType.dtAirArena;
        protected override int KeyOf(in EntityAirArena d) => d.EntityId;
        protected override void Write(Entity entity, in EntityAirArena d, IEntityCommandBuffer cmd) => cmd.AddComponent(entity, new AirArena
        {
            Centre = new Vector3(d.CentreX, d.CentreY, d.CentreZ), RadiusM = d.RadiusM, FloorAglM = d.FloorAglM, CeilingAglM = d.CeilingAglM,
            MaxShotRangeM = d.MaxShotRangeM, StalemateSeconds = d.StalemateSeconds, LapsesOff = d.LapsesOff, LastHitAt = (SimInstant)d.LastHitAt,
        });
    }
}
