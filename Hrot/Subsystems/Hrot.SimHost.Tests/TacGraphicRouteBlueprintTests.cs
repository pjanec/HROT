using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Fdp.Toolkit.Tkb;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Tests for ROUTES1-T003: TKB Blueprint for TacGraphic_Route.
///
/// Verifies that <see cref="NedTkbCatalog.RegisterAll"/> correctly registers the
/// <c>TacGraphic_Route</c> blueprint.
/// </summary>
public class TacGraphicRouteBlueprintTests
{
    // ── Registration ─────────────────────────────────────────────────────────

    [Fact]
    public void TacGraphicRoute_IsRegisteredInTkbCatalog()
    {
        var db = BuildDatabase();
        Assert.True(db.TryGetByType(TkbEntityTypes.TacGraphic_Route, out _),
            "TacGraphic_Route (8802) must be registered in NedTkbCatalog.");
    }

    [Fact]
    public void TkbType_TacGraphicRoute_Is8802()
    {
        Assert.Equal(8802L, TkbEntityTypes.TacGraphic_Route);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TkbDatabase BuildDatabase()
    {
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        RouteTkbExtensions.ApplyRoutePlanToBlueprint(db);
        return db;
    }
}

