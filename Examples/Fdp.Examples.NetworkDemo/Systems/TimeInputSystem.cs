using System;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.ModuleHost_Core.Network;
using FDP.Toolkit.Time.Messages;
using Fdp.ModuleHost_Core.Time;

namespace Fdp.Examples.NetworkDemo.Systems
{
    public interface IInputSource
    {
        bool KeyAvailable { get; }
        ConsoleKeyInfo ReadKey(bool intercept);
    }

    public class ConsoleInputSource : IInputSource
    {
        public bool KeyAvailable => Console.KeyAvailable;
        public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
    }

    public class NullInputSource : IInputSource
    {
        public bool KeyAvailable => false;
        public ConsoleKeyInfo ReadKey(bool intercept) => default;
    }

    [UpdateInPhase(SystemPhase.Input)]
    public class TimeInputSystem : IEcsModuleSystem
    {
        private readonly IInputSource _input;
        private readonly FdpEventBus? _eventBus;

        public TimeInputSystem(IInputSource? input = null, FdpEventBus? eventBus = null)
        {
            _input = input ?? new ConsoleInputSource();
            _eventBus = eventBus;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;

            // Ensure Registered
            try 
            { 
               repo.RegisterComponent<TimeConfiguration>(); 
            } 
            catch { }

            TimeConfiguration config;
            if (repo.HasSingleton<TimeConfiguration>())
            {
                config = repo.GetSingleton<TimeConfiguration>();
            }
            else
            {
                config = new TimeConfiguration { TimeScale = 1.0f, IsPaused = false };
                repo.SetSingleton(config);
            }

            bool changed = false;
            while (_input.KeyAvailable)
            {
                var key = _input.ReadKey(true).Key;
                switch (key)
                {
                    case ConsoleKey.Spacebar:
                        config.IsPaused = !config.IsPaused;
                        changed = true;
                        break;
                    case ConsoleKey.RightArrow:
                        config.TimeScale += 0.5f;
                        changed = true;
                        break;
                    case ConsoleKey.LeftArrow:
                        config.TimeScale -= 0.5f;
                        if (config.TimeScale < 0.1f) config.TimeScale = 0.1f;
                        changed = true;
                        break;
                    case ConsoleKey.R:
                        config.TimeScale = 1.0f;
                        changed = true;
                        break;
                    case ConsoleKey.T:
                        if (_eventBus != null)
                        {
                            _eventBus.Publish(new SwitchTimeModeEvent 
                            { 
                                TargetMode = TimeMode.Deterministic,
                                BarrierWallTicks = 0  // coordinator will override
                            });
                        }
                        break;
                }
            }

            if (changed)
            {
                repo.SetSingleton(config);
            }
        }
    }
}
