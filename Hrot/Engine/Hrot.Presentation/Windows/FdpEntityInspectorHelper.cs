using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Utils;
using Fdp.Presentation.WindowManager;
using Hrot.Presentation.Facades;

namespace Hrot.Presentation.Windows;

/// <summary>
/// Shared helper for wiring the FDP entity inspector reflector and registering
/// the standard "Inspect..." context menu handler across subsystems.
/// Eliminates the repeated boilerplate block in each subsystem's
/// <c>RegisterWindows</c> implementation.
/// </summary>
public static class FdpEntityInspectorHelper
{
    /// <summary>
    /// Wires the four component-editor reflector settings on <paramref name="panel"/>
    /// and registers an "Inspect..." context menu handler that opens per-entity
    /// <see cref="EntityWatchPanel"/> windows via the window manager.
    /// </summary>
    /// <param name="panel">The inspector panel to configure.</param>
    /// <param name="windowManager">The window manager for spawning watch windows.</param>
    /// <param name="owningPerspective">
    ///   ⭐⭐ <b>The PERSPECTIVE the spawned watch windows belong to</b> (e.g. <c>"Scenario"</c>,
    ///   <c>"IG"</c>), and the lower-cased prefix of their window ids.
    ///   <para>⚠⚠ <b>Renamed from <c>ownerName</c>, `2026-08-23`, because the old name and its old doc
    ///   were both FALSE:</b> it said <i>"subsystem name shown in watch-window titles"</i> — 📐 the
    ///   titles are built from the ENTITY *(<c>"Watch Entity [i, vN]"</c>)* and never mention it, while
    ///   the value is in fact assigned to <c>Reflector.EditOwningPerspective</c> and passed as
    ///   <see cref="FdpEntityWatchWindow"/>'s <c>owningPerspective</c>. ⛔ A caller reading the old doc
    ///   would pass a subsystem name and silently spawn windows into a perspective nothing claims —
    ///   which is exactly what the <c>Editor</c>→<c>Scenario</c> rename exposed.</para>
    /// </param>
    /// <param name="sessionGetter">Callback that returns the current repository session.</param>
    /// <param name="pickBridge">Map-pick bridge for in-world component editing (may be null).</param>
    /// <param name="titleBarColor">Title-bar color for spawned watch windows.</param>
    public static void WireInspectorWithInspectContextMenu(
        EntityInspectorPanel       panel,
        WindowManager              windowManager,
        string                     owningPerspective,
        Func<IInspectableSession?> sessionGetter,
        MapPickServiceBridge?      pickBridge,
        Vector4?                   titleBarColor)
    {
        panel.Reflector.EditWindowManager     = windowManager;
        panel.Reflector.EditSessionGetter     = sessionGetter;
        panel.Reflector.EditOwningPerspective = owningPerspective;
        panel.Reflector.EditPickerContext     = pickBridge;

        string prefix = owningPerspective.ToLowerInvariant();
        panel.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
        {
            builder.AddItem("Inspect...", () =>
            {
                var session = sessionGetter();
                bool isSingleton = entity == RepositoryAdapter.SingletonEntity;
                long? netId = null;
                if (!isSingleton && session != null &&
                    session.HasComponent(entity, typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity)))
                {
                    var comp = session.GetComponent(entity, typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity));
                    if (comp is Fdp.Toolkit.Replication.Components.NetworkIdentity ni)
                        netId = ni.Value;
                }

                string title = isSingleton ? "Watch [Singletons]"
                    : netId.HasValue ? $"Watch Entity [{entity.Index}, v{entity.Generation}] ({netId.Value})"
                    : $"Watch Entity [{entity.Index}, v{entity.Generation}]";
                string id = $"{prefix}_watch_{entity.Index}_{entity.Generation}_{Guid.NewGuid()}";
                var watchPanel = new EntityWatchPanel(entity);
                watchPanel.Reflector.EditWindowManager     = windowManager;
                watchPanel.Reflector.EditSessionGetter     = sessionGetter;
                watchPanel.Reflector.EditOwningPerspective = owningPerspective;
                watchPanel.Reflector.EditPickerContext     = pickBridge;
                windowManager.RegisterWindow(new FdpEntityWatchWindow(
                    id, title, owningPerspective, watchPanel, sessionGetter, titleBarColor));
            });
        }));
    }
}
