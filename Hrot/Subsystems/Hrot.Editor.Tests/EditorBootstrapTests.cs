using Hrot.Editor;
using Xunit;

namespace Hrot.Editor.Tests;

public class EditorBootstrapTests
{
    [Fact]
    public void CreateFileService_ReturnsNonNullService()
    {
        var service = EditorBootstrap.CreateFileService();
        Assert.NotNull(service);
    }
}

// ==========================================================================
// DEBT-002: GizmoUiStateHub wired in EditorSubsystem composition root
// ==========================================================================

public class DEBT002_Editor_Tests
{
    // DEBT002_Editor: EditorSubsystem.GizmoUiHub is non-null after construction.
    // The field is initialised in the field declaration, so it does not require
    // Initialize() to be called.
    [Fact]
    public void DEBT002_Editor_GizmoUiHub_IsNonNull_AfterConstruction()
    {
        var sub = new EditorSubsystem();
        Assert.NotNull(sub.GizmoUiHub);
    }
}
