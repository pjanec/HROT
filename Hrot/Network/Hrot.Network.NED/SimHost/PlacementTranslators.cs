using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Hrot.Map.Common.Replication.Utils;
using Hrot.NED.Common;
using Hrot.NED.Descriptors;
using NodeId = Fdp.Core.NodeId;

namespace Hrot.Network.NED.SimHost
{
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐ MACROS S3a ⑤ (docs/DESIGN_Scenario_Macros.md §14.2 SR-180, R-134) — the placement query's scale-out wire, PathfindingTranslators.cs's
    // shape: Brain egress of the request · Solver ingress · Solver egress of the response · Brain ingress. The in-process events are the
    // MANAGED PlacementRequestBatch / PlacementResponseBatch (lists); the DDS structs carry wire types only, points as ENU offsets from a batch
    // origin (EnuWire) exactly as the path batch does. ⚠ A node that is BOTH Brain and NavigationSolver behaves as the path pair does.
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Brain-side egress: forwards this node's own <see cref="PlacementRequestBatch"/>es (previous frame) to the solver, one sample each.</summary>
    public sealed class PlacementRequestBrainEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<PlacementRequestBatchWire>? _writer;
        private readonly NodeId _localNodeId;

        public long   DescriptorOrdinal => (long)EDescriptorType.dtPlacementRequestBatch;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<PlacementRequestBatchWire>();
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public PlacementRequestBrainEgressTranslator(DdsParticipant? participant, int localNodeId = 0)
        {
            _localNodeId = (NodeId)(localNodeId);
            _writer      = participant != null ? new DdsWriter<PlacementRequestBatchWire>(participant) : null;
        }

        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;
            var requests = view.ReadManagedEvents<PlacementRequestBatch>();
            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i].SourceNodeId != _localNodeId) continue;   // authority filter: only this node's own
                _writer.Write(ToWire(requests[i]));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form of <paramref name="req"/> (the test seam): points relative to the first slot's point (or the origin when empty).</summary>
        internal static PlacementRequestBatchWire ToWire(PlacementRequestBatch req)
        {
            var anchor = req.Slots.Count > 0 ? req.Slots[0].Point : Vector3.Zero;
            var slots = new List<DdsPlacementSlot>(req.Slots.Count);
            foreach (var s in req.Slots)
                slots.Add(new DdsPlacementSlot
                {
                    Key = s.Key,
                    Point = new RelativeVector3 { East = s.Point.X - anchor.X, North = s.Point.Y - anchor.Y, Up = s.Point.Z - anchor.Z },
                    Layer = (byte)s.Layer,
                    Radius = s.Radius,
                });
            return new PlacementRequestBatchWire
            {
                SourceNodeId = req.SourceNodeId, RequestId = req.RequestId, MaxShift = req.MaxShift,
                BatchOrigin = EnuWire.ToWire(anchor), Slots = slots,
            };
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }

    /// <summary>Solver-side ingress: turns a received <see cref="PlacementRequestBatchWire"/> into the in-process managed request the solver reads.</summary>
    public sealed class PlacementRequestSolverIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private readonly DdsReader<PlacementRequestBatchWire>? _reader;

        public long   DescriptorOrdinal => (long)EDescriptorType.dtPlacementRequestBatch;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<PlacementRequestBatchWire>();
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public PlacementRequestSolverIngressTranslator(DdsParticipant? participant)
            => _reader = participant != null ? new DdsReader<PlacementRequestBatchWire>(participant) : null;

        public int DiscardIngress() => _reader.DiscardAll();

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                if (cmd is EntityCommandBuffer ecb) ecb.PublishManagedEvent(FromWire(sample.Data));   // the concrete buffer carries managed events (EqsSolverSystem's pattern)
            }
        }

        /// <summary>The in-process request for <paramref name="wire"/> (the test seam).</summary>
        internal static PlacementRequestBatch FromWire(in PlacementRequestBatchWire wire)
        {
            var anchor = EnuWire.FromWire(wire.BatchOrigin);
            var req = new PlacementRequestBatch { RequestId = wire.RequestId, SourceNodeId = (NodeId)(wire.SourceNodeId), MaxShift = wire.MaxShift };
            if (wire.Slots != null)
                foreach (var s in wire.Slots)
                    req.Slots.Add(new PlacementSlot
                    {
                        Key = s.Key,
                        Point = anchor + new Vector3(s.Point.East, s.Point.North, s.Point.Up),
                        Layer = (PlacementLayer)s.Layer,
                        Radius = s.Radius,
                    });
            return req;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }

    /// <summary>Solver-side egress: sends each <see cref="PlacementResponseBatch"/> (previous frame) to the node that asked.</summary>
    public sealed class PlacementResponseSolverEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<PlacementResponseBatchWire>? _writer;

        public long   DescriptorOrdinal => (long)EDescriptorType.dtPlacementResponseBatch;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<PlacementResponseBatchWire>();
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public PlacementResponseSolverEgressTranslator(DdsParticipant? participant)
            => _writer = participant != null ? new DdsWriter<PlacementResponseBatchWire>(participant) : null;

        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;
            var responses = view.ReadManagedEvents<PlacementResponseBatch>();
            for (int i = 0; i < responses.Count; i++)
            {
                _writer.Write(ToWire(responses[i]));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form of <paramref name="resp"/> (the test seam): points relative to the first answer's point.</summary>
        internal static PlacementResponseBatchWire ToWire(PlacementResponseBatch resp)
        {
            var anchor = resp.Answers.Count > 0 ? resp.Answers[0].Point : Vector3.Zero;
            var answers = new List<DdsPlacementAnswer>(resp.Answers.Count);
            foreach (var a in resp.Answers)
                answers.Add(new DdsPlacementAnswer
                {
                    Key = a.Key,
                    Outcome = (byte)a.Outcome,
                    Point = new RelativeVector3 { East = a.Point.X - anchor.X, North = a.Point.Y - anchor.Y, Up = a.Point.Z - anchor.Z },
                    Reason = a.Reason ?? string.Empty,
                });
            return new PlacementResponseBatchWire
            {
                TargetNodeId = resp.SourceNodeId, RequestId = resp.RequestId, BatchOrigin = EnuWire.ToWire(anchor), Answers = answers,
            };
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }

    /// <summary>Brain-side ingress: publishes the in-process managed <see cref="PlacementResponseBatch"/> addressed to this node (or broadcast).</summary>
    public sealed class PlacementResponseBrainIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private readonly DdsReader<PlacementResponseBatchWire>? _reader;
        private readonly NodeId _localNodeId;

        public long   DescriptorOrdinal => (long)EDescriptorType.dtPlacementResponseBatch;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<PlacementResponseBatchWire>();
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public PlacementResponseBrainIngressTranslator(DdsParticipant? participant, int localNodeId = 0)
        {
            _localNodeId = (NodeId)(localNodeId);
            _reader      = participant != null ? new DdsReader<PlacementResponseBatchWire>(participant) : null;
        }

        public int DiscardIngress() => _reader.DiscardAll();

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                // Network routing firewall: only responses addressed to this node or broadcast.
                if (sample.Data.TargetNodeId != _localNodeId && sample.Data.TargetNodeId != 0) continue;
                if (cmd is EntityCommandBuffer ecb) ecb.PublishManagedEvent(FromWire(sample.Data));   // the concrete buffer carries managed events (EqsSolverSystem's pattern)
            }
        }

        /// <summary>The in-process response for <paramref name="wire"/> (the test seam).</summary>
        internal static PlacementResponseBatch FromWire(in PlacementResponseBatchWire wire)
        {
            var anchor = EnuWire.FromWire(wire.BatchOrigin);
            var resp = new PlacementResponseBatch { RequestId = wire.RequestId, SourceNodeId = (NodeId)(wire.TargetNodeId) };
            if (wire.Answers != null)
                foreach (var a in wire.Answers)
                    resp.Answers.Add(new PlacementAnswer
                    {
                        Key = a.Key,
                        Outcome = (PlacementOutcome)a.Outcome,
                        Point = anchor + new Vector3(a.Point.East, a.Point.North, a.Point.Up),
                        Reason = a.Reason ?? string.Empty,
                    });
            return resp;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
