using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Fdp.Examples.NetworkDemo.Tests.Infrastructure;
using NLog;

namespace Fdp.Examples.NetworkDemo.Tests.Scenarios
{
    [Collection("DdsIntegration")]
    public class InfrastructureTests
    {
        private readonly ITestOutputHelper _output;

        public InfrastructureTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Logging_Scope_Flows_Through_Async_And_Tasks()
        {
            using var env = new DistributedTestEnv(_output);
            await env.StartNodesAsync();
            
            // Nodes are started. They logged "Starting Node 100" and "Starting Node 200".
            // See NetworkDemoApp.InitializeAsync.
            
            env.AssertLogContains(100, "Starting Node 100");
            env.AssertLogContains(200, "Starting Node 200");
            
            // Negative assertions
            // Verify that Node 100 logs do not appear in Node 200 context and vice-versa
            AssertLogDoesNotContain(env, 200, "Starting Node 100");
            AssertLogDoesNotContain(env, 100, "Starting Node 200");
        }
        
        private void AssertLogDoesNotContain(DistributedTestEnv env, int nodeId, string message)
        {
            bool found = false;
            try 
            {
                env.AssertLogContains(nodeId, message);
                found = true;
            }
            catch
            {
                // Expected: not found
            }
            
            if (found)
            {
                 Assert.Fail($"Found forbidden message '{message}' appearing in context of Node {nodeId}");
            }
        }
    }
}
