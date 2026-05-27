using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Unit tests for <see cref="FakeDtCrowdProvider"/>.
    /// </summary>
    public class FakeDtCrowdProviderTests
    {
        // ── Test helpers ─────────────────────────────────────────────────────────

        private static CrowdAgentParams DefaultParams(float radius = 0.5f) => new CrowdAgentParams
        {
            Radius           = radius,
            Height           = 1.8f,
            MaxSpeed         = 5f,
            MaxAcceleration  = 10f,
            SeparationWeight = 2,
        };

        /// <summary>
        /// Minimal ISimulationView stub. Provides SimTransform positions for registered entities.
        /// Uses a fixed-size array for stable refs (no dictionary resize risk).
        /// </summary>
        private sealed class TestView : ISimulationView
        {
            private readonly SimTransform[] _transforms = new SimTransform[256];
            private readonly bool[]         _hasTransform = new bool[256];

            public void SetPosition(Entity e, Vector3 pos)
            {
                _transforms[e.Index]  = new SimTransform { Position = pos };
                _hasTransform[e.Index] = true;
            }

            public uint  Tick => 0;
            public float Time => 0f;

            public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
            {
                if (typeof(T) == typeof(SimTransform))
                    return ref Unsafe.As<SimTransform, T>(ref _transforms[e.Index]);
                throw new NotImplementedException($"Component {typeof(T).Name} not stubbed");
            }

            public T GetManagedComponentRO<T>(Entity e) where T : class
                => throw new NotImplementedException();

            public bool IsAlive(Entity e)
                => e.Index >= 0 && e.Index < _hasTransform.Length && _hasTransform[e.Index];

            public bool HasComponent<T>(Entity e) where T : unmanaged
                => typeof(T) == typeof(SimTransform)
                && e.Index >= 0 && e.Index < _hasTransform.Length && _hasTransform[e.Index];

            public bool HasManagedComponent<T>(Entity e) where T : class => false;

            public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => ReadOnlySpan<T>.Empty;

            public IReadOnlyList<T> ReadManagedEvents<T>() => Array.Empty<T>();

            public QueryBuilder Query() => throw new NotImplementedException();

            public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
        }

        // ── Tests ────────────────────────────────────────────────────────────────

        [Fact]
        public void RegisterAgent_NewEntity_ReturnsTrue()
        {
            var crowd  = new FakeDtCrowdProvider();
            var entity = new Entity(1, 1);
            Assert.True(crowd.RegisterAgent(entity, DefaultParams()));
        }

        [Fact]
        public void RegisterAgent_SameEntityTwice_ReturnsFalse()
        {
            var crowd  = new FakeDtCrowdProvider();
            var entity = new Entity(1, 1);
            crowd.RegisterAgent(entity, DefaultParams());
            Assert.False(crowd.RegisterAgent(entity, DefaultParams()));
        }

        [Fact]
        public void UnregisterAgent_RemovesAgent()
        {
            var crowd  = new FakeDtCrowdProvider();
            var entity = new Entity(1, 1);
            crowd.RegisterAgent(entity, DefaultParams());
            crowd.UnregisterAgent(entity);
            var api = (IFakeDtCrowdProviderTestApi)crowd;
            Assert.DoesNotContain(1, api.RegisteredEntityIndices);
        }

        [Fact]
        public void GetAgentVelocity_UnregisteredEntity_ReturnsZero()
        {
            var crowd  = new FakeDtCrowdProvider();
            var entity = new Entity(99, 1);
            Assert.Equal(Vector3.Zero, crowd.GetAgentVelocity(entity));
        }

        [Fact]
        public void Update_AgentMovesTowardTarget()
        {
            var crowd  = new FakeDtCrowdProvider();
            var entity = new Entity(1, 1);
            crowd.RegisterAgent(entity, DefaultParams());

            var target = new Vector3(10f, 0f, 0f);
            crowd.SetAgentTarget(entity, target);

            var view = new TestView();
            view.SetPosition(entity, Vector3.Zero);

            crowd.Update(0.1f, view);

            var vel = crowd.GetAgentVelocity(entity);
            Assert.True(vel.X > 0f, "Agent should have positive X velocity toward target");
        }

        [Fact]
        public void Update_AgentReachedTarget_VelocityIsZero()
        {
            var crowd  = new FakeDtCrowdProvider();
            var entity = new Entity(1, 1);
            crowd.RegisterAgent(entity, DefaultParams());

            // Place agent very close to target (within 0.1m threshold).
            var target = new Vector3(0.05f, 0f, 0f);
            crowd.SetAgentTarget(entity, target);

            var view = new TestView();
            view.SetPosition(entity, Vector3.Zero);

            crowd.Update(0.1f, view);

            Assert.True(crowd.TryGetAgentSnapshot(entity, out var snap));
            Assert.True(snap.ReachedTarget);
        }

        [Fact]
        public void Update_TwoAgentsCollide_SeparationApplied()
        {
            var crowd = new FakeDtCrowdProvider();
            var e1    = new Entity(1, 1);
            var e2    = new Entity(2, 1);
            crowd.RegisterAgent(e1, DefaultParams(radius: 1f));
            crowd.RegisterAgent(e2, DefaultParams(radius: 1f));

            crowd.SetAgentTarget(e1, new Vector3(10f, 0f, 0f));
            crowd.SetAgentTarget(e2, new Vector3(10f, 0f, 0f));

            var view = new TestView();
            // Place agents at same position — maximum overlap.
            view.SetPosition(e1, new Vector3(0f, 0f, 0f));
            view.SetPosition(e2, new Vector3(0.5f, 0f, 0f)); // within sum-of-radii = 2f

            crowd.Update(0.1f, view);

            Assert.True(crowd.TryGetAgentSnapshot(e1, out var snap1));
            Assert.True(snap1.NearbyAgentCount > 0, "e1 should report nearby agents");
        }

        [Fact]
        public void TryGetAgentSnapshot_Unregistered_ReturnsFalse()
        {
            var crowd  = new FakeDtCrowdProvider();
            Assert.False(crowd.TryGetAgentSnapshot(new Entity(42, 1), out _));
        }

        [Fact]
        public void TestApi_RegisteredEntityIndices_SortedByIndex()
        {
            var crowd = new FakeDtCrowdProvider();
            crowd.RegisterAgent(new Entity(5, 1), DefaultParams());
            crowd.RegisterAgent(new Entity(2, 1), DefaultParams());
            crowd.RegisterAgent(new Entity(8, 1), DefaultParams());

            var api     = (IFakeDtCrowdProviderTestApi)crowd;
            var indices = api.RegisteredEntityIndices;
            Assert.Equal(new[] { 2, 5, 8 }, indices);
        }

        [Fact]
        public void TestApi_UpdateCallCount_Increments()
        {
            var crowd = new FakeDtCrowdProvider();
            var view  = new TestView();
            crowd.Update(0.1f, view);
            crowd.Update(0.1f, view);
            var api = (IFakeDtCrowdProviderTestApi)crowd;
            Assert.Equal(2, api.UpdateCallCount);
        }

        [Fact]
        public void Update_MaxSpeedClamped()
        {
            var crowd  = new FakeDtCrowdProvider();
            var entity = new Entity(1, 1);
            crowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius          = 0.5f,
                Height          = 1.8f,
                MaxSpeed        = 2f,
                MaxAcceleration = 100f, // very high so speed is limiting factor
                SeparationWeight = 2,
            });

            crowd.SetAgentTarget(entity, new Vector3(100f, 0f, 0f));

            var view = new TestView();
            view.SetPosition(entity, Vector3.Zero);

            crowd.Update(1f, view); // 1s tick
            crowd.Update(1f, view);
            crowd.Update(1f, view);

            var vel = crowd.GetAgentVelocity(entity);
            Assert.True(vel.Length() <= 2f + 0.01f, $"Speed should be clamped to MaxSpeed=2. Got {vel.Length():F3}");
        }

        // ── Test 12: Agents with no target and no contact remain stationary ─────

        [Fact]
        public void Update_AgentsWithNoTarget_RemainStationary()
        {
            var crowd = new FakeDtCrowdProvider();
            var e1 = new Entity(1, 1);
            var e2 = new Entity(2, 1);
            var e3 = new Entity(3, 1);

            var p = new CrowdAgentParams
            {
                Radius          = 0.5f,
                Height          = 1.8f,
                MaxSpeed        = 5f,
                MaxAcceleration = 20f,
                SeparationWeight = 2,
            };
            crowd.RegisterAgent(e1, p);
            crowd.RegisterAgent(e2, p);
            crowd.RegisterAgent(e3, p);

            // Only e1 has a target. e2/e3 have no target and are far from others (no overlap).
            crowd.SetAgentTarget(e1, new Vector3(100f, 0f, 0f));

            var view = new TestView();
            // e1 starts at origin; e2/e3 are far away (>1 m separation, no overlap possible).
            view.SetPosition(e1, new Vector3(0f,    0f, 0f));
            view.SetPosition(e2, new Vector3(50f,   0f, 0f));
            view.SetPosition(e3, new Vector3(0f,    0f, 50f));

            for (int i = 0; i < 20; i++)
                crowd.Update(0.016f, view);

            // Agents with no target and not overlapping must stay stationary.
            float v2 = crowd.GetAgentVelocity(e2).Length();
            float v3 = crowd.GetAgentVelocity(e3).Length();
            Assert.True(v2 < 0.01f, $"e2 should be stationary, got speed={v2:F3}");
            Assert.True(v3 < 0.01f, $"e3 should be stationary, got speed={v3:F3}");
            // The agent with a target should be moving.
            float v1 = crowd.GetAgentVelocity(e1).Length();
            Assert.True(v1 > 0.1f, $"e1 should be moving toward target, got speed={v1:F3}");
        }

        // ── Test 13: OverrideAgentVelocity bypasses steering ─────────────────────

        [Fact]
        public void OverrideAgentVelocity_TestApiBypassesSteering()
        {
            var crowd  = new FakeDtCrowdProvider();
            var entity = new Entity(1, 1);
            crowd.RegisterAgent(entity, DefaultParams());

            // Strong desired velocity toward distant target.
            crowd.SetAgentTarget(entity, new Vector3(100f, 0f, 0f));

            var view = new TestView();
            view.SetPosition(entity, Vector3.Zero);

            var api = (IFakeDtCrowdProviderTestApi)crowd;
            var overrideVel = new Vector3(7f, 0f, 0f);
            api.OverrideAgentVelocity(entity, overrideVel);

            crowd.Update(0.016f, view);

            Assert.Equal(overrideVel, crowd.GetAgentVelocity(entity));

            // After clearing the override, the steering resumes.
            api.ClearAgentVelocityOverride(entity);
            crowd.Update(0.016f, view);

            // Velocity should no longer be exactly the overridden value.
            Assert.NotEqual(overrideVel, crowd.GetAgentVelocity(entity));
        }

        // ── Test 14: Determinism - identical runs produce identical outputs ───────

        [Fact]
        public void Determinism_SameInputs_SameOutputs()
        {
            static FakeDtCrowdProvider BuildCrowd(Entity e1, Entity e2, Entity e3, TestView v)
            {
                var crowd = new FakeDtCrowdProvider();
                var p = new CrowdAgentParams
                {
                    Radius = 0.5f, Height = 1.8f, MaxSpeed = 5f,
                    MaxAcceleration = 10f, SeparationWeight = 2,
                };
                crowd.RegisterAgent(e1, p);
                crowd.RegisterAgent(e2, p);
                crowd.RegisterAgent(e3, p);
                crowd.SetAgentTarget(e1, new Vector3(10f, 0f, 0f));
                crowd.SetAgentTarget(e2, new Vector3(-10f, 0f, 0f));
                crowd.SetAgentTarget(e3, new Vector3(0f, 0f, 10f));
                return crowd;
            }

            var e1 = new Entity(1, 1);
            var e2 = new Entity(2, 1);
            var e3 = new Entity(3, 1);
            var view = new TestView();
            view.SetPosition(e1, new Vector3(0f, 0f, 0f));
            view.SetPosition(e2, new Vector3(1f, 0f, 0f));
            view.SetPosition(e3, new Vector3(0f, 0f, 1f));

            var c1 = BuildCrowd(e1, e2, e3, view);
            var c2 = BuildCrowd(e1, e2, e3, view);

            for (int i = 0; i < 10; i++)
            {
                c1.Update(0.016f, view);
                c2.Update(0.016f, view);
            }

            Assert.Equal(c1.GetAgentVelocity(e1), c2.GetAgentVelocity(e1));
            Assert.Equal(c1.GetAgentVelocity(e2), c2.GetAgentVelocity(e2));
            Assert.Equal(c1.GetAgentVelocity(e3), c2.GetAgentVelocity(e3));
        }

        // ── Test 15: Large agent count completes without NaN ──────────────────────

        [Fact]
        public void Update_LargeAgentCount_Completes()
        {
            var crowd = new FakeDtCrowdProvider();
            var view  = new TestView();
            var p = new CrowdAgentParams
            {
                Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f,
                MaxAcceleration = 10f, SeparationWeight = 2,
            };
            var entities = new Entity[200];
            for (int i = 0; i < 200; i++)
            {
                entities[i] = new Entity(i, 1);
                crowd.RegisterAgent(entities[i], p);
                crowd.SetAgentTarget(entities[i], new Vector3(100f, 0f, 100f));
                // Grid layout: 20 columns x 10 rows, 1m apart.
                float x = (i % 20) * 1f;
                float z = (i / 20) * 1f;
                view.SetPosition(entities[i], new Vector3(x, 0f, z));
            }

            for (int tick = 0; tick < 5; tick++)
                crowd.Update(0.016f, view);

            for (int i = 0; i < 200; i++)
            {
                var vel = crowd.GetAgentVelocity(entities[i]);
                Assert.False(float.IsNaN(vel.X), $"Entity {i} has NaN velocity X");
                Assert.False(float.IsNaN(vel.Z), $"Entity {i} has NaN velocity Z");
            }
            var api = (IFakeDtCrowdProviderTestApi)crowd;
            Assert.Equal(5, api.UpdateCallCount);
        }
    }
}
