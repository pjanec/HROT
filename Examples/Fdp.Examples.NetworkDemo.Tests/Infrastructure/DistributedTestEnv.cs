using System;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using NLog;
using NLog.Config;
using Fdp.Examples.NetworkDemo;

namespace Fdp.Examples.NetworkDemo.Tests.Infrastructure
{
    public class DistributedTestEnv : IDisposable
    {
        public NetworkDemoApp NodeA { get; private set; } = default!;
        public NetworkDemoApp NodeB { get; private set; } = default!;
        
        private Task _taskA = default!;
        private Task _taskB = default!;
        private CancellationTokenSource _cts;
        private readonly ITestOutputHelper _output;
        private readonly TestLogCapture _logCapture;

        public DistributedTestEnv(ITestOutputHelper output)
        {
            _output = output;
            _cts = new CancellationTokenSource();
            
            // Setup in-memory logging
            _logCapture = new TestLogCapture 
            { 
                Layout = "${longdate}|${level:uppercase=true}|[${scopeproperty:NodeId}] ${logger:shortName}|${message}" 
            };
            
            var config = new LoggingConfiguration();
            // Default to Info, but usually for tests we want Trace/Debug?
            // "Ensure GenericDescriptorTranslator logs detailed authority checks (trace level)"
            // So we need Trace enabled.
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, _logCapture);
            LogManager.Configuration = config;
        }

        public async Task StartNodesAsync()
        {
            NodeA = new NetworkDemoApp();
            NodeB = new NetworkDemoApp();
            
            var uniqueId = Guid.NewGuid().ToString("N");
            var pathA = $"node_100_{uniqueId}.fdp";
            var pathB = $"node_200_{uniqueId}.fdp";
            
            var initTcsA = new TaskCompletionSource<bool>();
            var initTcsB = new TaskCompletionSource<bool>();

            _taskA = Task.Run(async () => {
                // Ensure context is fresh for this task
                using (ScopeContext.PushProperty("NodeId", 100))
                {
                    try {
                        await NodeA.InitializeAsync(100, false, pathA, autoSpawn: false);
                        initTcsA.SetResult(true);
                        await NodeA.RunLoopAsync(_cts.Token);
                    } catch (TaskCanceledException) {
                        initTcsA.TrySetCanceled();
                    }
                      catch (Exception ex) { 
                          _output.WriteLine($"Node 100 Error: {ex}");
                          initTcsA.TrySetException(ex);
                      }
                }
            });

            _taskB = Task.Run(async () => {
                await Task.Delay(100); // Stagger start
                using (ScopeContext.PushProperty("NodeId", 200))
                {
                    try {
                        await NodeB.InitializeAsync(200, false, pathB, autoSpawn: false);
                        initTcsB.SetResult(true);
                        await NodeB.RunLoopAsync(_cts.Token);
                    } catch (TaskCanceledException) {
                        initTcsB.TrySetCanceled();
                    }
                      catch (Exception ex) { 
                          _output.WriteLine($"Node 200 Error: {ex}");
                          initTcsB.TrySetException(ex);
                      }
                }
            });
            
            await Task.WhenAll(initTcsA.Task, initTcsB.Task);
        }

        public void AssertLogContains(int nodeId, string partialMessage)
        {
             var logs = _logCapture.Logs.ToArray();
             
             string nodeMarker = $"[{nodeId}]";
             bool found = logs.Any(l => l.Contains(nodeMarker) && l.Contains(partialMessage));
             
             if (!found) {
                 // Print all logs to output for debug
                 _output.WriteLine("=== LOGS START ===");
                 foreach(var l in logs) _output.WriteLine(l);
                 _output.WriteLine("=== LOGS END ===");
                 Assert.Fail($"Log message '{partialMessage}' for Node {nodeId} not found.");
             }
        }

        public async Task WaitForCondition(Func<bool> predicate, int timeoutMs = 30000)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            {
                if (predicate()) return;
                await Task.Delay(50);
            }
            throw new TimeoutException("Condition not met in time");
        }

        public async Task WaitForCondition(Func<NetworkDemoApp, bool> predicate, NetworkDemoApp target, int timeoutMs = 30000)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            {
                if (predicate(target)) return;
                await Task.Delay(50);
            }
            
            _output.WriteLine("=== TIMEOUT LOGS START ===");
            foreach(var l in _logCapture.Logs) _output.WriteLine(l);
            _output.WriteLine("=== TIMEOUT LOGS END ===");
            
            throw new TimeoutException("Condition not met in time");
        }

        public async Task RunFrames(int frames)
        {
            // Reduced from 33ms to 1ms to speed up tests significantly
            await Task.Delay(frames * 1);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _taskA?.Wait(1000); } catch { }
            try { _taskB?.Wait(1000); } catch { }
            NodeA?.Dispose();
            NodeB?.Dispose();
            _cts.Dispose();
        }
    }
}
