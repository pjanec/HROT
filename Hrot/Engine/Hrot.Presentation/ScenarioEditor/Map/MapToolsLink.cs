using System;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐ B-17 P1 (📄 docs/UX/DESIGN_Map_Interaction.md §2.4) — a map canvas's way to its tool controller (the one <c>MapInteractionPack.Build</c> made
    /// for the canvas's world), put on the canvas by <c>MapInteractionPack.AttachMapLayers</c> on every map host. The status line
    /// (<see cref="Hrot.UI.Common.Panels.ToolStatusSection"/>) reads it, so it needs no per-host wiring.
    /// </summary>
    public sealed class MapToolsLink
    {
        private readonly Func<Hrot.ScenarioEditor.Tools.ToolController?> _tools;
        public MapToolsLink(Func<Hrot.ScenarioEditor.Tools.ToolController?> tools) => _tools = tools ?? throw new ArgumentNullException(nameof(tools));

        /// <summary>The controller now (null when the world has none — a host with no map interaction).</summary>
        public Hrot.ScenarioEditor.Tools.ToolController? Tools => _tools();
    }
}
