using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Replication.Utils;
using Hrot.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator for the Hrot <c>MapVisualOverlay</c> DDS topic.
    ///
    /// Applies overlay geometry by creating or updating <see cref="EditablePolyline"/>.
    /// This translator is ingress-only; <see cref="ScanAndPublish"/> is a no-op.
    /// </summary>
    public class MapVisualOverlayIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMapVisualOverlay;

        private readonly DdsReader<MapVisualOverlay>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly long _localNodeId;

        /// <summary>
        /// Overlays deferred because the entity's <see cref="SimTransform"/> had not yet been
        /// applied to the live repository when the DDS sample arrived (same-frame ordering race
        /// with <c>GeoSpatialIngressTranslator</c>).  Re-processed on subsequent
        /// <see cref="PollIngress"/> calls once <c>SimTransform</c> becomes visible.
        /// </summary>
        private readonly Dictionary<Entity, MapVisualOverlay> _pendingOverlays = new();
        private int _worldEpoch;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<MapVisualOverlay>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public MapVisualOverlayIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem,
            long localNodeId)
        {
            _reader = participant is not null ? new DdsReader<MapVisualOverlay>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = localNodeId;
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _pendingOverlays.Clear();   // ⭐ R-3039 S3 — a world replacement (a load, a restore): the handles named the old world (R-3041: lost)
            var repo = view as EntityRepository;

            // Re-process deferred overlays from previous frames whose entity now has SimTransform.
            if (_pendingOverlays.Count > 0 && repo != null)
            {
                var resolved = new List<Entity>(_pendingOverlays.Count);
                foreach (var (entity, overlay) in _pendingOverlays)
                {
                    if (!repo.IsAlive(entity) || IsOwnedHere(repo, entity)) { resolved.Add(entity); continue; }   // S8 / F-5
                    if (!repo.HasComponent<SimTransform>(entity)) continue;
                    ref readonly var t = ref repo.GetComponentRO<SimTransform>(entity);
                    cmd.SetManagedComponent(entity, BuildPolyline(overlay, t.Position));
                    resolved.Add(entity);
                }
                for (int i = 0; i < resolved.Count; i++)
                    _pendingOverlays.Remove(resolved[i]);
            }

            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                if (sample.Info.InstanceState != DdsInstanceState.Alive)
                    continue;

                ReceivedSampleCount++;
                ProcessSample(sample.Data, cmd, repo);
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not MapVisualOverlay overlay)
                return;

            // When descriptors are applied in order (GeoSpatial before MapVisualOverlay),
            // SimTransform is already set and we can reconstruct accurate Cartesian offsets.
            Vector3? entityPos = null;
            if (repo.IsAlive(entity) && repo.HasComponent<SimTransform>(entity))
            {
                ref readonly var t = ref repo.GetComponentRO<SimTransform>(entity);
                entityPos = t.Position;
            }

            repo.SetManagedComponent(entity, BuildPolyline(overlay, entityPos));
            repo.SetComponent(entity, MapOverlayStyle.FromJson(overlay.StyleOverrideJson));
        }

        public void Dispose(long networkEntityId) { }

        private static bool IsOwnedHere(EntityRepository repo, Entity entity)
            => Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.IsRecordedOwner(
                repo, entity, Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(OrdinalValue, 0));

        internal void ProcessSample(in MapVisualOverlay data, IEntityCommandBuffer cmd, EntityRepository? repo)
        {
            long netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (repo == null)
                {
                    FdpLog<MapVisualOverlayIngressTranslator>.Warn(
                        "[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }

                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            // ⭐ S8 / F-5 — skip our OWN sample looping back: the recorded owner of this descriptor (the creator — CREATOR
            //   group) edits it, and the value it last published must not overwrite a newer local edit. ⛔ Recorded owner
            //   only: a ghost still being built takes the sample. 📄 docs/DESIGN_Ownership_Groups_And_Grants.md §3 F-5, §5.6 S8.
            if (repo != null && IsOwnedHere(repo, entity))
                return;

            // Pass the entity's position so BuildPolyline can make the wire's ABSOLUTE local points entity-relative.
            Vector3? entityPos = null;
            if (repo != null && repo.IsAlive(entity) && repo.HasComponent<SimTransform>(entity))
            {
                ref readonly var t = ref repo.GetComponentRO<SimTransform>(entity);
                entityPos = t.Position;
            }

            // When SimTransform is not yet in the live repo (same-frame ordering race with
            // GeoSpatialIngressTranslator), defer the polyline rebuild to the next frame.
            // Always apply MapOverlayStyle immediately since it is position-independent.
            if (entityPos == null && data.Points != null && data.Points.Count > 0)
            {
                _pendingOverlays[entity] = data;
            }
            else
            {
                cmd.SetManagedComponent(entity, BuildPolyline(data, entityPos));
            }
            cmd.SetComponent(entity, MapOverlayStyle.FromJson(data.StyleOverrideJson));
        }

        /// <param name="entityPos">
        /// The entity's position (<see cref="SimTransform.Position"/>). ⭐ AQ107 B — the wire's points are ABSOLUTE local ENU metres and
        /// the ECS polyline is entity-relative, so each point becomes <c>point.XY − entityPos.XY</c>. When <c>null</c> (SimTransform not
        /// yet available — only the descriptor-apply path), the entity is taken to sit at the local origin and the points are the offsets.
        /// </param>
        private EditablePolyline BuildPolyline(in MapVisualOverlay overlay, Vector3? entityPos = null)
        {
            var polyline = new EditablePolyline();
            if (overlay.Points == null || overlay.Points.Count == 0)
                return polyline;

            var reference = entityPos ?? Vector3.Zero;
            var points = new List<Vector2>(overlay.Points.Count);
            for (int i = 0; i < overlay.Points.Count; i++)
            {
                var abs = EnuWire.FromWire(overlay.Points[i]);
                points.Add(new Vector2(abs.X - reference.X, abs.Y - reference.Y));
            }

            return polyline with { Points = points };   // CE-4046 — immutable record
        }
    }
}
