using System;
using System.Collections.Generic;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using Xunit;

namespace Fdp.Examples.NetworkDemo.Tests.Systems
{
    public class TimeInputSystemTests
    {
        private class MockInputSource : IInputSource
        {
            public Queue<ConsoleKey> KeyQueue = new Queue<ConsoleKey>();

            public bool KeyAvailable => KeyQueue.Count > 0;

            public ConsoleKeyInfo ReadKey(bool intercept)
            {
                if (KeyQueue.Count == 0) return default;
                var k = KeyQueue.Dequeue();
                return new ConsoleKeyInfo((char)k, k, false, false, false);
            }
        }

        [Fact]
        public void TimeInput_InitializesSingleton()
        {
            using var repo = new EntityRepository();
            // Ensure registration is handled by system if possible, or verify system does it.
            // EntityRepository throws if SetSingleton used without Register.
            // So system MUST register it.

            var input = new MockInputSource();
            var system = new TimeInputSystem(input);

            system.Execute((ISimulationView)repo, 0.1f);

            Assert.True(repo.HasSingleton<TimeConfiguration>());
            var cfg = repo.GetSingleton<TimeConfiguration>();
            Assert.Equal(1.0f, cfg.TimeScale);
            Assert.False(cfg.IsPaused);
        }

        [Fact]
        public void TimeInput_Space_TogglesPause()
        {
            using var repo = new EntityRepository();
            // Pre-register for this test or let system do it
            repo.RegisterComponent<TimeConfiguration>();
            repo.SetSingleton(new TimeConfiguration { TimeScale = 1.0f, IsPaused = false });

            var input = new MockInputSource();
            input.KeyQueue.Enqueue(ConsoleKey.Spacebar);
            var system = new TimeInputSystem(input);

            system.Execute((ISimulationView)repo, 0.1f);

            var cfg = repo.GetSingleton<TimeConfiguration>();
            Assert.True(cfg.IsPaused);
            
            // Toggle back
            input.KeyQueue.Enqueue(ConsoleKey.Spacebar);
            system.Execute((ISimulationView)repo, 0.1f);
            cfg = repo.GetSingleton<TimeConfiguration>();
            Assert.False(cfg.IsPaused);
        }

        [Fact]
        public void TimeInput_Arrows_AdjustScale()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<TimeConfiguration>();
            repo.SetSingleton(new TimeConfiguration { TimeScale = 1.0f });

            var input = new MockInputSource();
            var system = new TimeInputSystem(input);

            // Right -> Increase
            input.KeyQueue.Enqueue(ConsoleKey.RightArrow);
            system.Execute((ISimulationView)repo, 0.1f);
            var cfg = repo.GetSingleton<TimeConfiguration>();
            Assert.True(cfg.TimeScale > 1.0f);

            // Left -> Decrease
            input.KeyQueue.Enqueue(ConsoleKey.LeftArrow); // Back to approx 1
            input.KeyQueue.Enqueue(ConsoleKey.LeftArrow); // Less than 1
            system.Execute((ISimulationView)repo, 0.1f);
            
            cfg = repo.GetSingleton<TimeConfiguration>();
            Assert.True(cfg.TimeScale < 1.0f);
        }
    }
}
