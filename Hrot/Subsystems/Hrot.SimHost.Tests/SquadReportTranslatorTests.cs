using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Squad.Command;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Hrot.Network.Translators;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3250</c> (behaviors' BR-3 + the report-ageing note; docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §3, §5b, §5d)
    /// — a report raised on one node arrives on every other node, once, with every field; one about an entity with no network id is
    /// refused at egress and counted; an unknown kind still crosses and is counted; a node skips its own sample; replay records it.
    /// </summary>
    public sealed class SquadReportTranslatorTests : IDisposable
    {
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private readonly EntityRepository _sender = new(), _receiver = new();
        private readonly NetworkEntityMap _senderMap = new(), _receiverMap = new();

        public SquadReportTranslatorTests()
        {
            _sender.Bus.RegisterManaged<SquadReportNotification>();
            _receiver.Bus.RegisterManaged<SquadReportNotification>();
        }

        public void Dispose() { _sender.Dispose(); _receiver.Dispose(); }

        private (Entity Squad, Entity Platoon, Entity Contact) Units(EntityRepository w, NetworkEntityMap map)
        {
            var squad = w.CreateEntity(); var platoon = w.CreateEntity(); var contact = w.CreateEntity();
            map.Register(11, squad); map.Register(12, platoon); map.Register(13, contact);
            return (squad, platoon, contact);
        }

        private static SquadReportNotification Spot(Entity from, Entity to, Entity subject) => new()
        {
            FromUnit = from, ToUnit = to, Subject = subject, Building = 7, SentAt = 44.0,
            Report = new CommandReport { Kind = ReportKind.Spot, At = new Vector3(120f, 80f, 3f), Count = 2, Radius = 15f,
                Size = ElementSize.Team, Activity = ElementActivity.Firing, Equipment = ElementEquipment.MachineGun, ObservedAt = 41f, Seq = 5 },
        };

        private static SquadReportNotification Of(Entity from, Entity to, CommandReport report) => new() { FromUnit = from, ToUnit = to, Report = report };

        // sender publishes → its egress writes → the receiver's ingress raises
        private SquadReportNotification RoundTrip(SquadReportNotification sent, out CapturingWriter<SquadReport> writer)
        {
            writer = new CapturingWriter<SquadReport>();
            var egress = new SquadReportEgressTranslator(writer, _senderMap, localNodeId: 3);
            _sender.Bus.PublishManaged(sent);
            _sender.Bus.SwapBuffers();
            egress.ScanAndPublish(_sender);
            var ingress = new SquadReportIngressTranslator(null, _receiverMap, localNodeId: 1);
            foreach (var s in writer.Written) Assert.True(ingress.ProcessSample(s, _receiver));
            _receiver.Bus.SwapBuffers();
            return Assert.Single(_receiver.Bus.ReadManaged<SquadReportNotification>());
        }

        [Fact]
        public void CE3250_ASpot_ArrivesOnAnotherNode_WithEveryField_AsThatNodesEntities()
        {
            var (squad, platoon, contact) = Units(_sender, _senderMap);
            var (rSquad, rPlatoon, rContact) = Units(_receiver, _receiverMap);
            var got = RoundTrip(Spot(squad, platoon, contact), out var writer);

            var wire = Assert.Single(writer.Written);
            Assert.Equal((11L, 12L, 13L, 3L), (wire.FromUnit, wire.ToUnit, wire.Subject, wire.OriginNodeId));
            Assert.Equal((rSquad, rPlatoon, rContact), (got.FromUnit, got.ToUnit, got.Subject));
            var r = got.Report;
            Assert.Equal((ReportKind.Spot, 7, new Vector3(120f, 80f, 3f), (byte)2, 15f), (r.Kind, got.Building, r.At, r.Count, r.Radius));
            Assert.Equal((ElementSize.Team, ElementActivity.Firing, ElementEquipment.MachineGun), (r.Size, r.Activity, r.Equipment));
            Assert.Equal((41f, 44.0, 5u, 3L), (r.ObservedAt, got.SentAt, r.Seq, got.OriginNodeId));
            Assert.True(r.ObservedAt < got.SentAt, "a report held 3 s arrives with ObservedAt before SentAt");
        }

        [Fact]
        public void CE3250_ASitrep_ALace_AProgressFireLifted_APhaseLine_AndASyntheticRequest_RoundTrip()
        {
            var (squad, platoon, _) = Units(_sender, _senderMap);
            Units(_receiver, _receiverMap);

            var sitrep = RoundTrip(Of(squad, platoon, new CommandReport { Kind = ReportKind.Sitrep, Strength = StatusColour.Amber,
                Ammo = StatusColour.Red, Phase = 3, Seq = 1 }), out _);
            Assert.Equal((StatusColour.Amber, StatusColour.Red, (ushort)3), (sitrep.Report.Strength, sitrep.Report.Ammo, sitrep.Report.Phase));
            Assert.True(sitrep.Subject.IsNull);

            _receiver.Bus.SwapBuffers();
            var lace = RoundTrip(Of(squad, platoon, new CommandReport { Kind = ReportKind.Lace, Wia = 2, Kia = 1, Seq = 2 }), out _);
            Assert.Equal(((byte)2, (byte)1), (lace.Report.Wia, lace.Report.Kia));

            _receiver.Bus.SwapBuffers();
            var lifted = RoundTrip(Of(squad, platoon, new CommandReport { Kind = ReportKind.Progress, Subtype = (byte)ProgressKind.FireLifted, Seq = 3 }), out _);
            Assert.Equal((byte)ProgressKind.FireLifted, lifted.Report.Subtype);

            _receiver.Bus.SwapBuffers();
            var phase = RoundTrip(Of(squad, platoon, new CommandReport { Kind = ReportKind.Progress, Subtype = (byte)ProgressKind.PhaseLine,
                Count = 2, Seq = 4 }), out _);
            Assert.Equal(((byte)ProgressKind.PhaseLine, (byte)2), (phase.Report.Subtype, phase.Report.Count));

            _receiver.Bus.SwapBuffers();
            var request = RoundTrip(Of(squad, platoon, new CommandReport { Kind = ReportKind.Request, Subtype = (byte)RequestKind.Casevac,
                Synthetic = 1, Seq = 5 }), out _);
            Assert.Equal(((byte)RequestKind.Casevac, (byte)1, 5u), (request.Report.Subtype, request.Report.Synthetic, request.Report.Seq));
        }

        [Fact]
        public void CE3250_AReportAboutAnEntityWithNoNetworkId_IsRefusedAndCounted_AndAnArrivedOneIsNeverSentAgain()
        {
            var (squad, platoon, _) = Units(_sender, _senderMap);
            var stranger = _sender.CreateEntity();   // no network id
            var writer = new CapturingWriter<SquadReport>();
            var egress = new SquadReportEgressTranslator(writer, _senderMap, localNodeId: 3);
            _sender.Bus.PublishManaged(Spot(squad, platoon, stranger));
            _sender.Bus.PublishManaged(new SquadReportNotification { FromUnit = squad, ToUnit = platoon, Report = new CommandReport { Kind = ReportKind.Sitrep }, OriginNodeId = 9 });
            _sender.Bus.SwapBuffers();
            egress.ScanAndPublish(_sender);
            Assert.Empty(writer.Written);
            Assert.Equal(1, egress.RefusedCount);
        }

        [Fact]
        public void CE3250_ANodesOwnSample_IsSkipped_AndAnUnknownKind_StillArrivesAndIsCounted()
        {
            Units(_receiver, _receiverMap);
            var ingress = new SquadReportIngressTranslator(null, _receiverMap, localNodeId: 1);
            Assert.False(ingress.ProcessSample(new SquadReport { FromUnit = 11, ToUnit = 12, OriginNodeId = 1, Kind = 1 }, _receiver));
            Assert.True(ingress.ProcessSample(new SquadReport { FromUnit = 11, ToUnit = 12, OriginNodeId = 4, Kind = 77 }, _receiver));
            _receiver.Bus.SwapBuffers();
            Assert.Equal((ReportKind)77, Assert.Single(_receiver.Bus.ReadManaged<SquadReportNotification>()).Report.Kind);
            Assert.Equal(1, ingress.UnknownKindCount);
        }

        [Fact]
        public void CE3250_ReplayRecordsAReport_AndPlaysItBack()
        {
            string path = Path.Combine(Path.GetTempPath(), "ce3250_" + Guid.NewGuid().ToString("N") + ".fdp");
            try
            {
                using (var fs = File.Create(path))
                using (var writer = new BinaryWriter(fs))
                using (var repo = new EntityRepository())
                using (var bus = new FdpEventBus())
                {
                    writer.Write((uint)1);
                    writer.Write((ulong)0);
                    repo.Tick();
                    bus.PublishManaged(new SquadReportNotification { Building = 4, Report = new CommandReport { Kind = ReportKind.Progress,
                        Subtype = (byte)ProgressKind.RoomClear, At = new Vector3(1f, 2f, 3f), Seq = 8, Synthetic = 1 } });
                    new Fdp.Core.FlightRecorder.RecorderSystem().RecordDeltaFrame(repo, 0, writer, 0L, bus);
                    bus.SwapBuffers();
                }
                using (var fs = File.OpenRead(path))
                using (var reader = new BinaryReader(fs))
                using (var repo = new EntityRepository())
                using (var bus = new FdpEventBus())
                {
                    reader.ReadUInt32();
                    reader.ReadUInt64();
                    new Fdp.Core.FlightRecorder.PlaybackSystem().ApplyFrame(repo, reader, bus);
                    var back = Assert.Single(bus.ReadManaged<SquadReportNotification>());
                    Assert.Equal((ReportKind.Progress, (byte)ProgressKind.RoomClear, 4, 8u, (byte)1),
                                 (back.Report.Kind, back.Report.Subtype, back.Building, back.Report.Seq, back.Report.Synthetic));
                    Assert.Equal(new Vector3(1f, 2f, 3f), back.Report.At);
                }
            }
            finally { File.Delete(path); }
        }
    }
}
