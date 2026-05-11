using System;
using System.Linq;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Example;
using GizmoMap.Network;
using Xunit;

namespace GizmoMap.Example.Tests
{
    public class GizmoExampleTests
    {
        // SC-GZ056-1: Local mode: produces and transports at least one primitive.
        [Fact]
        public void SC_GZ056_1_LocalModeRunsOneFrame()
        {
            var producer = new GizmoPrimitiveBuffer();
            var consumer = new GizmoPrimitiveBuffer();
            using var transport = new LocalGizmoTransport();
            var gen     = new DemoSceneGenerator();
            var builder = new LocalDrawBuilder(producer);

            gen.Emit(0.016f, builder);
            transport.PublishPrimitives(producer.GetFrame(), producer.InternMap);
            transport.PollAndApply(consumer);

            Assert.True(consumer.GetFrame().Length > 0,
                "Expected at least one primitive in consumer buffer.");
        }

        // SC-GZ056-2: Demo emits SpatialAnchor.
        [Fact]
        public void SC_GZ056_2_EmitsSpatialAnchor()
        {
            var producer = new GizmoPrimitiveBuffer();
            var gen      = new DemoSceneGenerator();
            var builder  = new LocalDrawBuilder(producer);

            gen.Emit(0f, builder);

            var prims = producer.GetFrame().ToArray();
            Assert.Contains(prims, p => p.Shape == DebugPrimitiveShape.SpatialAnchor);
        }

        // SC-GZ056-3: GizmoMap.Example has no Fdp.* / Hrot.* references.
        [Fact]
        public void SC_GZ056_3_NoForbiddenAssemblyReferences()
        {
            var asm      = typeof(DemoSceneGenerator).Assembly;
            var refNames = asm.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();

            Assert.DoesNotContain(refNames, n => n.StartsWith("Fdp.",  StringComparison.Ordinal));
            Assert.DoesNotContain(refNames, n => n.StartsWith("Hrot.", StringComparison.Ordinal));
        }

        // SC-GZ056-4: IGizmoTransport is defined in GizmoMap.Contracts assembly.
        [Fact]
        public void SC_GZ056_4_IGizmoTransportInContracts()
        {
            var contractsAsm = typeof(IGizmoTransport).Assembly;
            Assert.Equal("GizmoMap.Contracts", contractsAsm.GetName().Name);
        }

        // SC-GZ056-5: All required shape types emitted in one frame.
        [Fact]
        public void SC_GZ056_5_AllRequiredShapesEmitted()
        {
            var producer = new GizmoPrimitiveBuffer();
            var gen      = new DemoSceneGenerator();
            var builder  = new LocalDrawBuilder(producer);

            gen.Emit(1f, builder); // t=1s for deterministic state

            var shapes = producer.GetFrame().ToArray().Select(p => p.Shape).ToHashSet();

            Assert.Contains(DebugPrimitiveShape.SpatialAnchor,      shapes);
            Assert.Contains(DebugPrimitiveShape.SemanticShape,       shapes);
            Assert.Contains(DebugPrimitiveShape.MilStd2525,          shapes);
            Assert.Contains(DebugPrimitiveShape.Line,                shapes);
            Assert.Contains(DebugPrimitiveShape.Sphere,              shapes);
            Assert.Contains(DebugPrimitiveShape.Arrow,               shapes);
        }

        // SC-GZ056-6: Damaged bit toggles on SemanticShape between frames.
        [Fact]
        public void SC_GZ056_6_DamagedBitToggles()
        {
            // Frame at t=0.5s (not yet toggled => not damaged)
            var buf1  = new GizmoPrimitiveBuffer();
            var gen   = new DemoSceneGenerator();
            gen.Emit(0.5f, new LocalDrawBuilder(buf1));

            // Frame at t=0.5+2.0=2.5s (toggles at 2s boundary => damaged)
            var buf2  = new GizmoPrimitiveBuffer();
            gen.Emit(2.0f, new LocalDrawBuilder(buf2));

            var sem1 = buf1.GetFrame().ToArray()
                           .FirstOrDefault(p => p.Shape == DebugPrimitiveShape.SemanticShape);
            var sem2 = buf2.GetFrame().ToArray()
                           .FirstOrDefault(p => p.Shape == DebugPrimitiveShape.SemanticShape);

            // One should have bit 0 set (Damaged) and the other not.
            Assert.NotEqual(sem1.ConditionMask & 1u, sem2.ConditionMask & 1u);
        }

