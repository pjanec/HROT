using System;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Squad.Command;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.Translators
{
    /// <summary>
    /// ⭐ <b><c>CE-3250</c> (BR-3) — a report raised on this node leaves it.</b> 📄 <c>docs/designs/group-maneuvers/DESIGN_Building_Clearing.md</c>
    /// §3, §5b. Drains the local <see cref="SquadReportNotification"/>s this node raised (<see cref="SquadReportNotification.OriginNodeId"/> 0 — one that
    /// ARRIVED is never sent again) and writes one <see cref="SquadReport"/> each, the units as network ids. ⛔ A report naming an entity
    /// with no network id is REFUSED (<see cref="RefusedCount"/>) — it could not be read anywhere else.
    /// Installed by <c>SharedTranslatorPack</c>: any node may raise a report (a staff on a brain node, a synthetic one over HTTP).
    /// </summary>
    public sealed class SquadReportEgressTranslator : IDescriptorTranslator
    {
        internal const string DdsTopicName = "SquadReport";

        private readonly IDdsWriter<SquadReport> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtSquadReport;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Reports refused because a unit / subject had no network id.</summary>
        public long RefusedCount { get; private set; }

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public SquadReportEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap, long localNodeId)
            : this(new DdsWriterAdapter<SquadReport>(participant, DdsTopicName), entityMap, localNodeId)
        {
        }

        /// <summary>Testable constructor — accepts an injected writer.</summary>
        internal SquadReportEgressTranslator(IDdsWriter<SquadReport> writer, NetworkEntityMap entityMap, long localNodeId)
        {
            _writer      = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap   = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (view is EntityRepository repo && !repo.Bus.IsRegisteredManaged<SquadReportNotification>()) return;
            foreach (var r in view.ReadManagedEvents<SquadReportNotification>())
            {
                if (r is null || r.OriginNodeId != 0) continue;   // it arrived from another node
                if (!_entityMap.TryGetNetworkId(r.FromUnit, out long from)
                    || !_entityMap.TryGetNetworkId(r.ToUnit, out long to)
                    || !NetIdOrNone(r.Subject, out long subject))
                {
                    RefusedCount++;
                    continue;
                }
                _writer.Write(ToWire(r, from, to, subject, _localNodeId));
                SentSampleCount++;
            }
        }

        private bool NetIdOrNone(Entity e, out long id)
        {
            id = 0;
            return e.IsNull || _entityMap.TryGetNetworkId(e, out id);
        }

        internal static SquadReport ToWire(SquadReportNotification n, long from, long to, long subject, long origin)
        {
            ref readonly var r = ref n.Report;
            return new SquadReport
            {
                FromUnit = from, ToUnit = to, OriginNodeId = origin, Subject = subject, Building = n.Building, SentAt = n.SentAt,
                Kind = (byte)r.Kind, Subtype = r.Subtype, AtX = r.At.X, AtY = r.At.Y, AtZ = r.At.Z, Radius = r.Radius, Count = r.Count,
                Size = (byte)r.Size, Activity = (byte)r.Activity, Equipment = (byte)r.Equipment, Strength = (byte)r.Strength,
                Ammo = (byte)r.Ammo, Wia = r.Wia, Kia = r.Kia, Phase = r.Phase, ObservedAt = r.ObservedAt, Seq = r.Seq, Synthetic = r.Synthetic,
            };
        }

        /// <summary>The wire's report back as behaviors' <see cref="CommandReport"/>.</summary>
        internal static CommandReport FromWire(in SquadReport w) => new()
        {
            Kind = (ReportKind)w.Kind, Subtype = w.Subtype, At = new Vector3(w.AtX, w.AtY, w.AtZ), Radius = w.Radius, Count = w.Count,
            Size = (ElementSize)w.Size, Activity = (ElementActivity)w.Activity, Equipment = (ElementEquipment)w.Equipment,
            Strength = (StatusColour)w.Strength, Ammo = (StatusColour)w.Ammo, Wia = w.Wia, Kia = w.Kia, Phase = w.Phase,
            ObservedAt = w.ObservedAt, Seq = w.Seq, Synthetic = w.Synthetic,
        };

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// ⭐ <b><c>CE-3250</c> (BR-3) — a report raised on another node arrives here.</b> Raises the same local <see cref="SquadReportNotification"/>
    /// (units as this node's entities, <see cref="SquadReportNotification.OriginNodeId"/> = the sender's node) for the staff and the log to read.
    /// Skips this node's own samples. A unit not known here yet is read as null (the report still arrives — a log can show it).
    /// An unknown kind still arrives and is counted (<see cref="UnknownKindCount"/>). Installed by <c>SharedTranslatorPack</c> (every node).
    /// </summary>
    public sealed class SquadReportIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private readonly DdsReader<SquadReport>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;

        public string TopicName         => SquadReportEgressTranslator.DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtSquadReport;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        /// <summary>Reports whose kind this build does not know (a newer sender) — raised anyway, and counted.</summary>
        public long UnknownKindCount { get; private set; }

        /// <summary>Production constructor. A <c>null</c> participant makes <see cref="PollIngress"/> a no-op (tests).</summary>
        public SquadReportIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, long localNodeId)
        {
            _reader      = participant is not null ? new DdsReader<SquadReport>(participant, SquadReportEgressTranslator.DdsTopicName) : null;
            _entityMap   = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null || view is not EntityRepository repo) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var data = sample.Data;
                ProcessSample(in data, repo);
            }
        }

        /// <summary>One received report. Returns true when it raised the local event. Exposed for tests (no DDS stack needed).</summary>
        internal bool ProcessSample(in SquadReport report, EntityRepository repo)
        {
            ReceivedSampleCount++;
            if (report.OriginNodeId == _localNodeId) return false;   // our own report — raised here already
            if (report.Kind == 0 || !Enum.IsDefined(typeof(ReportKind), report.Kind))
            {
                UnknownKindCount++;
                Fdp.Core.Logging.FdpLog<SquadReportIngressTranslator>.Warn(
                    "[SquadReport] unknown kind {0} from unit {1} (node {2}) — raised as is.", report.Kind, report.FromUnit, report.OriginNodeId);
            }
            if (!repo.Bus.IsRegisteredManaged<SquadReportNotification>()) repo.Bus.RegisterManaged<SquadReportNotification>();
            repo.Bus.PublishManaged(new SquadReportNotification
            {
                FromUnit = EntityOf(report.FromUnit), ToUnit = EntityOf(report.ToUnit),
                Subject = report.Subject == 0 ? Entity.Null : EntityOf(report.Subject), Building = report.Building, SentAt = report.SentAt,
                Report = SquadReportEgressTranslator.FromWire(in report), OriginNodeId = report.OriginNodeId,
            });
            return true;
        }

        private Entity EntityOf(long netId) => _entityMap.TryGetEntity(netId, out var e) ? e : Entity.Null;

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
