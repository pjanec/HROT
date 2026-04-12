using FluentAssertions;
using Raylib_cs;
using Xunit;
using System.Collections.Generic;

namespace FDP.Framework.Raylib.Tests;

[Collection("Raylib")]
public class FdpApplicationTests
{
    private class TestApp : FdpApplication
    {
        public List<string> CallLog { get; } = new();

        public TestApp(ApplicationConfig config) : base(config) { }

        protected override void OnLoad()
        {
            CallLog.Add("OnLoad");
        }

        protected override void OnUpdate(float dt)
        {
            CallLog.Add("OnUpdate");
            Quit(); // Exit after first frame
        }

        protected override void OnDrawWorld()
        {
            CallLog.Add("OnDrawWorld");
        }

        protected override void OnDrawUI()
        {
            CallLog.Add("OnDrawUI");
        }

        protected override void OnUnload()
        {
            CallLog.Add("OnUnload");
            base.OnUnload();
        }
    }

    [Fact]
    public void FdpApplication_Run_CallsLifecycleMethods_InOrder()
    {
        // Arrange
        var config = new ApplicationConfig
        {
            WindowTitle = "Test Window",
            Width = 100,
            Height = 100
        };
        
        using var app = new TestApp(config);

        // Act
        app.Run();

        // Assert
        // Expected order: OnLoad -> OnUpdate -> OnDrawWorld -> OnDrawUI -> OnUnload
        app.CallLog.Should().ContainInOrder(new[]
        {
            "OnLoad",
            "OnUpdate",
            "OnDrawWorld",
            "OnDrawUI",
            "OnUnload"
        });
    }
}
