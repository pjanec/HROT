using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fbt;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Events;
using Xunit;

namespace Hrot.MuscleCharacter.Animation.Tests
{
    /// <summary>
    /// Phase 4 tests: eight animation event types, picker attributes, EventId uniqueness.
    /// (ANC-P4-01, ANC-P4-02, DD-3 §3, §9.7)
    /// </summary>
    public class Phase4EventTypeTests
    {
        // ---- Helpers -------------------------------------------------------

        private static int GetEventId<T>() =>
            typeof(T)
                .GetCustomAttributes(typeof(EventIdAttribute), false)
                .Cast<EventIdAttribute>()
                .Single()
                .Id;

        private static bool HasDataPolicy<T>(DataPolicy policy) =>
            typeof(T)
                .GetCustomAttributes(typeof(DataPolicyAttribute), false)
                .Cast<DataPolicyAttribute>()
                .Any(a => (a.Policy & policy) != 0);

        // ---- ANC-P4-01: Event IDs in 8200-8299 range, no collision ---------

        [Fact]
        public void AllAnimationEvents_HaveEventIdInCorrectRange()
        {
            var ids = new[]
            {
                GetEventId<MontageStartedEvent>(),
                GetEventId<MontageEndedEvent>(),
                GetEventId<MontageSectionAdvancedEvent>(),
                GetEventId<StanceChangedEvent>(),
                GetEventId<FootstepEvent>(),
                GetEventId<HitWindowOpenedEvent>(),
                GetEventId<HitWindowClosedEvent>(),
                GetEventId<AnimNotifyEvent>(),
            };

            foreach (var id in ids)
                Assert.InRange(id, 8200, 8299);
        }

        [Fact]
        public void AllAnimationEvents_HaveDistinctEventIds()
        {
            var ids = new[]
            {
                GetEventId<MontageStartedEvent>(),
                GetEventId<MontageEndedEvent>(),
                GetEventId<MontageSectionAdvancedEvent>(),
                GetEventId<StanceChangedEvent>(),
                GetEventId<FootstepEvent>(),
                GetEventId<HitWindowOpenedEvent>(),
                GetEventId<HitWindowClosedEvent>(),
                GetEventId<AnimNotifyEvent>(),
            };

            Assert.Equal(ids.Length, ids.Distinct().Count());
        }

        [Fact]
        public void AllAnimationEventIds_DoNotCollideWith_GlobalActionRequestedEvent()
        {
            // GlobalActionRequestedEvent uses [EventId(8059)] — must not overlap with 8200-8299.
            // This test documents the architect ruling from TASK-DETAIL ANC-P4-01.
            const int globalActionEventId = 8059;

            var ids = new[]
            {
                GetEventId<MontageStartedEvent>(),
                GetEventId<MontageEndedEvent>(),
                GetEventId<MontageSectionAdvancedEvent>(),
                GetEventId<StanceChangedEvent>(),
                GetEventId<FootstepEvent>(),
                GetEventId<HitWindowOpenedEvent>(),
                GetEventId<HitWindowClosedEvent>(),
                GetEventId<AnimNotifyEvent>(),
            };

            Assert.DoesNotContain(globalActionEventId, ids);
        }

        [Fact]
        public void AnimationEventIds_AreAssignedInExpectedOrder()
        {
            // Verifies the exact IDs per architect ruling: Started=8201, Ended=8202,
            // SectionAdvanced=8203, StanceChanged=8204, Footstep=8210,
            // HitWindowOpened=8211, HitWindowClosed=8212, AnimNotify=8213.
            Assert.Equal(8201, GetEventId<MontageStartedEvent>());
            Assert.Equal(8202, GetEventId<MontageEndedEvent>());
            Assert.Equal(8203, GetEventId<MontageSectionAdvancedEvent>());
            Assert.Equal(8204, GetEventId<StanceChangedEvent>());
            Assert.Equal(8210, GetEventId<FootstepEvent>());
            Assert.Equal(8211, GetEventId<HitWindowOpenedEvent>());
            Assert.Equal(8212, GetEventId<HitWindowClosedEvent>());
            Assert.Equal(8213, GetEventId<AnimNotifyEvent>());
        }

        // ---- ANC-P4-01: All events have [DataPolicy(NoRecord)] -------------

        [Fact]
        public void AllAnimationEvents_HaveDataPolicyNoRecord()
        {
            Assert.True(HasDataPolicy<MontageStartedEvent>(DataPolicy.NoRecord));
            Assert.True(HasDataPolicy<MontageEndedEvent>(DataPolicy.NoRecord));
            Assert.True(HasDataPolicy<MontageSectionAdvancedEvent>(DataPolicy.NoRecord));
            Assert.True(HasDataPolicy<StanceChangedEvent>(DataPolicy.NoRecord));
            Assert.True(HasDataPolicy<FootstepEvent>(DataPolicy.NoRecord));
            Assert.True(HasDataPolicy<HitWindowOpenedEvent>(DataPolicy.NoRecord));
            Assert.True(HasDataPolicy<HitWindowClosedEvent>(DataPolicy.NoRecord));
            Assert.True(HasDataPolicy<AnimNotifyEvent>(DataPolicy.NoRecord));
        }

