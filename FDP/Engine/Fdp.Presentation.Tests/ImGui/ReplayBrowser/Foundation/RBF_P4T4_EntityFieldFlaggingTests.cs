using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Xunit;

namespace Fdp.Presentation.ReplayBrowser.Foundation;

/// <summary>
/// RBF-P4T4: EntityFieldParadoxHelper flagging logic tests (DESIGN §8.3).
/// </summary>
public sealed class RBF_P4T4_EntityFieldFlaggingTests
{
    [Fact]
    public void RBF_P4T4_NullEntityField_InMerged_ShouldFlag()
    {
        Assert.True(EntityFieldParadoxHelper.ShouldFlag(Entity.Null, isMergedView: true));
    }

    [Fact]
    public void RBF_P4T4_NullEntityField_InSingleNode_NoWarning()
    {
        Assert.False(EntityFieldParadoxHelper.ShouldFlag(Entity.Null, isMergedView: false));
    }

    [Fact]
    public void RBF_P4T4_NonNullEntityField_NoWarning()
    {
        var liveEntity = new Entity(1, 1);
        Assert.False(EntityFieldParadoxHelper.ShouldFlag(liveEntity, isMergedView: true));
    }

    [Fact]
    public void RBF_P4T4_TooltipMentionsBothCauses()
    {
        string tooltip = EntityFieldParadoxHelper.ParadoxTooltip;
        Assert.Contains("time offset", tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("desync", tooltip, StringComparison.OrdinalIgnoreCase);
    }
}
