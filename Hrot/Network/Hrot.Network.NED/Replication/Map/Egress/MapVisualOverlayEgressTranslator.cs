using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common.Replication.Utils;
using Hrot.NED.Common;
using Hrot.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that publishes <see cref="MapVisualOverlay"/> DDS samples
    /// from the <see cref="EditablePolyline"/> managed component.
    /// </summary>
    public class MapVisualOverlayEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMapVisualOverlay;

        private readonly DdsWriter<MapVisualOverlay> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly HashSet<long> _tracedNetIds = new();
        private readonly long _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<MapVisualOverlay>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        // Targets: EditablePolyline (117)
        private static readonly IReadOnlyList<int> _targetIds =
            new int[] { GlobalComponentIds.EditablePolyline };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public MapVisualOverlayEgressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            long localNodeId)
        {
            _writer = new DdsWriter<MapVisualOverlay>(participant);
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
        }

        // â”€â”€ Ingress (egress-only) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        // â”€â”€ Egress â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .DefaultRoutePublishable()
                .With<SimTransform>()
                .WithManaged<EditablePolyline>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                if (!SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, isUnreliable: false))
                    continue;

                ref readonly var netId   = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var simTr   = ref view.GetComponentRO<SimTransform>(entity);
                var polyline = view.GetManagedComponentRO<EditablePolyline>(entity);

                // ⭐ AQ107 B — Points in EditablePolyline are RELATIVE offsets from SimTransform; the wire carries ABSOLUTE local ENU
                //   positions (the entity's position + the offset; Z is the entity's), so every position on the wire has one meaning.
                var wirePoints = new List<Vec3d>(polyline.Points.Count);
                for (int i = 0; i < polyline.Points.Count; i++)
                {
                    var rel = polyline.Points[i];
                    wirePoints.Add(EnuWire.ToWire(new Vector3(simTr.Position.X + rel.X, simTr.Position.Y + rel.Y, simTr.Position.Z)));
                }

                // Extract the authoritative style to send to the IGs
                string styleJson = string.Empty;
                if (view.HasComponent<MapOverlayStyle>(entity))
                {
                    ref readonly var style = ref view.GetComponentRO<MapOverlayStyle>(entity);
                    // CE-3191: the style's OWN wire form — FromJson reads hex colour strings. Serializing the struct wrote "{}"
                    //   (its colours are fields, its properties [JsonIgnore]), so every overlay arrived on its default style.
                    styleJson = style.ToJson();
                }

                _writer.Write(new MapVisualOverlay
                {
                    EntityId        = netId.Value,
                    PersistenceMode = PersistenceMode.MODE_PERSISTENT,
                    Points          = wirePoints,
                    IsEditable      = true,
                    IsClickable     = true,
                    StyleOverrideJson = styleJson // FIX: Inject the preserved style
                });

                SentSampleCount++;
                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);

                if (_tracedNetIds.Add(netId.Value))
                {
                    FdpLog<MapVisualOverlayEgressTranslator>.Debug(
                        "[Node-{0}] Egress: MapVisualOverlay for NetID={1} points={2}",
                        _localNodeId, netId.Value, polyline.Points.Count);
                }
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId)
        {
            _writer.DisposeInstance(new MapVisualOverlay { EntityId = networkEntityId });
        }
    }
}