        // ---- ANC-P4-01: Target field is first field on each event ----------

        [Fact]
        public void MontageStartedEvent_HasTargetFieldFirst()
        {
            var first = typeof(MontageStartedEvent).GetFields(
                BindingFlags.Public | BindingFlags.Instance)[0];
            Assert.Equal("Target", first.Name);
            Assert.Equal(typeof(Entity), first.FieldType);
        }

        [Fact]
        public void MontageEndedEvent_HasTargetFieldFirst()
        {
            var first = typeof(MontageEndedEvent).GetFields(
                BindingFlags.Public | BindingFlags.Instance)[0];
            Assert.Equal("Target", first.Name);
            Assert.Equal(typeof(Entity), first.FieldType);
        }

        [Fact]
        public void FootstepEvent_HasTargetFieldFirst()
        {
            var first = typeof(FootstepEvent).GetFields(
                BindingFlags.Public | BindingFlags.Instance)[0];
            Assert.Equal("Target", first.Name);
            Assert.Equal(typeof(Entity), first.FieldType);
        }

        [Fact]
        public void AnimNotifyEvent_HasTargetFieldFirst()
        {
            var first = typeof(AnimNotifyEvent).GetFields(
                BindingFlags.Public | BindingFlags.Instance)[0];
            Assert.Equal("Target", first.Name);
            Assert.Equal(typeof(Entity), first.FieldType);
        }

        // ---- ANC-P4-01: MontageEndReason enum exists with expected values --

        [Fact]
        public void MontageEndReason_HasExpectedValues()
        {
            Assert.Equal(0, (int)MontageEndReason.NaturalEnd);
            Assert.Equal(1, (int)MontageEndReason.Interrupted);
            Assert.Equal(2, (int)MontageEndReason.BlendedOutByNext);
            Assert.Equal(3, (int)MontageEndReason.Failed);
        }

        // ---- ANC-P4-02: Picker attributes on correct fields ----------------

        [Fact]
        public void AnimNotifyEvent_MarkerHash_HasAnimMarkerPickerAttribute()
        {
            var field = typeof(AnimNotifyEvent).GetField("MarkerHash",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            Assert.Equal(typeof(uint), field!.FieldType);
            Assert.NotNull(field.GetCustomAttribute<AnimMarkerPickerAttribute>());
        }

        [Fact]
        public void MontageStartedEvent_MontageId_HasMontagePickerAttribute()
        {
            var field = typeof(MontageStartedEvent).GetField("MontageId",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            Assert.NotNull(field!.GetCustomAttribute<MontagePickerAttribute>());
        }

        [Fact]
        public void MontageEndedEvent_MontageId_HasMontagePickerAttribute()
        {
            var field = typeof(MontageEndedEvent).GetField("MontageId",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            Assert.NotNull(field!.GetCustomAttribute<MontagePickerAttribute>());
        }

        [Fact]
        public void MontageSectionAdvancedEvent_MontageId_HasMontagePickerAttribute()
        {
            var field = typeof(MontageSectionAdvancedEvent).GetField("MontageId",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            Assert.NotNull(field!.GetCustomAttribute<MontagePickerAttribute>());
        }

        [Fact]
        public void HitWindowOpenedEvent_MontageId_HasMontagePickerAttribute()
        {
            var field = typeof(HitWindowOpenedEvent).GetField("MontageId",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            Assert.NotNull(field!.GetCustomAttribute<MontagePickerAttribute>());
        }

        [Fact]
        public void HitWindowClosedEvent_MontageId_HasMontagePickerAttribute()
        {
            var field = typeof(HitWindowClosedEvent).GetField("MontageId",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            Assert.NotNull(field!.GetCustomAttribute<MontagePickerAttribute>());
        }

        [Fact]
        public void AnimNotifyEvent_MontageId_HasMontagePickerAttribute()
        {
            var field = typeof(AnimNotifyEvent).GetField("MontageId",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            Assert.NotNull(field!.GetCustomAttribute<MontagePickerAttribute>());
        }

        // ---- ANC-P4-01: StanceChangedEvent field types ---------------------

        [Fact]
        public void StanceChangedEvent_HasStanceIdFields()
        {
            var prev = typeof(StanceChangedEvent).GetField("PreviousStance",
                BindingFlags.Public | BindingFlags.Instance);
            var next = typeof(StanceChangedEvent).GetField("NewStance",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(prev);
            Assert.NotNull(next);
            Assert.Equal(typeof(StanceId), prev!.FieldType);
            Assert.Equal(typeof(StanceId), next!.FieldType);
        }

        // ---- ANC-P4-01: FootstepEvent has WorldPosition as Vector3 ----------

        [Fact]
        public void FootstepEvent_HasVector3WorldPosition()
        {
            var pos = typeof(FootstepEvent).GetField("WorldPosition",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(pos);
            Assert.Equal(typeof(Vector3), pos!.FieldType);
        }
    }
}
