using Fdp.Toolkit.Runner;
using Hrot.Editor;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐ CE-4024 — the forwarding rail for the editor's entity-creation pack: it applies a request's <c>InitialAttributesJson</c>, as every
/// networked host does (its factory hands the pack the compiler). 📌 Measured 2026-10-17: the editor passed none, so a spawn's
/// <c>attributesJson</c> and a converted SUMO pedestrian's fright (AQ93 §15 K5) were silently dropped.
/// </summary>
public sealed class TheEditorAppliesRequestAttributesTests
{
    [Fact]
    public void TheEditorsCreationPack_AppliesRequestAttributes()
    {
        var subsystem = new EditorSubsystem();
        try
        {
            subsystem.Initialize(new SubsystemConfig { Headless = true });
            Assert.True(subsystem.EntityCreation!.RequestSystem.AppliesAttributes);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }
}
