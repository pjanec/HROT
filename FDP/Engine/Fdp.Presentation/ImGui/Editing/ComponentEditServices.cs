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
        public static ComponentEditServiceBuilder CreateBuilder()
            => new ComponentEditServiceBuilder()
                .RegisterFieldEditor<Fdp.Core.FixedString32>(new FixedString32FieldEditor())
                .RegisterFieldEditor<Fdp.Core.FixedString64>(new FixedString64FieldEditor())
                .RegisterFieldEditor<Fdp.Core.FixedString128>(new FixedString128FieldEditor())
                .RegisterFieldEditor<Quaternion>(new QuaternionEulerFieldEditor())
                .RegisterFieldEditor<Fdp.Toolkit.Replication.EntityRef>(new PickableLeafFieldEditor(typeof(Fdp.Toolkit.Replication.EntityRef)))
                .RegisterFieldEditor<Fdp.Toolkit.Behavior.Params.PickableGeoPoint>(new PickableLeafFieldEditor(typeof(Fdp.Toolkit.Behavior.Params.PickableGeoPoint)))
                // ⭐ CE-2068 — a utility decision reference is pickable by its TYPE (R-184): one leaf, drawn as a catalog combo.
                .RegisterFieldEditor<Fdp.Toolkit.Utility.UtilityDecisionRef>(new PickableLeafFieldEditor(typeof(Fdp.Toolkit.Utility.UtilityDecisionRef)))
                .RegisterFieldEditor<Guid>(new StructEdit.Reflection.Editors.GuidFieldEditor());
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
        public bool TryConsumeLocationPick(string jsonPath, out Fdp.Toolkit.Behavior.Params.PickableGeoPoint location)
            => _inner.TryConsumeLocationPick(Path(jsonPath), out location);
        public bool CanListEntities => _inner.CanListEntities;   // ⭐ CE-1059
        public void RequestEntityListPick(string jsonPath, string[]? filterPresets) => _inner.RequestEntityListPick(Path(jsonPath), filterPresets);
    }
}
