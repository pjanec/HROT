using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// Verifies that runtime execution components tagged with [DataPolicy(DataPolicy.NoSave)]
    /// are excluded from GetSaveableTypeIds() but remain in GetRecordableTypeIds().
    ///
    /// Success conditions for TASK-S102, TASK-S103, TASK-S104.
    /// </summary>
    public sealed class DataPolicyNoSaveTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public DataPolicyNoSaveTests()
        {
            // ComponentTypeRegistry is a shared static — clear before each test class.
            ComponentTypeRegistry.Clear();
            _repo = new EntityRepository();
        }

        public void Dispose() => _repo.Dispose();

        // ── TASK-S102: Execution Channel Components ───────────────────────────────

        [Fact]
        public void ChannelComponents_AbsentFromSaveableTypeIds()
        {
            _repo.RegisterComponent<LocomotionChannel>();
            _repo.RegisterComponent<WeaponChannel>();
            _repo.RegisterComponent<InteractionChannel>();

            var saveableIds = new HashSet<int>(ComponentTypeRegistry.GetSaveableTypeIds());

            int locomotionId    = ComponentTypeRegistry.GetId(typeof(LocomotionChannel));
            int weaponId        = ComponentTypeRegistry.GetId(typeof(WeaponChannel));
            int interactionId   = ComponentTypeRegistry.GetId(typeof(InteractionChannel));

            Assert.DoesNotContain(locomotionId,  saveableIds);
            Assert.DoesNotContain(weaponId,      saveableIds);
            Assert.DoesNotContain(interactionId, saveableIds);
        }

        [Fact]
        public void ChannelComponents_PresentInRecordableTypeIds()
        {
            _repo.RegisterComponent<LocomotionChannel>();
            _repo.RegisterComponent<WeaponChannel>();
            _repo.RegisterComponent<InteractionChannel>();

            var recordableIds = new HashSet<int>(ComponentTypeRegistry.GetRecordableTypeIds());

            int locomotionId    = ComponentTypeRegistry.GetId(typeof(LocomotionChannel));
            int weaponId        = ComponentTypeRegistry.GetId(typeof(WeaponChannel));
            int interactionId   = ComponentTypeRegistry.GetId(typeof(InteractionChannel));

            Assert.Contains(locomotionId,  recordableIds);
            Assert.Contains(weaponId,      recordableIds);
            Assert.Contains(interactionId, recordableIds);
        }

        // ── TASK-S103: Brain Execution Components ─────────────────────────────────

        [Fact]
        public void BrainComponents_AbsentFromSaveableTypeIds()
        {
            _repo.RegisterComponent<BrainBTreeState>();
            _repo.RegisterComponent<BrainHsm64>();
            _repo.RegisterComponent<BrainHsm128>();

            var saveableIds = new HashSet<int>(ComponentTypeRegistry.GetSaveableTypeIds());

            int btreeId  = ComponentTypeRegistry.GetId(typeof(BrainBTreeState));
            int hsm64Id  = ComponentTypeRegistry.GetId(typeof(BrainHsm64));
            int hsm128Id = ComponentTypeRegistry.GetId(typeof(BrainHsm128));

            Assert.DoesNotContain(btreeId,  saveableIds);
            Assert.DoesNotContain(hsm64Id,  saveableIds);
            Assert.DoesNotContain(hsm128Id, saveableIds);
        }

        [Fact]
        public void BrainComponents_PresentInRecordableTypeIds()
        {
            _repo.RegisterComponent<BrainBTreeState>();
            _repo.RegisterComponent<BrainHsm64>();
            _repo.RegisterComponent<BrainHsm128>();

            var recordableIds = new HashSet<int>(ComponentTypeRegistry.GetRecordableTypeIds());

            int btreeId  = ComponentTypeRegistry.GetId(typeof(BrainBTreeState));
            int hsm64Id  = ComponentTypeRegistry.GetId(typeof(BrainHsm64));
            int hsm128Id = ComponentTypeRegistry.GetId(typeof(BrainHsm128));

            Assert.Contains(btreeId,  recordableIds);
            Assert.Contains(hsm64Id,  recordableIds);
            Assert.Contains(hsm128Id, recordableIds);
        }

        // ── TASK-S104: Transient Perception Components ────────────────────────────

        [Fact]
        public void PerceptionComponents_AbsentFromSaveableTypeIds()
        {
            _repo.RegisterComponent<SensorContactList>();
            _repo.RegisterComponent<ActiveSensorTracks>();

            var saveableIds = new HashSet<int>(ComponentTypeRegistry.GetSaveableTypeIds());

            int contactListId     = ComponentTypeRegistry.GetId(typeof(SensorContactList));
            int activeSensorTracksId = ComponentTypeRegistry.GetId(typeof(ActiveSensorTracks));

            Assert.DoesNotContain(contactListId,        saveableIds);
            Assert.DoesNotContain(activeSensorTracksId, saveableIds);
        }

        [Fact]
        public void PerceptionComponents_PresentInRecordableTypeIds()
        {
            _repo.RegisterComponent<SensorContactList>();
            _repo.RegisterComponent<ActiveSensorTracks>();

            var recordableIds = new HashSet<int>(ComponentTypeRegistry.GetRecordableTypeIds());

            int contactListId        = ComponentTypeRegistry.GetId(typeof(SensorContactList));
            int activeSensorTracksId = ComponentTypeRegistry.GetId(typeof(ActiveSensorTracks));

            Assert.Contains(contactListId,        recordableIds);
            Assert.Contains(activeSensorTracksId, recordableIds);
        }
    }
}
