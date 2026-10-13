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
        /// <summary>⭐ CE-3024 (E2) — load a TERRAIN ZONE across the cluster. Always enabled (design §9.7 ③b).</summary>
        public const int LoadZone          = 103;

        // --- Canvas-level tools ---
        public const int Measure       = 200;
        public const int PlaceEntity   = 201;
        public const int PlaceObstacle = 202;

        // --- Add Entity (CE-1017 S3, docs/DESIGN_Add_Entity_Picker.md D6) ---
        // The empty-map menu's "Add Entity" submenu: each opens the type picker, then arms the picked type's tool.
        public const int AddEntityFriendly = 203;
        public const int AddEntityHostile  = 204;
        public const int AddEntityNeutral  = 205;
        public const int AddMapGraphic     = 206;

        // --- Layer control ---
        public const int OpenLayerControl = 250;
        /// <summary>⭐ CE-1033 — View › 2-D / 3-D map: the animated switch, on every map host (<c>DESIGN_Map_3D_Mode.md</c> M12).</summary>
        public const int ToggleMap3D      = 253;
        /// <summary>⭐ CE-3171 — View › RTS Command Mode: arms the RTS command tool (LMB select, RMB move / attack, Esc leaves),
        /// on every LIVE map host (docs/DESIGN_Entity_Control.md §8a).</summary>
        public const int ToggleRtsCommand = 254;
        /// <summary>⭐ CE-3174 — the unit menu's "Possess (FPS)": this station takes the unit in first person (docs/DESIGN_Entity_Control.md
        /// §8d). ⚠ 255 measured free on backend, ui and behaviors 2026-10-11.</summary>
        public const int Possess          = 255;

        // --- AI Diagnostics (behav-diag-1) ---
        /// <summary>Toggle <c>DebugState.Behavior.EnableTraceBuffer</c> on the target entity.</summary>
        public const int ToggleAiTrace    = 251;
        /// <summary>Toggle <c>DebugState.Behavior.EmitToLog</c> on the target entity.</summary>
        public const int ToggleAiTraceLog = 252;

        // --- Gizmo pins (CE-3120, R-227) — toggle a gizmo family on ONE unit (DebugState.Ai) ---
        public const int PinGizmosPath       = 260;
        public const int PinGizmosPerception = 261;
        public const int PinGizmosContacts   = 262;
        public const int PinGizmosEqs        = 263;
        public const int PinGizmosUtility    = 264;
        public const int PinGizmosSquad      = 265;
        public const int PinGizmosAll        = 266;
        public const int UnpinGizmosAll      = 267;
        /// <summary>⭐ <c>CE-3136</c> — pin the action-status family (Channels). 268 free on backend, behaviors and ui, <c>2026-10-09</c>.</summary>
        public const int PinGizmosActions    = 268;

        // --- Designations (CE-2149, docs/DESIGN_Orders_Params_Entity_Classes.md §13.3) — the unit menu's Designate submenu: one pick
        //     each, stored per unit for a later order's missing [Required] field. ⚠ 470–474 measured free 2026-10-13 (orders use 400–463).
        public const int DesignateTarget = 470;
        public const int DesignatePoint  = 471;
        public const int DesignateRoute  = 472;
        public const int DesignateArea   = 473;
        public const int DesignateClear  = 474;

        // --- Order with options (CE-1059, §13.12 O5) — the unit menu's submenu opening the ORDER FORM for an order, prefilled:
        //     id = OrderWithOptionsBase + the order's index (OrderMenu.ActionBase + index ⇒ +80). ⚠ 480–543 measured free 2026-10-13.
        public const int OrderWithOptionsBase = 480;
        public const int OrderWithOptionsMax  = 64;
    }
}
