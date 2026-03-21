using Bagira.Map.Common.Components;
using Fdp.Toolkit.Tkb;

namespace Bagira.Map.Common;

/// <summary>
/// Extension helpers that bind <see cref="RoutePlan"/> (a managed-class component
/// from <c>Bagira.Map.Common</c>) to the TKB blueprints registered by
/// <c>BdcTkbCatalog</c> (in <c>Bagira.Map.Definitions</c>).
///
/// <para>
/// <c>BdcTkbCatalog</c> cannot reference <c>Bagira.Map.Common</c> directly
/// because that would introduce a circular project dependency.
/// Call <see cref="ApplyRoutePlanToBlueprint"/> once — after
/// <c>BdcTkbCatalog.RegisterAll()</c> — from every host (SimHost, IG) that
/// needs route entities to be spawned with a live <see cref="RoutePlan"/>.
/// </para>
/// </summary>
public static class RouteTkbExtensions
{
    /// <summary>
    /// Attaches a <see cref="RoutePlan"/> managed-component factory to the
    /// <c>TacGraphic_Route</c> (8802) TKB template.
    ///
    /// <para>
    /// Must be called <b>after</b> <c>BdcTkbCatalog.RegisterAll(tkbDb)</c>.
    /// The method is idempotent: calling it more than once is safe (the
    /// <c>preserveExisting</c> flag on <see cref="TkbTemplate.ApplyTo"/> ensures
    /// a second factory invocation cannot overwrite an already-set component).
    /// </para>
    /// </summary>
    public static void ApplyRoutePlanToBlueprint(TkbDatabase tkbDb)
    {
        if (tkbDb.TryGetByType(TkbEntityTypes.TacGraphic_Route, out var routeTemplate))
            routeTemplate.AddManagedComponent(() => new RoutePlan());
    }
}
