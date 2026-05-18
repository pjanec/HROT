```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7462/25H2/2025Update/HudsonValley2)
Intel Core i7-7700HQ CPU 2.80GHz (Kaby Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.101
  [Host]   : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                | Mean      | Error     | StdDev    | Allocated |
|---------------------- |----------:|----------:|----------:|----------:|
| SimpleSequence_Tick   |  30.13 ns |  79.04 ns |  4.333 ns |         - |
| ComplexTree_Tick      | 100.15 ns | 195.73 ns | 10.729 ns |         - |
| SimpleSequence_Resume |  21.88 ns |  15.49 ns |  0.849 ns |         - |
