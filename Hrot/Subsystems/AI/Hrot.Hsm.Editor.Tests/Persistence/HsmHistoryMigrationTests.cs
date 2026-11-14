using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Persistence;

/// <summary>Q84 C1/D1 — an old childless History pseudo-state folds into its parent composite's flag.</summary>
public sealed class HsmHistoryMigrationTests
{
    [Fact]
    public void Q84_APseudoState_BecomesItsParentsFlag_AndTransitionsRetarget()
    {
        var parent = new StateNodeDto { StableId = Guid.NewGuid(), Name = "Guard" };
        var idle   = new StateNodeDto { StableId = Guid.NewGuid(), Name = "Idle", ParentStableId = parent.StableId, IsInitial = true };
        var alert  = new StateNodeDto { StableId = Guid.NewGuid(), Name = "Alert", ParentStableId = parent.StableId };
        var pseudo = new StateNodeDto { StableId = Guid.NewGuid(), Name = "H", ParentStableId = parent.StableId, IsDeepHistory = true };
        parent.ChildStableIds.AddRange(new[] { idle.StableId, alert.StableId, pseudo.StableId });
        var dto = new HsmAssetDto { States = new List<StateNodeDto> { parent, idle, alert, pseudo } };
        dto.Transitions.Add(new TransitionNodeDto { SourceStableId = alert.StableId, TargetStableId = pseudo.StableId });

        Assert.Equal(1, HsmHistoryMigration.Apply(dto));
        Assert.Equal(0, HsmHistoryMigration.Apply(dto));          // idempotent

        Assert.True(parent.IsDeepHistory);
        Assert.DoesNotContain(dto.States, s => s.Name == "H");
        Assert.DoesNotContain(pseudo.StableId, parent.ChildStableIds);
        Assert.Equal(parent.StableId, dto.Transitions.Single().TargetStableId);
    }
}
