using Xunit;
using FDP.Toolkit.ImGui.Panels;
using ModuleHost.Core;
using ModuleHost.Core.Resilience;
using System.Collections.Generic;

namespace FDP.Toolkit.ImGui.Tests
{
    [Collection("ImGui Sequential")]
    public class SystemProfilerPanelTests
    {
        [Fact]
        public void Draw_WithEmptyStats_RunsWithoutException()
        {
            using var fixture = new ImGuiTestFixture();
            fixture.NewFrame();
            SystemProfilerPanel.Draw(new List<ModuleStats>());
            fixture.Render();
        }

        [Fact]
        public void Draw_WithStats_RunsWithoutException()
        {
            using var fixture = new ImGuiTestFixture();
            var stats = new List<ModuleStats>
            {
                new ModuleStats 
                { 
                    ModuleName = "TestModule", 
                    ExecutionCount = 10, 
                    CircuitState = CircuitState.Closed, 
                    FailureCount = 0 
                },
                new ModuleStats 
                { 
                    ModuleName = "FailedModule", 
                    ExecutionCount = 0, 
                    CircuitState = CircuitState.Open, 
                    FailureCount = 5 
                }
            };
            
            fixture.NewFrame();
            SystemProfilerPanel.Draw(stats);
            fixture.Render();
        }
    }
}
