namespace Hrot.Common.Constants
{
    // Shared numeric identifiers for context menu / operator actions that flow
    // from the IG terminal (ContextMenuProjectorGizmo) to the SimHost
    // (ContextActionIngressSystem -> GlobalActionDispatchSystem).
    //
    // Values MUST stay in sync with the Id fields emitted by
    // ContextMenuProjectorGizmo and with the action IDs handled by
    // HandleContextMenuActionById in IgApplication.
    public static class GlobalActionIds
    {
        // --- IG context menu: tactical orders ---
        public const int MoveHere       = 1;
        public const int Engage         = 2;
        public const int Stop           = 3;

        // --- IG context menu: view / selection ---
        public const int CenterOnEntity = 10;
        public const int Select         = 11;
        public const int Properties     = 12;
        public const int Delete         = 13;
        public const int Teleport       = 14;

        // --- IG context menu: gizmo tools ---
        public const int Rotate         = 20;
        public const int Repair         = 21;
        public const int Reinforce      = 22;
        public const int Resupply       = 23;
        public const int Transfer       = 24;

        // --- Editor / overlay actions ---
        public const int EditOverlay       = 100;
        public const int EditRoute         = 101;
        public const int EditPersonalRoute = 102;

        // --- Canvas-level tools ---
        public const int Measure       = 200;
        public const int PlaceEntity   = 201;
        public const int PlaceObstacle = 202;

        // --- Layer control ---
        public const int OpenLayerControl = 250;

        // --- AI Diagnostics (behav-diag-1) ---
        /// <summary>Toggle <c>DebugState.Behavior.EnableTraceBuffer</c> on the target entity.</summary>
        public const int ToggleAiTrace    = 251;
        /// <summary>Toggle <c>DebugState.Behavior.EmitToLog</c> on the target entity.</summary>
        public const int ToggleAiTraceLog = 252;
    }
}
