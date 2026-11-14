using Fdp.Toolkit.Runner;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐ O-7 B (CE-4418, docs/DESIGN_Terrain_Authoring.md §18e-2 B-4) — the forwarding rail of the silent-default rule: the REAL editor's constructed obstacle bake HAS the live overlay that shows its terrain blocks and features
/// without a save. The other hosts get none (railed in <c>Hrot.SimHost.Tests/EntityCreationPackRails</c>: only the editor's root assigns it, and a pack-built system has none by default).
/// 🔴 Red-proof: drop the assignment in <c>EditorNodeBootstrapper.RegisterSpawningPipeline</c> ⇒ this reds.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheEditorShowsItsTerrainBlocksLiveTests
{
    [Fact]
    public void O7B_TheRealEditorsConstructedObstacleBake_HasTheLiveOverlay()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            var bake = editor.EntityCreation!.ObstacleBakeSystem;
            Assert.NotNull(bake.LiveOverlay);
        }
        finally { editor.Shutdown(); }
    }
}
