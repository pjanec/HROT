using System;
using System.Diagnostics;
using Fdp.Kernel;
using FDP.Toolkit.Time;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Messages;
using Fdp.ModuleHost_Core;
using Fdp.ModuleHost_Core.Time;
using Xunit;

namespace FDP.Toolkit.Time.Tests
{
    /// <summary>
    /// Verifies <see cref="SwitchTimeModeDescriptorTranslator"/> behaviour at the
    /// event-bus level (no DDS infrastructure required).
    /// </summary>
    public class SwitchTimeModeTranslatorTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static SwitchTimeModeDescriptorTranslator CreateNullParticipantTranslator(
            FdpEventBus bus) =>
            new SwitchTimeModeDescriptorTranslator(participant: null, eventBus: bus);

        // ── Constructor / metadata ─────────────────────────────────────────────

        [Fact]
        public void Constructor_NullParticipant_DoesNotThrow()
        {
            var bus = new FdpEventBus();
            // Should not throw even without a DDS participant.
            var translator = CreateNullParticipantTranslator(bus);
            Assert.NotNull(translator);
        }

        [Fact]
        public void TopicName_Is_SwitchTimeModeEvent()
        {
            var bus = new FdpEventBus();
            var translator = CreateNullParticipantTranslator(bus);
            Assert.Equal("SwitchTimeModeEvent", translator.TopicName);
        }

        [Fact]
        public void DescriptorOrdinal_Is_201()
        {
            var bus = new FdpEventBus();
            var translator = CreateNullParticipantTranslator(bus);
            Assert.Equal(201L, translator.DescriptorOrdinal);
        }

        // ── Bus pre-registration ───────────────────────────────────────────────

        [Fact]
        public void Constructor_RegistersSwitchTimeModeEvent_OnEventBus()
        {
            var bus = new FdpEventBus();
            // Pre-registration should allow Consume<T> without warm-up publish.
            var translator = CreateNullParticipantTranslator(bus);

            // After SwapBuffers the current buffer should be an empty span, not a crash.
            bus.SwapBuffers();
            var events = bus.Consume<SwitchTimeModeEvent>();
            Assert.Empty(events.ToArray());
        }

        // ── ScanAndPublish ─────────────────────────────────────────────────────

        [Fact]
        public void ScanAndPublish_NullParticipant_IsNoOp()
        {
            var bus = new FdpEventBus();
            var translator = CreateNullParticipantTranslator(bus);

            // Publish an event then swap — ScanAndPublish should not throw.
            bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode = TimeMode.Deterministic,
                BarrierWallTicks = 12345L,
                FixedDelta = 1f / 60f
            });
            bus.SwapBuffers();

            // No DDS writer — returns without doing anything. Must not throw.
            translator.ScanAndPublish(null!);
        }

        // ── PollIngress ────────────────────────────────────────────────────────

        [Fact]
        public void PollIngress_NullParticipant_IsNoOp()
        {
            var bus = new FdpEventBus();
            var translator = CreateNullParticipantTranslator(bus);

            // No DDS reader — must not throw.
            translator.PollIngress(null!, null!);
        }

        // ── Descriptor interface completeness ──────────────────────────────────

        [Fact]
        public void ApplyToEntity_DoesNotThrow()
        {
            var bus = new FdpEventBus();
            var translator = CreateNullParticipantTranslator(bus);
            // No-op implementation; calling it must not throw.
            translator.ApplyToEntity(default, new object(), null!);
        }

        [Fact]
        public void Dispose_NetworkEntityId_DoesNotThrow()
        {
            var bus = new FdpEventBus();
            var translator = CreateNullParticipantTranslator(bus);
            translator.Dispose(42L);
        }

        // ── CreateDescriptorTranslator factory ─────────────────────────────────

        [Fact]
        public void TimeNetworkModule_CreateDescriptorTranslator_ReturnsTranslatorInstance()
        {
            var bus = new FdpEventBus();
            // Returns the concrete SwitchTimeModeDescriptorTranslator.
            var translator = TimeNetworkModule.CreateDescriptorTranslator(null, bus)
                as SwitchTimeModeDescriptorTranslator;
            Assert.NotNull(translator);
            Assert.Equal("SwitchTimeModeEvent", translator!.TopicName);
        }

        [Fact]
        public void TimeNetworkModule_CreateDescriptorTranslator_NullBus_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                TimeNetworkModule.CreateDescriptorTranslator(null, null!));
        }
    }
}
