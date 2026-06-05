using CarKinem.Spatial;
using Xunit;

namespace Fdp.Tests;

public sealed class EntityRepositorySyncBorrowedSingletonTests
{
    [Fact]
    public void SyncFrom_DisposingSnapshot_DoesNotDisposeBorrowedSingleton()
    {
        using var live = new Fdp.Core.EntityRepository();
        live.SetSingleton(new SpatialGridData());

        var snapshot = new Fdp.Core.EntityRepository();
        snapshot.SyncFrom(live);
        snapshot.Dispose();

        var ex = Record.Exception(() => live.SetSingleton(new SpatialGridData()));
        Assert.Null(ex);

        live.GetSingleton<SpatialGridData>();
    }
}
