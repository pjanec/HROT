using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;

[SimpleJob(RuntimeMoniker.Net80)]
[MemoryDiagnoser]
[MarkdownExporter]
public unsafe class HsmBenchmarks
{
    private HsmDefinitionBlob _shallowBlob;
    private HsmDefinitionBlob _deepBlob;
    private HsmDefinitionBlob _flatBlob;
    private HsmInstance64[] _instances64;
    private HsmInstance128[] _instances128;
    private HsmInstance256[] _instances256;
    private int _context;
    
    [Params(1, 10, 100, 1000, 10000)]
    public int InstanceCount;
    
    [GlobalSetup]
    public void Setup()
    {
        // Build Shallow Machine (2 states, 1 transition)
        _shallowBlob = BuildShallowMachine();
        
        // Build Deep Machine (16 states, nested 8 levels deep)
        _deepBlob = BuildDeepMachine();
        
        // Build Flat Machine (100 states, all siblings)
        _flatBlob = BuildFlatMachine();
        
        // Initialize instances
        _instances64 = new HsmInstance64[InstanceCount];
        _instances128 = new HsmInstance128[InstanceCount];
        _instances256 = new HsmInstance256[InstanceCount];
        
        _context = 0;
        
        for (int i = 0; i < InstanceCount; i++)
        {
            fixed (HsmInstance64* ptr = &_instances64[i])
                HsmInstanceManager.Initialize(ptr, _shallowBlob);
            fixed (HsmInstance128* ptr = &_instances128[i])
                HsmInstanceManager.Initialize(ptr, _shallowBlob);
            fixed (HsmInstance256* ptr = &_instances256[i])
                HsmInstanceManager.Initialize(ptr, _shallowBlob);
        }
    }
    
    [Benchmark(Description = "Shallow_Tier64_Idle")]
    public void ShallowMachine_Tier64_IdleUpdate()
    {
        HsmKernel.UpdateBatch(_shallowBlob, _instances64.AsSpan(), _context, 0.016f);
    }
    
    [Benchmark(Description = "Shallow_Tier64_Transition")]
    public void ShallowMachine_Tier64_WithTransition()
    {
        // Queue event to trigger transition
        for (int i = 0; i < InstanceCount; i++)
        {
            fixed (HsmInstance64* ptr = &_instances64[i])
            {
                HsmEventQueue.TryEnqueue(ptr, 64, new HsmEvent { EventId = 1 });
            }
        }
        
        HsmKernel.UpdateBatch(_shallowBlob, _instances64.AsSpan(), _context, 0.016f);
    }
    
    [Benchmark(Description = "Deep_Tier64_Idle")]
    public void DeepMachine_Tier64_IdleUpdate()
    {
        HsmKernel.UpdateBatch(_deepBlob, _instances64.AsSpan(), _context, 0.016f);
    }
    
    [Benchmark(Description = "Shallow_Tier128_Idle")]
    public void ShallowMachine_Tier128_IdleUpdate()
    {
        HsmKernel.UpdateBatch(_shallowBlob, _instances128.AsSpan(), _context, 0.016f);
    }
    
    [Benchmark(Description = "Shallow_Tier256_Idle")]
    public void ShallowMachine_Tier256_IdleUpdate()
    {
        HsmKernel.UpdateBatch(_shallowBlob, _instances256.AsSpan(), _context, 0.016f);
    }
    
    [Benchmark(Description = "Flat_100States_Idle")]
    public void FlatMachine_100States_IdleUpdate()
    {
        HsmKernel.UpdateBatch(_flatBlob, _instances64.AsSpan(), _context, 0.016f);
    }
    
    [Benchmark(Description = "EventQueue_Enqueue")]
    public void EventQueue_EnqueueBenchmark()
    {
        for (int i = 0; i < InstanceCount; i++)
        {
            fixed (HsmInstance64* ptr = &_instances64[i])
            {
                HsmEventQueue.TryEnqueue(ptr, 64, new HsmEvent { EventId = 1 });
            }
        }
    }
    
    private HsmDefinitionBlob BuildShallowMachine()
    {
        var builder = new HsmBuilder("Shallow");
        var idle = builder.State("Idle").Initial();
        var active = builder.State("Active");
        idle.On(1).GoTo(active);
        active.On(2).GoTo(idle);
        
        var graph = builder.Build();
        HsmNormalizer.Normalize(graph);
        HsmGraphValidator.Validate(graph);
        var flattened = HsmFlattener.Flatten(graph);
        return HsmEmitter.Emit(flattened);
    }
    
    private HsmDefinitionBlob BuildDeepMachine()
    {
        var builder = new HsmBuilder("Deep");
        var current = builder.State("Level0").Initial();
        
        for (int i = 1; i < 8; i++)
        {
            StateBuilder next = null;
            current.Child($"Level{i}", c => {
                c.Initial();
                next = c;
            });
            current = next;
        }
        
        var graph = builder.Build();
        HsmNormalizer.Normalize(graph);
        HsmGraphValidator.Validate(graph);
        var flattened = HsmFlattener.Flatten(graph);
        return HsmEmitter.Emit(flattened);
    }
    
    private HsmDefinitionBlob BuildFlatMachine()
    {
        var builder = new HsmBuilder("Flat");
        var states = new StateBuilder[100];
        
        for (int i = 0; i < 100; i++)
        {
            states[i] = builder.State($"State{i}");
            if (i == 0) states[i].Initial();
        }
        
        var graph = builder.Build();
        HsmNormalizer.Normalize(graph);
        HsmGraphValidator.Validate(graph);
        var flattened = HsmFlattener.Flatten(graph);
        return HsmEmitter.Emit(flattened);
    }
}
