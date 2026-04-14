using System;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Fdp.Kernel;

namespace Fdp.Benchmarks
{
    [MemoryDiagnoser]
    public class CommandBufferPlaybackBenchmarks
    {
        private EntityRepository _repo;
        private EntityCommandBuffer _cmd;
        private const int UpdateCount = 5000;
        private Entity[] _entities;
        
        [GlobalSetup]
        public void Setup()
        {
            _repo = new EntityRepository();
            
            // Register components
            _repo.RegisterComponent<Position>();
            _repo.RegisterComponent<Velocity>();
            // Note: In real app, there are 100+ components, making Dictionary slower.
            // Current benchmark has only 2, so Dictionary is very fast.
            // But Array access is O(1) regardless of count.
            
            _entities = new Entity[UpdateCount];
            // Create entities
            for (int i = 0; i < UpdateCount; i++)
            {
                _entities[i] = _repo.CreateEntity();
                _repo.AddComponent(_entities[i], new Position { X = i, Y = i });
            }
            
            // Pre-allocate command buffer
            _cmd = new EntityCommandBuffer(UpdateCount * 32); 
        }
        
        [Benchmark]
        public void PlaybackBenchmark_5000Updates()
        {
            _cmd.Clear();
            var pos = new Position { X = 1, Y = 1 };
            for (int i = 0; i < UpdateCount; i++)
            {
                _cmd.SetComponent(_entities[i], pos);
            }
            
            _cmd.Playback(_repo);
        }
    }
    
    struct Position
    {
        public float X, Y;
    }
    
    struct Velocity
    {
        public float Vx, Vy;
    }
    
    public class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<CommandBufferPlaybackBenchmarks>();
        }
    }
}
