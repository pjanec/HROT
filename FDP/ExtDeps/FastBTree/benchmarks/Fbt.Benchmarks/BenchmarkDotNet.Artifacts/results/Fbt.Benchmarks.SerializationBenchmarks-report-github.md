```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7462/25H2/2025Update/HudsonValley2)
Intel Core i7-7700HQ CPU 2.80GHz (Kaby Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.101
  [Host]   : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method             | Mean       | Error      | StdDev     | Gen0   | Gen1   | Allocated |
|------------------- |-----------:|-----------:|-----------:|-------:|-------:|----------:|
| CompileSimpleTree  |   7.407 μs |   2.700 μs |  0.1480 μs | 0.8545 | 0.0305 |   2.82 KB |
| CompileComplexTree |  17.130 μs |  70.479 μs |  3.8632 μs | 2.3804 | 0.0610 |   7.42 KB |
| SaveBinary         | 273.408 μs | 959.569 μs | 52.5972 μs | 0.9766 |      - |    4.3 KB |
| LoadBinary         | 189.237 μs | 598.294 μs | 32.7945 μs | 1.4648 |      - |   4.76 KB |
