using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Constants;
using Hrot.IG.Components;

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

        // ---- IStatelessGizmo --------------------------------------------------

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            long networkId = netId.Value;
            if (networkId == 0) return;

            // Select the menu permutation based on health state when available.
            string menuJson = MenuJsonHealthy;
            if (view.HasComponent<IgHealthState>(entity))
            {
                ref readonly var health = ref view.GetComponentRO<IgHealthState>(entity);
                if (health.Damage >= 50f)
                    menuJson = MenuJsonDegraded;
            }

            draw.DrawContextMenuBinding(networkId, menuJson);
        }
    }
}
