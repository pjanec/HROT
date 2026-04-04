using System.Threading.Tasks;
using Fdp.Kernel;
using Hrot.Editor;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.Editor.Tests;

public class FeatureSwitchTests
{
    private static EditorApplication BuildMinimalApp()
    {
        var world       = new EntityRepository();
        var fileService = EditorBootstrap.CreateFileService();
        // Minimal 3-arg constructor — no kernel, no packs (no-op mode)
        return new EditorApplication(fileService, world.Bus, world);
    }

    [Fact]
    public void InitialMode_IsInternal()
    {
        var app = BuildMinimalApp();
        Assert.Equal(SimHostMode.Internal, app.CurrentMode);
    }

    [Fact]
    public async Task SwitchToExternal_NullKernel_IsNoOp_AndDoesNotThrow()
    {
        var app = BuildMinimalApp();
        // Should complete synchronously without throwing
        await app.SwitchToExternalAsync();
        // Mode stays Internal because kernel is null (guard returns early)
        Assert.Equal(SimHostMode.Internal, app.CurrentMode);
    }

    [Fact]
    public async Task SwitchToInternal_NullKernel_IsNoOp_AndDoesNotThrow()
    {
        var app = BuildMinimalApp();
        await app.SwitchToInternalAsync();
        Assert.Equal(SimHostMode.Internal, app.CurrentMode);
    }
}
