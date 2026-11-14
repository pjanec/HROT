using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Systems;
using Xunit;

namespace Fdp.Toolkits.Tests.Perception
{
    /// <summary>⭐ B-5 S3 — <see cref="SeedTargetSystem"/>, the one applier of <see cref="SeedTargetCommand"/> (re-homed from the retired editor system's tests).</summary>
    public sealed unsafe class SeedTargetSystemTests : IDisposable
    {
        private readonly EntityRepository _world;

        public SeedTargetSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<TargetMemory>();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterEvent<SeedTargetCommand>();
        }

        public void Dispose() => _world.Dispose();

        [Fact]
        public void SeedTarget_ValidPerceiverAndTarget_AddsToTargetMemory()
        {
            var perceiver = _world.CreateEntity();
            var target = _world.CreateEntity();
            _world.AddComponent(perceiver, new TargetMemory());
            _world.SetAuthority<TargetMemory>(perceiver, true);   // the node that owns the memory applies the seed
            _world.AddComponent(target, new SimTransform { Position = new Vector3(10f, 20f, 0f) });

            _world.Bus.Publish(new SeedTargetCommand { Perceiver = perceiver, Target = target, ScoreBoost = 5.0f });
            _world.Bus.SwapBuffers();
            new SeedTargetSystem().Execute(_world, 0f);

            ref readonly var mem = ref _world.GetComponent<TargetMemory>(perceiver);
            Assert.Equal(1, mem.Count);
            Assert.Equal((long)target.PackedValue, mem.EntityIds[0]);
        }

        [Fact]
        public void SeedTarget_DeadPerceiver_SkipsSilently()
        {
            var perceiver = _world.CreateEntity();
            var target = _world.CreateEntity();
            _world.AddComponent(target, new SimTransform());
            _world.DestroyEntity(perceiver);

            _world.Bus.Publish(new SeedTargetCommand { Perceiver = perceiver, Target = target, ScoreBoost = 1.0f });
            _world.Bus.SwapBuffers();

            Assert.Null(Record.Exception(() => new SeedTargetSystem().Execute(_world, 0f)));
        }
    }
}
