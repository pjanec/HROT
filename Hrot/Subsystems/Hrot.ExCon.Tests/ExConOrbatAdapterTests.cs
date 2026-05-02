using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.Replication.Events;
using Hrot.Core.Mission;
using Hrot.Core.Network;
using Hrot.ExCon.Adapters;
using Hrot.Map.Common;
using Moq;
using Xunit;

namespace Hrot.ExCon.Tests;

public sealed class ExConOrbatAdapterTests
{
    private readonly Mock<IDerRepo>        _repo    = new();
    private readonly Mock<IExConLogic>     _logic   = new();
    private readonly Mock<ICommandGateway> _gateway = new();

    private ExConOrbatAdapter CreateAdapter()
        => new ExConOrbatAdapter(_repo.Object, _logic.Object, _gateway.Object);

    // CS021-T01
    [Fact]
    public void RequestAssignSubordinate_CallsSendUpdateAttributeAsync_WithCommanderIdPatch()
    {
        UpdateEntityAttributeCommand? captured = null;
        _gateway
            .Setup(g => g.SendUpdateAttributeAsync(It.IsAny<UpdateEntityAttributeCommand>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateEntityAttributeCommand, CancellationToken>((cmd, _) => captured = cmd)
            .Returns(Task.CompletedTask);

        var adapter = CreateAdapter();
        adapter.RequestAssignSubordinate(subordinateEntityId: 10, commanderEntityId: 5);

        _gateway.Verify(g => g.SendUpdateAttributeAsync(It.IsAny<UpdateEntityAttributeCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        _gateway.Verify(g => g.SendUpdateDescriptorAsync(It.IsAny<UpdateEntityDescriptorCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotNull(captured);
        Assert.Equal(10, (int)captured!.NetworkId);
        Assert.Contains("\"CommanderId\":5", captured.AttributePatchJson);
    }

    // CS021-T02
    [Fact]
    public void RequestRemoveSubordinate_CallsSendUpdateAttributeAsync_WithCommanderIdZero()
    {
        UpdateEntityAttributeCommand? captured = null;
        _gateway
            .Setup(g => g.SendUpdateAttributeAsync(It.IsAny<UpdateEntityAttributeCommand>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateEntityAttributeCommand, CancellationToken>((cmd, _) => captured = cmd)
            .Returns(Task.CompletedTask);

        var adapter = CreateAdapter();
        adapter.RequestRemoveSubordinate(subordinateEntityId: 10);

        Assert.NotNull(captured);
        Assert.Equal(10, (int)captured!.NetworkId);
        Assert.Contains("\"CommanderId\":0", captured.AttributePatchJson);
    }

    // CS021-T03
    [Fact]
    public void GetVisibleNodes_EntityWithNonZeroTkbType_CanAcceptSubordinatesTrue()
    {
        var repo = new DerRepo();
        var entity = repo.CreateEntity(1, TkbEntityTypes.Unit_InfantrySquad); // non-zero TkbType
        entity.SetDescriptor(new EntityInfoDescriptor
        {
            EntityId    = 1,
            Name        = "HQ",
            CommanderId = 0,
            Affiliation = eForceIdentifier.FORCE_FRIENDLY.ToString(),
        });

        _logic.Setup(l => l.IsEntityPendingDelete(It.IsAny<int>())).Returns(false);
        var adapter = new ExConOrbatAdapter(repo, _logic.Object, _gateway.Object);

        var nodes = adapter.GetVisibleNodes(string.Empty, new HashSet<int>());

        Assert.Single(nodes);
        Assert.True(nodes[0].CanAcceptSubordinates);
    }
}
