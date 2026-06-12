using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Tests for <see cref="WM.RegisterPerspectiveLabel"/> and
/// <see cref="WM.GetPerspectiveLabel"/> display-label overrides (MTB2-T5, BATCH-34).
/// The label is used in <see cref="WM.RenderPerspectiveMenu"/> for the item text;
/// <see cref="WM.SelectPerspective"/> continues to use the raw perspective id.
/// </summary>
[Collection("ImGui Sequential")]
public class PerspectiveLabelTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);

    public void Dispose() => _atlas.Dispose();

    private WM CreateManager() => new(_atlas);

    [Fact]
    public void PerspectiveLabel_OverridesDisplay_NotId()
    {
        var wm = CreateManager();

        wm.RegisterPerspectiveLabel("Editor", "Scenario");

        // Label overrides for the registered perspective.
        Assert.Equal("Scenario", wm.GetPerspectiveLabel("Editor"));

        // Unregistered perspectives fall back to the id.
        Assert.Equal("BTree", wm.GetPerspectiveLabel("BTree"));
    }

    [Fact]
    public void SelectPerspective_UsesId_NotLabel()
    {
        var wm = CreateManager();

        wm.RegisterPerspectiveLabel("Editor", "Scenario");

        // SelectPerspective uses the id, not the label.
        wm.SelectPerspective("Editor");

        Assert.True(wm.IsPerspectiveActive("Editor"));
        // The label "Scenario" is NOT a valid perspective.
        Assert.False(wm.IsPerspectiveActive("Scenario"));
    }
}
