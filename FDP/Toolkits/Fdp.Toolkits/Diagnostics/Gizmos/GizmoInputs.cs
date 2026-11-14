using System;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ CE-1480 G5 (docs/DESIGN_Change_Gated_Systems.md) — what a stateless gizmo rule's PICTURE is a function of, declared by the rule itself
    /// (<see cref="IStatelessGizmo.Inputs"/>). A rule that declares them is drawn only on a frame where one moved; while none does,
    /// <c>StatelessGizmoSystem</c> re-appends the primitives it drew last (<c>ChangeGate</c> asks the world; the buffer is cleared every frame, so
    /// the picture is RE-EMITTED, never skipped). A rule that declares nothing is drawn every frame, exactly as before.
    /// <para>⛔ A declaration is a promise: the picture depends on NOTHING else (no clock, no selection, no setting, no component outside
    /// <see cref="Components"/>). Presence / absence of a component is a structural change and always redraws; list the components whose VALUES are read.</para>
    /// </summary>
    public sealed class GizmoInputs
    {
        /// <summary>The component types whose values the picture reads (the matched entities' own).</summary>
        public Type[] Components { get; }

        /// <summary>The picture depends on the map view (zoom, what is in view): a view change redraws it. A rule that is view-dependent is only held
        /// when the system's <c>ViewFilter</c> publishes a <see cref="IGizmoViewFilter.Version"/>.</summary>
        public bool DependsOnView { get; }

        /// <summary>Any other input as a number that changes whenever it does (a setting): compared with the value seen last frame. Null = none.</summary>
        public Func<long>? Version { get; }

        /// <summary>The rule asks <c>IDebugDrawBuilder.IsLayerActive</c> (draws nothing on a hidden layer): toggling a layer redraws it. Needs a
        /// <see cref="DebugPrimitiveBuffer"/>; the buffer's <c>LayersVersion</c> is the input.</summary>
        public bool ReadsLayerState { get; init; }

        public GizmoInputs(bool dependsOnView, Func<long>? version, params Type[] components)
        {
            Components = components ?? Array.Empty<Type>();
            DependsOnView = dependsOnView;
            Version = version;
        }
    }
}
