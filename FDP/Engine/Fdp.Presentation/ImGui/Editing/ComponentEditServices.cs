using System;
using System.Numerics;
using StructEdit.Reflection;

namespace Fdp.Presentation.Editing
{
    /// <summary>
    /// ⭐⭐ <c>CE-1023</c> (📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.6b F1) — THE standard leaves every value form draws with:
    /// fixed strings, a quaternion as Euler angles, a <see cref="Guid"/>, and the picked types (<c>EntityRef</c>,
    /// <c>PickableGeoPoint</c>, <c>UtilityDecisionRef</c> — "the type makes a field pickable", DESIGN_Entity_Reference D5).
    /// <para>⛔ The list lived privately in <c>ComponentReflector</c>, so the behaviour params form's service (<c>AiFacetEditService</c>)
    /// had none of it: an <c>EntityRef</c> param was a container — drawn read-only, never picked. Every builder starts here.</para>
    /// </summary>
    public static class ComponentEditServices
    {
        /// <summary>A builder with the standard leaves registered — a host adds its own on top.</summary>
        /// <remarks>⭐ S3 (CE-3378) — the list lives in ONE place now, <see cref="Types.TypeCatalogue.Standard"/> (<c>StandardTypes</c>): the leaves AND their cells.</remarks>
        public static ComponentEditServiceBuilder CreateBuilder() => Types.TypeCatalogue.Standard.CreateBuilder();
    }

    /// <summary>
    /// ⭐ <c>CE-1023</c> (§7.6b F2) — one form's view of a SHARED pick context: the form keys a pick on its own
    /// <c>$.Member</c>, this rewrites it to <c>scope.Member</c>. ⛔ The broker holds ONE pending pick keyed by path, so two forms on
    /// one screen (a task and an SOP; N mission tasks) would otherwise answer each other's picks.
    /// </summary>
    public sealed class ScopedMapPickContext : IMapPickContext
    {
        private readonly IMapPickContext _inner;
        private readonly string _scope;

        /// <param name="inner">The shared context (the host's broker).</param>
        /// <param name="scope">A path such as <c>$.tasks[2]</c>; a form's <c>$.X</c> becomes <c>$.tasks[2].X</c>.</param>
        public ScopedMapPickContext(IMapPickContext inner, string scope)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        /// <summary>The shared path of a form path: <c>$</c> ⇒ the scope, <c>$.X</c> ⇒ <c>scope.X</c>.</summary>
        public string Path(string jsonPath)
            => jsonPath == "$" ? _scope
             : jsonPath.StartsWith("$", StringComparison.Ordinal) ? _scope + jsonPath.Substring(1)
             : _scope + "." + jsonPath;

        public bool IsPickPendingFor(string jsonPath) => _inner.IsPickPendingFor(Path(jsonPath));
        public void RequestEntityPick(string jsonPath, string[]? filterPresets) => _inner.RequestEntityPick(Path(jsonPath), filterPresets);
        public void RequestLocationPick(string jsonPath) => _inner.RequestLocationPick(Path(jsonPath));
        public bool TryConsumeEntityPick(string jsonPath, out Fdp.Toolkit.Replication.EntityRef picked)
            => _inner.TryConsumeEntityPick(Path(jsonPath), out picked);
        public bool TryConsumeLocationPick(string jsonPath, out Fdp.Toolkit.Replication.PointLink location)
            => _inner.TryConsumeLocationPick(Path(jsonPath), out location);
        public bool CanListEntities => _inner.CanListEntities;   // ⭐ CE-1059
        public void RequestEntityListPick(string jsonPath, string[]? filterPresets) => _inner.RequestEntityListPick(Path(jsonPath), filterPresets);
        public Fdp.Toolkit.Scenario.LiveRefScope? References => _inner.References;
        public bool CanShowOnMap => _inner.CanShowOnMap;   // ⭐ CE-4206 R3
        public MapPreview? Preview => _inner.Preview;   // ⭐ U-10 — a form's view shares the host's preview
        public void ShowOnMap(Fdp.Toolkit.Replication.EntityRef entity) => _inner.ShowOnMap(entity);
        public void ShowOnMap(Fdp.Core.WorldPoint point) => _inner.ShowOnMap(point);
        public void RequestAreaEdit(string jsonPath, Fdp.Toolkit.Replication.AreaLink start) => _inner.RequestAreaEdit(Path(jsonPath), start);   // ⭐ CE-4206 R2
        public bool TryConsumeAreaEdit(string jsonPath, out Fdp.Toolkit.Replication.AreaLink area) => _inner.TryConsumeAreaEdit(Path(jsonPath), out area);   // ⭐ CE-4206 — a form's view shares the host's scope
    }
}
