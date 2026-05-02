using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.Map.Common.Tests;

/// <summary>
/// Unit tests for <see cref="Hrot.Map.Definitions.Behavior.Intents.DefendAreaIntentDto"/> (TASK-TI006).
/// </summary>
public class DefendAreaIntentDtoTests
{
    // SC-2: BehaviorCatalog includes "DefendArea" for MilitaryApc (AllMilitary covers it)
    [Fact]
    public void BehaviorCatalog_MilitaryApc_ContainsDefendArea()
    {
        var behaviors = BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.MilitaryApc);
        Assert.Contains("DefendArea", behaviors);
    }

    // SC-3: BehaviorCatalog does NOT include "DefendArea" for Civilian types
    [Fact]
    public void BehaviorCatalog_CivilianCar_DoesNotContainDefendArea()
    {
        var behaviors = BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.CivilianCar);
        Assert.DoesNotContain("DefendArea", behaviors);
    }
}
