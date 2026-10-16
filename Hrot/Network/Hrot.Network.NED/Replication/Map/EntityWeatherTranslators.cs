using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Weather;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ CE-3216 S1 (R-3012; 📄 docs/designs/weather/DESIGN_Weather_And_Time.md §1) — publishes AUTHORED weather (<see cref="WeatherConditions"/>:
    /// the World Environment's global weather, every Weather Region's) from the node owning its <c>dtWeather</c>, on change. Same shape as
    /// <see cref="EntityScenarioClockEgressTranslator"/>.
    /// </summary>
    public sealed class EntityWeatherEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityWeather";
        private static readonly int[] s_targetIds = { GlobalComponentIds.WeatherConditions };
        private readonly DdsWriter<EntityWeather>? _writer;
        private readonly Dictionary<long, EntityWeather> _last = new();
        private int _worldEpoch;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtWeather;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityWeatherEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityWeather>(participant, DdsTopicName) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run (the topic is TransientLocal).
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityWeather { EntityId = (int)id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<WeatherConditions>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var e in view.Query().With<WeatherConditions>().With<NetworkIdentity>().Build())
            {
                if (!view.HasAuthority(e, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var sample = ToSample(netId, view.GetComponentRO<WeatherConditions>(e));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static unsafe EntityWeather ToSample(long netId, WeatherConditions w)
        {
            var s = new EntityWeather
            {
                EntityId = (int)netId, GroundTemperatureC = w.GroundTemperatureC, LapseRateKPerKm = w.LapseRateKPerKm,
                SeaLevelPressureHpa = w.SeaLevelPressureHpa, RelativeHumidity = w.RelativeHumidity, VisibilityM = w.VisibilityM,
                FogTopM = w.FogTopM, FogVisibilityM = w.FogVisibilityM, Precipitation = w.Precipitation,
                PrecipitationMmPerHour = w.PrecipitationMmPerHour, FeatherM = w.FeatherM, WindCount = w.WindCount, CloudCount = w.CloudCount,
            };
            s.W0Alt = w.WindAltitudeM[0]; s.W0From = w.WindFromDeg[0]; s.W0Speed = w.WindSpeedMps[0]; s.W0Gust = w.WindGustMps[0];
            s.W1Alt = w.WindAltitudeM[1]; s.W1From = w.WindFromDeg[1]; s.W1Speed = w.WindSpeedMps[1]; s.W1Gust = w.WindGustMps[1];
            s.W2Alt = w.WindAltitudeM[2]; s.W2From = w.WindFromDeg[2]; s.W2Speed = w.WindSpeedMps[2]; s.W2Gust = w.WindGustMps[2];
            s.W3Alt = w.WindAltitudeM[3]; s.W3From = w.WindFromDeg[3]; s.W3Speed = w.WindSpeedMps[3]; s.W3Gust = w.WindGustMps[3];
            s.W4Alt = w.WindAltitudeM[4]; s.W4From = w.WindFromDeg[4]; s.W4Speed = w.WindSpeedMps[4]; s.W4Gust = w.WindGustMps[4];
            s.W5Alt = w.WindAltitudeM[5]; s.W5From = w.WindFromDeg[5]; s.W5Speed = w.WindSpeedMps[5]; s.W5Gust = w.WindGustMps[5];
            s.W6Alt = w.WindAltitudeM[6]; s.W6From = w.WindFromDeg[6]; s.W6Speed = w.WindSpeedMps[6]; s.W6Gust = w.WindGustMps[6];
            s.W7Alt = w.WindAltitudeM[7]; s.W7From = w.WindFromDeg[7]; s.W7Speed = w.WindSpeedMps[7]; s.W7Gust = w.WindGustMps[7];
            s.C0Base = w.CloudBaseM[0]; s.C0Thick = w.CloudThicknessM[0]; s.C0Octas = w.CloudOctas[0]; s.C0Kind = w.CloudKind[0];
            s.C1Base = w.CloudBaseM[1]; s.C1Thick = w.CloudThicknessM[1]; s.C1Octas = w.CloudOctas[1]; s.C1Kind = w.CloudKind[1];
            s.C2Base = w.CloudBaseM[2]; s.C2Thick = w.CloudThicknessM[2]; s.C2Octas = w.CloudOctas[2]; s.C2Kind = w.CloudKind[2];
            s.C3Base = w.CloudBaseM[3]; s.C3Thick = w.CloudThicknessM[3]; s.C3Octas = w.CloudOctas[3]; s.C3Kind = w.CloudKind[3];
            return s;
        }

        /// <summary>The component a sample carries (the test seam; the ingress applies it).</summary>
        internal static unsafe WeatherConditions FromSample(in EntityWeather s)
        {
            var w = new WeatherConditions
            {
                GroundTemperatureC = s.GroundTemperatureC, LapseRateKPerKm = s.LapseRateKPerKm, SeaLevelPressureHpa = s.SeaLevelPressureHpa,
                RelativeHumidity = s.RelativeHumidity, VisibilityM = s.VisibilityM, FogTopM = s.FogTopM, FogVisibilityM = s.FogVisibilityM,
                Precipitation = s.Precipitation, PrecipitationMmPerHour = s.PrecipitationMmPerHour, FeatherM = s.FeatherM,
                WindCount = s.WindCount, CloudCount = s.CloudCount,
            };
            w.WindAltitudeM[0] = s.W0Alt; w.WindFromDeg[0] = s.W0From; w.WindSpeedMps[0] = s.W0Speed; w.WindGustMps[0] = s.W0Gust;
            w.WindAltitudeM[1] = s.W1Alt; w.WindFromDeg[1] = s.W1From; w.WindSpeedMps[1] = s.W1Speed; w.WindGustMps[1] = s.W1Gust;
            w.WindAltitudeM[2] = s.W2Alt; w.WindFromDeg[2] = s.W2From; w.WindSpeedMps[2] = s.W2Speed; w.WindGustMps[2] = s.W2Gust;
            w.WindAltitudeM[3] = s.W3Alt; w.WindFromDeg[3] = s.W3From; w.WindSpeedMps[3] = s.W3Speed; w.WindGustMps[3] = s.W3Gust;
            w.WindAltitudeM[4] = s.W4Alt; w.WindFromDeg[4] = s.W4From; w.WindSpeedMps[4] = s.W4Speed; w.WindGustMps[4] = s.W4Gust;
            w.WindAltitudeM[5] = s.W5Alt; w.WindFromDeg[5] = s.W5From; w.WindSpeedMps[5] = s.W5Speed; w.WindGustMps[5] = s.W5Gust;
            w.WindAltitudeM[6] = s.W6Alt; w.WindFromDeg[6] = s.W6From; w.WindSpeedMps[6] = s.W6Speed; w.WindGustMps[6] = s.W6Gust;
            w.WindAltitudeM[7] = s.W7Alt; w.WindFromDeg[7] = s.W7From; w.WindSpeedMps[7] = s.W7Speed; w.WindGustMps[7] = s.W7Gust;
            w.CloudBaseM[0] = s.C0Base; w.CloudThicknessM[0] = s.C0Thick; w.CloudOctas[0] = s.C0Octas; w.CloudKind[0] = s.C0Kind;
            w.CloudBaseM[1] = s.C1Base; w.CloudThicknessM[1] = s.C1Thick; w.CloudOctas[1] = s.C1Octas; w.CloudKind[1] = s.C1Kind;
            w.CloudBaseM[2] = s.C2Base; w.CloudThicknessM[2] = s.C2Thick; w.CloudOctas[2] = s.C2Octas; w.CloudKind[2] = s.C2Kind;
            w.CloudBaseM[3] = s.C3Base; w.CloudThicknessM[3] = s.C3Thick; w.CloudOctas[3] = s.C3Octas; w.CloudKind[3] = s.C3Kind;
            return w;
        }

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ CE-3216 S1 — applies published weather on every node that does not own it; a sample for an entity this node does not know yet is
    /// HELD (the newest per entity). The recorded owner never takes its own sample back (S8 / F-5).
    /// </summary>
    public sealed class EntityWeatherIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityWeather";
        private const long OrdinalValue = (long)EDescriptorType.dtWeather;
        private readonly DdsReader<EntityWeather>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<long, EntityWeather> _pending = new();
        private readonly List<long> _done = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public int PendingCount => _pending.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityWeatherIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityWeather>(participant, DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is not null)
            {
                using var loan = _reader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                    ReceivedSampleCount++;
                    _pending[sample.Data.EntityId] = sample.Data;
                }
            }
            Flush(cmd, view);
        }

        internal void Receive(in EntityWeather d) { ReceivedSampleCount++; _pending[d.EntityId] = d; }

        internal void Flush(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_pending.Count == 0) return;
            foreach (var (id, d) in _pending)
                if (TryApply(in d, cmd, view)) _done.Add(id);
            foreach (var id in _done) _pending.Remove(id);
            _done.Clear();
        }

        private bool TryApply(in EntityWeather d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var e) || !view.IsAlive(e)) return false;
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;   // ours: drop
            cmd.AddComponent(e, Hrot.Map.Common.Replication.Egress.EntityWeatherEgressTranslator.FromSample(in d));
            return true;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) => _pending.Remove(networkEntityId);
    }
}
