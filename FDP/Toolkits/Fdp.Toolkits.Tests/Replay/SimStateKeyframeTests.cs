using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Survey;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkits.Tests.Replay
{
    /// <summary>
    /// ⭐⭐ <c>CE-3299</c> D1 (restore design §6b, M4) — the SIM-STATE types a restored run diverges without are RECORDED: each, set to
    /// non-default values, survives a recorded keyframe (the format of every checkpoint, replay and dry run) field for field. ⛔ Each was
    /// <c>Transient</c>/<c>NoReplay</c>, so a keyframe dropped it and a restore emptied it. <c>MissionAdapterState</c> (Hrot.CGF) has its
    /// own rail in the CGF suite.
    /// </summary>
    public sealed class SimStateKeyframeTests
    {
        private static readonly JsonSerializerOptions Fields = new() { IncludeFields = true };

        // set the value, keyframe, remove it, put the keyframe back: the value comes back field for field, and is recordable
        private static void SurvivesAKeyframe<T>(Func<EntityRepository, T> make) where T : class
        {
            using var repo = new EntityRepository();
            repo.RegisterManagedComponent<T>();
            Assert.True(ComponentTypeRegistry.IsRecordable(ComponentTypeRegistry.GetId(typeof(T))), $"{typeof(T).Name} must be recordable (D1)");
            var e = repo.CreateEntity();
            var value = make(repo);
            repo.SetManagedComponent(e, value);
            repo.Tick();
            var keyframe = WorldKeyframes.Capture(repo, CaptureOptions.Recording);

            repo.Tick();
            repo.RemoveComponent<T>(e);
            WorldKeyframes.Apply(repo, keyframe);

            Assert.True(repo.HasManagedComponent<T>(e), $"{typeof(T).Name} must come back from the keyframe");
            Assert.Equal(JsonSerializer.Serialize(value, Fields), JsonSerializer.Serialize(((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetManagedComponentRO<T>(e), Fields));
        }

        [Fact] public void D1_PausedTask() => SurvivesAKeyframe(_ => new PausedTask
        {
            BehaviorName = "Patrol", JsonParams = "{\"r\":3}", Origin = BehaviorOrigin.Superior, Hash = 77, InstanceId = 9,
            BrainTier = 2, HeldKeys = new[] { 4, 5 }, Restart = true,
        });

        [Fact] public void D1_SubordinateOrders() => SurvivesAKeyframe(repo => new SubordinateOrders(new[]
        {
            new SubordinateOrders.Entry(repo.CreateEntity(), "Hold", BehaviorOrigin.Superior, 3, "{\"a\":1}"),
            new SubordinateOrders.Entry(repo.CreateEntity(), "Move", BehaviorOrigin.Operator, 4),
        }));

        [Fact] public void D1_BehaviorStartRecord() => SurvivesAKeyframe(_ => new BehaviorStartRecord
        {
            BehaviorName = "UrbanMovement", JsonParams = "{\"route\":7}", InstanceId = 12,
        });

        [Fact] public void D1_SopStartRecord() => SurvivesAKeyframe(_ =>
        {
            return new SopStartRecord { BehaviorName = "Sop", JsonParams = "{}", InstanceId = 5, HostedKeys = new[] { 41, 42 } };
        });

        // ⭐ CE-4046 ② — the route geometry (an immutable record) and the follower (an unmanaged struct) survive a keyframe apart
        [Fact] public void D1_AirPath() => SurvivesAKeyframe(_ => new AirPath
        {
            RouteEntityId = 1234, RouteVersion = 2, IntentId = 8, Loop = true,
            Points = System.Collections.Immutable.ImmutableArray.Create(new Vector3(1, 2, 3), new Vector3(4, 5, 6)),
            S = System.Collections.Immutable.ImmutableArray.Create(0f, 5.2f), Speed = System.Collections.Immutable.ImmutableArray.Create(10f, 12f),
            Curvature = System.Collections.Immutable.ImmutableArray.Create(0f, 0.1f),
            Stops = System.Collections.Immutable.ImmutableArray.Create(new AirStop { S = 5.2f, Waypoint = 1, HoldSeconds = 3f }),
        });

        [Fact] public void CE4046_AirFollower_SurvivesAKeyframe()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<AirFollower>();
            Assert.True(ComponentTypeRegistry.IsRecordable(ComponentTypeRegistry.GetId(typeof(AirFollower))), "AirFollower must be recordable");
            var e = repo.CreateEntity();
            var value = new AirFollower
            {
                Stage = AirStage.Holding, Progress = 3.3f, SpeedNow = 11f, NextStop = 1, HoldLeft = 1.5f, ReleaseArmed = false,
                Dropping = true, Pitch = 0.1f, Roll = -0.2f, OrbitCenter = new Vector2(7, 8), OrbitAngle = 1.25f,
            };
            repo.AddComponent(e, value);
            repo.Tick();
            var keyframe = WorldKeyframes.Capture(repo, CaptureOptions.Recording);
            repo.Tick();
            repo.RemoveComponent<AirFollower>(e);
            WorldKeyframes.Apply(repo, keyframe);
            Assert.True(repo.HasComponent<AirFollower>(e), "AirFollower must come back from the keyframe");
            Assert.Equal(value, repo.GetComponentRO<AirFollower>(e));
        }

        /// <summary>⭐ CE-4057 — the construction barrier's tag is part of what a restore re-derives from: the ELM re-opens a Constructing entity
        /// (<c>ResumeFromRestoredWorld</c>) and the gateway waits for peers only when the entity carries <c>PendingNetworkAck</c>. ⛔ It was
        /// <c>Transient</c>, so a review's return dropped it and a reliable entity went Active without its peers.</summary>
        [Fact] public void CE4057_PendingNetworkAck_SurvivesAKeyframe()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.PendingNetworkAck>();
            Assert.True(ComponentTypeRegistry.IsRecordable(ComponentTypeRegistry.GetId(typeof(Fdp.Toolkit.Replication.Components.PendingNetworkAck))),
                "PendingNetworkAck must be recordable (CE-4057)");
            var e = repo.CreateEntity();
            var value = new Fdp.Toolkit.Replication.Components.PendingNetworkAck { ExpectedType = Fdp.Toolkit.Replication.ReliableInitType.AllPeers };
            repo.AddComponent(e, value);
            repo.Tick();
            var keyframe = WorldKeyframes.Capture(repo, CaptureOptions.Recording);
            repo.Tick();
            repo.RemoveComponent<Fdp.Toolkit.Replication.Components.PendingNetworkAck>(e);
            WorldKeyframes.Apply(repo, keyframe);
            Assert.True(repo.HasComponent<Fdp.Toolkit.Replication.Components.PendingNetworkAck>(e), "PendingNetworkAck must come back from the keyframe");
            Assert.Equal(value, repo.GetComponentRO<Fdp.Toolkit.Replication.Components.PendingNetworkAck>(e));
        }

        [Fact] public void D1_SensorCapability() => SurvivesAKeyframe(_ => new SensorCapability
        {
            Default = new SensorEntryDto { Kind = SensorModality.Visual, Template = Guid.NewGuid(), SearchRadius = 50f,
                                           Visual = new VisualSensorDto { Range = 800f, FieldOfViewDegrees = 120f } },
            Current = new SensorEntryDto { Kind = SensorModality.Visual, Disabled = true, SearchRadius = 20f,
                                           Visual = new VisualSensorDto { Range = 300f, FieldOfViewDegrees = 60f } },
        });

        [Fact] public void D1_SensorConfigPayload() => SurvivesAKeyframe(_ => new SensorConfigPayload { Kind = "Visual", Json = "{\"range\":300}" });

        [Fact] public void D1_WorldSurveyRequest() => SurvivesAKeyframe(_ => new WorldSurveyRequest
        {
            Kind = WorldSurveyKind.VisibilityFan, Rays = 18, Range = 220f, EyeHeight = 1.8f, AreaX = 3f, AreaY = 4f, AreaRadius = 30f,
            RefreshSeconds = 0.5f, Buildings = new List<int> { 6, 7 },
            Origins = new List<SurveyOrigin> { new() { X = 1, Y = 2, Z = 3, Bearing = 0.5f, HalfArc = 0.7f } },
            Points = new List<SurveyPointQuery> { new() { X = 9, Y = 8, Z = 7 } },
        });
    }
}
