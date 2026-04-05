using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CycloneDDS.Core;
using CycloneDDS.Runtime;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    /// <summary>
    /// Direct C# marshal round-trip tests for <see cref="MissionControlRequest"/>.
    ///
    /// These tests call <see cref="MissionControlRequest.MarshalToNative"/> and
    /// <see cref="MissionControlRequest.MarshalFromNative"/> directly (without going
    /// through DDS CDR encoding) to verify whether the C# serialization code correctly
    /// preserves nested sequences inside union types.  If these pass but the DDS
    /// pub/sub tests fail the bug is in the <c>_ops</c> CDR descriptor.
    /// </summary>
    public class MissionControlMarshalRoundTripTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private static MissionControlRequest MarshalRoundTrip(in MissionControlRequest source)
        {
            int totalSize = MissionControlRequest.GetNativeSize(source);
            int headSize  = MissionControlRequest.GetNativeHeadSize();

            byte[] buffer = new byte[totalSize];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer,
                System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                IntPtr ptr  = handle.AddrOfPinnedObject();
                var    span = buffer.AsSpan(0, totalSize);
                var    arena = new NativeArena(span, ptr, headSize);

                MissionControlRequest.MarshalToNative(source, ptr, ref arena);
                MissionControlRequest.MarshalFromNative(ptr, out var result);
                return result;
            }
            finally
            {
                handle.Free();
            }
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public void CmdReplaceMission_TasksPreservedAfterMarshalRoundTrip()
        {
            var taskId = Guid.NewGuid();

            var missionTask = new MissionTask
            {
                TaskId          = taskId,
                ExecutingEngine = "CGFX",
                BehaviorId      = "WanderMilitary",
                BehaviorParams  = string.Empty,
                Triggers        = new List<MissionTrigger>(),
                State           = eTaskState.TASK_PLANNED,
            };

            var missionPlan = new MissionPlan
            {
                ActiveTaskId = taskId,
                Tasks        = new List<MissionTask> { missionTask },
            };

            var request = new MissionControlRequest
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 42L,
                BaseVersion    = 0,
                Payload        = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = missionPlan,
                },
            };

            var result = MarshalRoundTrip(request);

            Assert.Equal(eMissionCommandType.CMD_REPLACE_MISSION, result.Payload._d);
            Assert.NotNull(result.Payload.FullMissionData.Tasks);
            Assert.Single(result.Payload.FullMissionData.Tasks);
            Assert.Equal("WanderMilitary", result.Payload.FullMissionData.Tasks[0].BehaviorId);
            Assert.Equal(taskId, result.Payload.FullMissionData.Tasks[0].TaskId);
        }

        [Fact]
        public void CmdReplaceMission_RequestIdPreservedAfterMarshalRoundTrip()
        {
            var requestId = Guid.NewGuid();
            var request   = new MissionControlRequest
            {
                RequestId      = requestId,
                TargetEntityId = 99L,
                BaseVersion    = 7L,
                Payload        = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = new MissionPlan
                    {
                        ActiveTaskId = Guid.Empty,
                        Tasks        = new List<MissionTask>(),
                    },
                },
            };

            var result = MarshalRoundTrip(request);

            Assert.Equal(requestId, result.RequestId);
            Assert.Equal(99L, result.TargetEntityId);
            Assert.Equal(7L, result.BaseVersion);
        }

        [Fact]
        public void CmdReplaceMission_MultipleTasksPreservedAfterMarshalRoundTrip()
        {
            var tasks = new List<MissionTask>
            {
                new MissionTask { TaskId = Guid.NewGuid(), BehaviorId = "MoveToLocation", BehaviorParams = "{}", ExecutingEngine = "CGFX", Triggers = new List<MissionTrigger>(), State = eTaskState.TASK_PLANNED },
                new MissionTask { TaskId = Guid.NewGuid(), BehaviorId = "WanderMilitary",  BehaviorParams = "",   ExecutingEngine = "CGFX", Triggers = new List<MissionTrigger>(), State = eTaskState.TASK_PLANNED },
            };

            var request = new MissionControlRequest
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 1L,
                Payload        = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = new MissionPlan { ActiveTaskId = tasks[0].TaskId, Tasks = tasks },
                },
            };

            var result = MarshalRoundTrip(request);

            Assert.Equal(2, result.Payload.FullMissionData.Tasks.Count);
            Assert.Equal("MoveToLocation", result.Payload.FullMissionData.Tasks[0].BehaviorId);
            Assert.Equal("WanderMilitary",  result.Payload.FullMissionData.Tasks[1].BehaviorId);
        }

        [Fact]
        public async System.Threading.Tasks.Task CmdReplaceMission_DdsRoundTrip_TasksPreserved()
        {
            var taskId = Guid.NewGuid();
            var missionTask = new MissionTask
            {
                TaskId          = taskId,
                ExecutingEngine = "CGFX",
                BehaviorId      = "WanderMilitary",
                BehaviorParams  = string.Empty,
                Triggers        = new List<MissionTrigger>(),
                State           = eTaskState.TASK_PLANNED,
            };

            var request = new MissionControlRequest
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 1L,
                BaseVersion    = 0,
                Payload        = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = new MissionPlan
                    {
                        ActiveTaskId = taskId,
                        Tasks        = new List<MissionTask> { missionTask },
                    },
                },
            };

            // Test 1: same participant (intra-process loopback)
            using var participantA = new DdsParticipant(150);
            using var writer       = new DdsWriter<MissionControlRequest>(participantA, "MissionControlRequest");
            using var reader       = new DdsReader<MissionControlRequest>(participantA, "MissionControlRequest");

            await System.Threading.Tasks.Task.Delay(500);
            writer.Write(request);
            await System.Threading.Tasks.Task.Delay(500);

            using (var loan = reader.Take(1))
            {
                MissionControlRequest result = default;
                foreach (var sample in loan)
                {
                    if (sample.IsValid) { result = sample.Data; break; }
                }
                Assert.Equal(eMissionCommandType.CMD_REPLACE_MISSION, result.Payload._d);
                Assert.Single(result.Payload.FullMissionData.Tasks);
                Assert.Equal("WanderMilitary", result.Payload.FullMissionData.Tasks[0].BehaviorId);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task CmdReplaceMission_CrossParticipant_DdsRoundTrip_TasksPreserved()
        {
            var taskId = Guid.NewGuid();
            var request = new MissionControlRequest
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 1L,
                Payload        = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = new MissionPlan
                    {
                        ActiveTaskId = taskId,
                        Tasks        = new List<MissionTask>
                        {
                            new MissionTask
                            {
                                TaskId          = taskId,
                                ExecutingEngine = "CGFX",
                                BehaviorId      = "WanderMilitary",
                                BehaviorParams  = string.Empty,
                                Triggers        = new List<MissionTrigger>(),
                                State           = eTaskState.TASK_PLANNED,
                            },
                        },
                    },
                },
            };

            // Test 2: separate participants (cross-process loopback)
            using var writerParticipant = new DdsParticipant(151);
            using var readerParticipant = new DdsParticipant(151);
            using var writer2 = new DdsWriter<MissionControlRequest>(writerParticipant, "MissionControlRequest");
            using var reader2 = new DdsReader<MissionControlRequest>(readerParticipant, "MissionControlRequest");

            await System.Threading.Tasks.Task.Delay(500);
            writer2.Write(request);
            await System.Threading.Tasks.Task.Delay(500);

            using var loan2 = reader2.Take(1);
            MissionControlRequest result2 = default;
            foreach (var sample in loan2)
            {
                if (sample.IsValid) { result2 = sample.Data; break; }
            }

            Assert.Equal(eMissionCommandType.CMD_REPLACE_MISSION, result2.Payload._d);
            Assert.NotNull(result2.Payload.FullMissionData.Tasks);
            Assert.Single(result2.Payload.FullMissionData.Tasks);
            Assert.Equal("WanderMilitary", result2.Payload.FullMissionData.Tasks[0].BehaviorId);
        }

        [Fact]
        public void MissionPlan_DirectMarshalRoundTrip_TasksPreserved()
        {
            var taskId = Guid.NewGuid();
            var plan   = new MissionPlan
            {
                ActiveTaskId = taskId,
                Tasks        = new List<MissionTask>
                {
                    new MissionTask
                    {
                        TaskId          = taskId,
                        BehaviorId      = "WanderMilitary",
                        ExecutingEngine = "CGFX",
                        BehaviorParams  = string.Empty,
                        Triggers        = new List<MissionTrigger>(),
                        State           = eTaskState.TASK_PLANNED,
                    },
                },
            };

            int    totalSize = MissionPlan.GetNativeSize(plan);
            int    headSize  = MissionPlan.GetNativeHeadSize();
            byte[] buffer    = new byte[totalSize];
            var    handle    = System.Runtime.InteropServices.GCHandle.Alloc(buffer,
                System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                IntPtr ptr  = handle.AddrOfPinnedObject();
                var    span = buffer.AsSpan(0, totalSize);
                var    arena = new NativeArena(span, ptr, headSize);

                MissionPlan.MarshalToNative(plan, ptr, ref arena);
                MissionPlan.MarshalFromNative(ptr, out var result);

                Assert.Single(result.Tasks);
                Assert.Equal("WanderMilitary", result.Tasks[0].BehaviorId);
                Assert.Equal(taskId, result.ActiveTaskId);
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Verifies that <see cref="EntityMission"/> round-trips over a same-participant
        /// DDS writer/reader pair with <c>Plan.Tasks</c> preserved.
        /// If this fails the <c>EntityMission._ops</c> CDR descriptor has a bug.
        /// </summary>
        [Fact]
        public void EntityMission_DdsRoundTrip_PlanTasksPreserved()
        {
            using var participant = new DdsParticipant(200u);
            using var writer = new DdsWriter<EntityMission>(participant, "EntityMission_Test");
            using var reader = new DdsReader<EntityMission>(participant, "EntityMission_Test");

            var em = new EntityMission
            {
                EntityId = 42L,
                Plan = new MissionPlan
                {
                    ActiveTaskId = Guid.NewGuid(),
                    Tasks = new List<MissionTask>
                    {
                        new MissionTask
                        {
                            TaskId          = Guid.NewGuid(),
                            ExecutingEngine = "SimHost",
                            BehaviorId      = "WanderMilitary",
                            BehaviorParams  = string.Empty,
                            Triggers        = new List<MissionTrigger>
                            {
                                new MissionTrigger { Type = "DoctrineFinished", Params = "0" }
                            },
                            State = eTaskState.TASK_ACTIVE
                        }
                    }
                }
            };

            writer.Write(em);

            EntityMission result = default;
            bool found = false;
            for (int i = 0; i < 50 && !found; i++)
            {
                System.Threading.Thread.Sleep(20);
                using var loan = reader.Take(10);
                foreach (var sample in loan)
                {
                    if (!sample.IsValid) continue;
                    if (sample.Data.EntityId != 42L) continue;
                    result = sample.Data;
                    found = true;
                    break;
                }
            }

            Assert.True(found, "EntityMission was not received via DDS within timeout.");
            Assert.NotNull(result.Plan.Tasks);
            Assert.Single(result.Plan.Tasks);
            Assert.Equal("WanderMilitary", result.Plan.Tasks[0].BehaviorId);
            Assert.Single(result.Plan.Tasks[0].Triggers);
            Assert.Equal("DoctrineFinished", result.Plan.Tasks[0].Triggers![0].Type);
        }
    }
}
