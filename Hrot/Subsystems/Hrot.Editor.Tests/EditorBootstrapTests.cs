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