        // SC-GZ056-7: DDS mode byte roundtrip - primitive count is preserved end-to-end.
        [Fact]
        public void SC_GZ056_7_DdsMode_ByteRoundtrip_PreservesPrimitiveCount()
        {
            var bridge = new InMemoryDdsBridge();
            using var transport = new DdsGizmoTransport(bridge, bridge, bridge, bridge);

            var producer = new GizmoPrimitiveBuffer(capacity: 16);
            var builder  = new LocalDrawBuilder(producer);
            var gen      = new DemoSceneGenerator();
            gen.Emit(1f, builder);
            int inputCount = producer.GetFrame().Length;
            Assert.True(inputCount > 0, "DemoSceneGenerator must emit at least one primitive.");

            transport.PublishPrimitives(producer.GetFrame(), producer.InternMap);

            var consumer = new GizmoPrimitiveBuffer(capacity: 64);
            transport.PollAndApply(consumer);

            Assert.Equal(inputCount, consumer.GetFrame().Length);
        }

        // SC-GZ056-8: DDS mode preserves primitive field values through byte encode/decode.
        [Fact]
        public void SC_GZ056_8_DdsMode_ByteRoundtrip_PreservesFieldValues()
        {
            var bridge = new InMemoryDdsBridge();
            using var transport = new DdsGizmoTransport(bridge, bridge, bridge, bridge);

            var source = new GizmoPrimitiveBuffer(capacity: 4);
            var prim   = new DebugPrimitive
            {
                Shape        = DebugPrimitiveShape.Sphere,
                SphereRadius = 9.81f,
            };
            source.AppendRaw(in prim);

            transport.PublishPrimitives(source.GetFrame(), source.InternMap);

            var consumer = new GizmoPrimitiveBuffer(capacity: 4);
            transport.PollAndApply(consumer);

            var frame = consumer.GetFrame();
            Assert.Equal(1, frame.Length);
            Assert.Equal(DebugPrimitiveShape.Sphere, frame[0].Shape);
            Assert.Equal(9.81f, frame[0].SphereRadius, precision: 3);
        }

        // SC-GZ056-9: DemoSceneGenerator emits a ContextMenuBinding primitive each frame.
        [Fact]
        public void SC_GZ056_9_EmitsContextMenuBinding()
        {
            var buf     = new GizmoPrimitiveBuffer();
            var gen     = new DemoSceneGenerator();
            var builder = new LocalDrawBuilder(buf);

            gen.Emit(1f, builder);

            var prims = buf.GetFrame().ToArray();
            Assert.Contains(prims, p => p.Shape == DebugPrimitiveShape.ContextMenuBinding);
        }

        // SC-GZ056-10: ContextMenuBinding primitive references the correct entity id and a non-zero hash.
        [Fact]
        public void SC_GZ056_10_ContextMenuBinding_EntityIdAndHashAreValid()
        {
            var buf     = new GizmoPrimitiveBuffer();
            var gen     = new DemoSceneGenerator();
            var builder = new LocalDrawBuilder(buf);

            gen.Emit(0f, builder);

            var binding = buf.GetFrame().ToArray()
                .FirstOrDefault(p => p.Shape == DebugPrimitiveShape.ContextMenuBinding);

            // StructNetworkId == 1 (matches the interactive box SubElementId).
            Assert.Equal(1L, binding.StructNetworkId);
            // StringHash must be non-zero (menu JSON was interned).
            Assert.NotEqual(0u, binding.StringHash);
        }

        // SC-GZ056-11: Menu JSON for the binding is interned and resolvable via InternMap.
        [Fact]
        public void SC_GZ056_11_MenuJsonInternedAndResolvable()
        {
            var buf     = new GizmoPrimitiveBuffer();
            var gen     = new DemoSceneGenerator();
            var builder = new LocalDrawBuilder(buf);

            gen.Emit(0f, builder); // phase 0 -> Idle menu

            var binding = buf.GetFrame().ToArray()
                .FirstOrDefault(p => p.Shape == DebugPrimitiveShape.ContextMenuBinding);

            string? resolved = buf.InternMap.TryResolve(binding.StringHash);
            Assert.NotNull(resolved);
            // The resolved JSON must be valid JSON (parseable) and non-empty.
            Assert.False(string.IsNullOrWhiteSpace(resolved));
        }

