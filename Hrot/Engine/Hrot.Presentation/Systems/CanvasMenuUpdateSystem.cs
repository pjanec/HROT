using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.IG.Components;
using Hrot.UI.Common.AddEntity;

namespace Hrot.Presentation.Systems
{
    /// <summary>
    /// Evaluates domain rules and writes the canvas context-menu JSON into
    /// <see cref="CanvasContextMenuState"/> each frame.
    ///
    /// <para>The JSON is built once and cached; it is only rewritten when the
    /// relevant state changes. <c>CanvasContextMenuGizmo</c> reads the
    /// singleton and projects a <c>ContextMenuBinding</c> meta-primitive into
    /// the gizmo buffer keyed by anchor <c>-1L</c>.</para>
    ///
    /// <para>Items: <c>Add Entity ▸</c> (⭐ <c>CE-1017</c> S3 — only on a host whose
    /// <see cref="AddEntityAction"/> is available, greyed while authoring is suspended), then the
    /// Measurement Tool (action ID 200 = <c>GlobalActionIds.Measure</c>).</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class CanvasMenuUpdateSystem : IEcsModuleSystem
    {
        private static readonly object MeasureItem = new { id = 200, label = "Measurement Tool" };

        private readonly AddEntityAction? _addEntity;
        private (bool Available, string? Reason)? _builtFor;
        private string _json = "";

        /// <summary>Creates the system. <paramref name="addEntity"/> is the map's shared Add Entity action
        /// (<c>MapInteraction.AddEntity</c>); null ⇒ the menu offers no Add Entity.</summary>
        public CanvasMenuUpdateSystem(AddEntityAction? addEntity = null) => _addEntity = addEntity;

        /// <summary>The menu JSON for the current state (also what <see cref="Execute"/> publishes).</summary>
        public string CurrentJson()
        {
            var key = (_addEntity?.IsAvailable == true, _addEntity?.SuspendedReason);
            if (_builtFor != key)
            {
                var items = new List<object>(2);
                if (key.Item1) items.Add(AddEntityAction.MenuItem(key.Item2));
                items.Add(MeasureItem);
                _json = AddEntityAction.ToJson(items);
                _builtFor = key;
            }
            return _json;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            var repo = (EntityRepository)view;
            repo.SetSingletonManaged(new CanvasContextMenuState { MenuJson = CurrentJson() });
        }
    }
}
