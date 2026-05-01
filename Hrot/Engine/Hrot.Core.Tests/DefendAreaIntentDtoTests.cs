using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.Map.Common.Tests;

/// <summary>
/// Unit tests for <see cref="Hrot.Map.Definitions.Doctrine.Intents.DefendAreaIntentDto"/> (TASK-TI006).
/// </summary>
public class DefendAreaIntentDtoTests
{
    // SC-2: DoctrineCatalog includes "DefendArea" for MilitaryApc (AllMilitary covers it)
    [Fact]
    public void DoctrineCatalog_MilitaryApc_ContainsDefendArea()
    {
        var doctrines = DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.MilitaryApc);
        Assert.Contains("DefendArea", doctrines);
    }

    // SC-3: DoctrineCatalog does NOT include "DefendArea" for Civilian types
    [Fact]
    public void DoctrineCatalog_CivilianCar_DoesNotContainDefendArea()
    {
        var doctrines = DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.CivilianCar);
        Assert.DoesNotContain("DefendArea", doctrines);
    }
}
