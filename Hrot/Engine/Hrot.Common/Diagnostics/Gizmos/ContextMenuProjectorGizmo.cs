using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Constants;
using Fdp.Toolkit.Combat.Components;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// Emits a <see cref="Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.ContextMenuBinding"/>
    /// meta-primitive for every networked entity so that the IG dumb terminal can present a
    /// right-click context menu via the gizmo stream.
    ///
    /// Menu JSON strings are pre-serialised as static fields - one per distinct entity state
    /// permutation - and interned in the <see cref="Fdp.Toolkit.Diagnostics.Gizmos.StringInternMap"/>
    /// on the first frame each string is encountered. After that, only the 4-byte FNV-1a hash
    /// travels with every DebugPrimitive; the full string is cached on both sides.
    ///
    /// Currently two permutations are defined:
    /// <list type="bullet">
    ///   <item>Healthy   - unit is combat-effective (health &gt;= 50 %)</item>
    ///   <item>Degraded  - unit has taken significant damage (health &lt; 50 %)</item>
    /// </list>
    /// </summary>
    [GizmoProjector(typeof(NetworkIdentity))]
    public sealed class ContextMenuProjectorGizmo : IStatelessGizmo
    {
        // ---- Pre-serialised menu permutations (built once at class-load time) ----

        private static readonly JsonSerializerOptions SerializerOptions =
            new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            };

        /// <summary>Menu for a combat-effective (healthy) unit.</summary>
        private static readonly string MenuJsonHealthy = JsonSerializer.Serialize(
            new ContextMenuItemDto[]
            {
                new ContextMenuItemDto { Id = GlobalActionIds.MoveHere,       Label = "Move Here",    Shortcut = "M" },
                new ContextMenuItemDto { Id = GlobalActionIds.Engage,          Label = "Engage",       Shortcut = "E" },
                new ContextMenuItemDto { Id = GlobalActionIds.Stop,            Label = "Stop",         Shortcut = "S" },
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.CenterOnEntity,  Label = "Center View",  Shortcut = "C" },
                new ContextMenuItemDto { Id = GlobalActionIds.Select,          Label = "Select",       Shortcut = "Space" },
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.Rotate,          Label = "Rotate",       Shortcut = "R" },
            }, SerializerOptions);

        /// <summary>Menu for a significantly damaged unit (health &lt; 50 %).</summary>
        private static readonly string MenuJsonDegraded = JsonSerializer.Serialize(
            new ContextMenuItemDto[]
            {
                new ContextMenuItemDto { Id = GlobalActionIds.MoveHere, Label = "Move Here",  Enabled = false,
                    Tooltip = "Cannot move: Unit is heavily damaged" },
                new ContextMenuItemDto { Id = GlobalActionIds.Engage,   Label = "Engage",     Enabled = false,
                    Tooltip = "Cannot engage: Unit is heavily damaged" },
                new ContextMenuItemDto { Id = GlobalActionIds.Stop,           Label = "Stop",       Shortcut = "S" },
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.CenterOnEntity, Label = "Center View", Shortcut = "C" },
                new ContextMenuItemDto { Id = GlobalActionIds.Select,         Label = "Select",      Shortcut = "Space" },
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.Rotate,         Label = "Rotate",      Shortcut = "R" },
            }, SerializerOptions);

        /// <summary>Menu for a tactical graphics area overlay.</summary>
        private static readonly string MenuJsonArea = JsonSerializer.Serialize(
            new ContextMenuItemDto[]
            {
                new ContextMenuItemDto { Id = GlobalActionIds.CenterOnEntity, Label = "Center View",  Shortcut = "C" },
                new ContextMenuItemDto { Id = GlobalActionIds.Select,         Label = "Select",       Shortcut = "Space" },
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.EditOverlay,    Label = "Edit Shape",   Shortcut = "E" },
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.Delete,         Label = "Delete",       Style = "destructive" },
            }, SerializerOptions);

        /// <summary>Menu for a tactical route graphic.</summary>
        private static readonly string MenuJsonRoute = JsonSerializer.Serialize(
            new ContextMenuItemDto[]
            {
                new ContextMenuItemDto { Id = GlobalActionIds.CenterOnEntity, Label = "Center View",  Shortcut = "C" },
                new ContextMenuItemDto { Id = GlobalActionIds.Select,         Label = "Select",       Shortcut = "Space" },
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.EditRoute,      Label = "Edit Route",   Shortcut = "E" },
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.Delete,         Label = "Delete",       Style = "destructive" },
            }, SerializerOptions);

        // ---- IStatelessGizmo --------------------------------------------------

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            long networkId = netId.Value;
            if (networkId == 0) return;

            string menuJson;

            if (view.HasManagedComponent<EditablePolyline>(entity))
            {
                menuJson = MenuJsonArea;
            }
            else if (view.HasManagedComponent<RoutePlan>(entity))
            {
                menuJson = MenuJsonRoute;
            }
            else
            {
                // Select the menu permutation based on health state when available.
                // ⭐ CE-196 — derived from Health.Current/Max, not from a precomputed damage percentage.
                //   The threshold is unchanged: "degraded" is 50% or more damage, i.e. at or below half
                //   health. ⚠ Max <= 0 leaves the menu HEALTHY rather than dividing by zero.
                menuJson = MenuJsonHealthy;
                if (view.HasComponent<Health>(entity))
                {
                    ref readonly var health = ref view.GetComponentRO<Health>(entity);
                    if (health.Max > 0f && health.Current / health.Max <= 0.5f)
                        menuJson = MenuJsonDegraded;
                }
            }

            draw.DrawContextMenuBinding(networkId, menuJson);
        }
    }
}
