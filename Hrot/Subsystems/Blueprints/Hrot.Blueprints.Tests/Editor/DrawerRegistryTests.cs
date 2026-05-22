using Hrot.Blueprints.Editor.Inspector;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class DrawerRegistryTests
{
    // SC1
    [Fact]
    public void DrawerRegistry_Register_ThenTryGet_Returns_Drawer()
    {
        var registry = new DrawerRegistry();
        registry.Register<float>(new FloatDrawer());
        var found = registry.TryGet<float>(out var d);
        Assert.True(found);
        Assert.NotNull(d);
    }

    // SC2
    [Fact]
    public void DrawerRegistry_TryGet_Missing_ReturnsFalse()
    {
        var registry = new DrawerRegistry();
        var found = registry.TryGet<double>(out var d);
        Assert.False(found);
    }

    // SC3
    [Fact]
    public void DrawerRegistry_Register_Overwrite_Succeeds()
    {
        var registry = new DrawerRegistry();
        var first  = new FloatDrawer();
        var second = new FloatDrawer();
        registry.Register<float>(first);
        registry.Register<float>(second);
        registry.TryGet<float>(out var d);
        Assert.Same(second, d);
    }
}
