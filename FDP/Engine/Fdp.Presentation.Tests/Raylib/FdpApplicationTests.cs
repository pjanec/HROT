using FluentAssertions;
using Raylib_cs;
using Xunit;
using System.Collections.Generic;

namespace Fdp.Presentation.Raylib.Tests;

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

    // ⭐⭐⭐ RequiresDisplayFact, not Fact (2026-09-10). `app.Run()` calls Raylib.InitWindow, which
    //   SEGFAULTS without a GL context — and a segfault aborts the WHOLE RUN, hiding every test ordered
    //   after it. That is one of the two crash sites behind CE-259aa. ⛔ Do not "fix" this by deleting
    //   the test: it is the only rail on the application lifecycle ORDER and it passes on a machine with
    //   a display. See RequiresDisplayFactAttribute for the reasoning.
    [RequiresDisplayFact]
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