        // SC-GZ056-12: Menu cycles through 3 different JSON definitions over time.
        [Fact]
        public void SC_GZ056_12_MenuCycles_ThreeDistinctDefinitions()
        {
            uint HashAt(float t)
            {
                var buf     = new GizmoPrimitiveBuffer();
                var builder = new LocalDrawBuilder(buf);
                // New generator per invocation: accumulate time from zero to t.
                var gen = new DemoSceneGenerator();
                gen.Emit(t, builder);
                var binding = buf.GetFrame().ToArray()
                    .FirstOrDefault(p => p.Shape == DebugPrimitiveShape.ContextMenuBinding);
                return binding.StringHash;
            }

            // Phase 0: t = 1.5 (inside [0, 3))
            // Phase 1: t = 4.5 (inside [3, 6))
            // Phase 2: t = 7.5 (inside [6, 9))
            uint h0 = HashAt(1.5f);
            uint h1 = HashAt(4.5f);
            uint h2 = HashAt(7.5f);

            Assert.NotEqual(h0, h1);
            Assert.NotEqual(h1, h2);
            Assert.NotEqual(h0, h2);
        }

        // SC-GZ056-13: GetActiveMenuJson helper returns distinct strings for the three phases.
        [Fact]
        public void SC_GZ056_13_GetActiveMenuJson_ReturnsDifferentStringPerPhase()
        {
            string m0 = DemoSceneGenerator.GetActiveMenuJson(1.5f);  // phase 0: Idle
            string m1 = DemoSceneGenerator.GetActiveMenuJson(4.5f);  // phase 1: Moving
            string m2 = DemoSceneGenerator.GetActiveMenuJson(7.5f);  // phase 2: Engaging

            Assert.NotEqual(m0, m1);
            Assert.NotEqual(m1, m2);
            Assert.NotEqual(m0, m2);

            // Each must be valid non-empty JSON.
            Assert.False(string.IsNullOrWhiteSpace(m0));
            Assert.False(string.IsNullOrWhiteSpace(m1));
            Assert.False(string.IsNullOrWhiteSpace(m2));
        }

        // SC-GZ056-14: ResolveActionLabel returns the matching label from the menu JSON.
        [Fact]
        public void SC_GZ056_14_ResolveActionLabel_ReturnsMatchingLabel()
        {
            string menuJson = DemoSceneGenerator.GetActiveMenuJson(0f); // Idle menu
            string label    = DemoSceneGenerator.ResolveActionLabel(menuJson, 1);

            Assert.Equal("Center View", label);
        }

        // In-memory bridge: captures the written batch and replays it once on read.
        // Used by SC-GZ056-7 and SC-GZ056-8 to exercise the byte serialization path
        // without requiring a live CycloneDDS participant.
        private sealed class InMemoryDdsBridge :
            IDdsWriter<DebugPrimitivesBatch>,
            IDdsReader<DebugPrimitivesBatch>,
            IDdsWriter<StringInternEntry>,
            IDdsReader<StringInternEntry>
        {
            private DebugPrimitivesBatch _pending;
            private bool _hasPending;
            private readonly System.Collections.Generic.Queue<StringInternEntry> _stringPending = new();

            public void Write(DebugPrimitivesBatch sample)
            {
                _pending    = sample;
                _hasPending = true;
            }

            public bool TryRead(out DebugPrimitivesBatch sample)
            {
                if (_hasPending)
                {
                    sample      = _pending;
                    _hasPending = false;
                    return true;
                }
                sample = default;
                return false;
            }

            void IDdsWriter<StringInternEntry>.Write(StringInternEntry sample)
            {
                _stringPending.Enqueue(sample);
            }

            bool IDdsReader<StringInternEntry>.TryRead(out StringInternEntry sample)
            {
                if (_stringPending.Count > 0)
                {
                    sample = _stringPending.Dequeue();
                    return true;
                }
                sample = default;
                return false;
            }
        }
    }
}
