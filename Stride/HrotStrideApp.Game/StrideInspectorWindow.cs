#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial;
using Raylib_cs;
using rlImGui_cs;
using ImGuiNET;

namespace HrotStrideApp;

/// <summary>
/// Configuration for the optional second raylib/ImGui inspector window
/// (STR-P5-T2, BATCH-22).
///
/// <para>
/// Enabled when the environment variable <c>STRIDE_EDITOR_WINDOW=1</c> is set, or by
/// setting <see cref="Enabled"/> explicitly before <see cref="StrideInspectorWindow"/> is
/// constructed.  Disabled by default so headless tests and CI are unaffected.
/// </para>
/// </summary>
public static class StrideInspectorWindowConfig
{
    /// <summary>
    /// Returns <c>true</c> if the inspector window should be opened.
    /// Checks the <c>STRIDE_EDITOR_WINDOW</c> environment variable (value "1" or "true",
    /// case-insensitive) unless overridden by setting <see cref="ForceEnabled"/>.
    /// </summary>
    public static bool IsEnabled =>
        ForceEnabled ??
        string.Equals(
            Environment.GetEnvironmentVariable("STRIDE_EDITOR_WINDOW") ?? string.Empty,
            "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            Environment.GetEnvironmentVariable("STRIDE_EDITOR_WINDOW") ?? string.Empty,
            "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// When non-null, overrides the environment variable check.
    /// Set to <c>true</c> to force the window open; <c>false</c> to force it closed.
    /// Useful in integration tests that need to assert on the view model without a window.
    /// </summary>
    public static bool? ForceEnabled { get; set; }
}

// ── View-model types (pure data, no window dependency) ──────────────────────

/// <summary>
/// One row in the entity list panel.  Pure data — no Raylib/ImGui dependency.
/// </summary>
public sealed class EntityRow
{
    /// <summary>The FDP entity handle.</summary>
    public Entity Entity { get; init; }

    /// <summary>
    /// The entity's TKB type (0 = unknown / component not present).
    /// Obtained from <see cref="TkbIdentity.TkbType"/> if the component is registered.
    /// </summary>
    public long TkbType { get; init; }

    /// <summary>
    /// Display name derived from TKB type.  Falls back to "Entity #{NetworkId}"
    /// when no type mapping is available.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The entity's FDP world position (from <see cref="SimTransform"/>).</summary>
    public System.Numerics.Vector3 Position { get; init; }

    /// <summary>The entity's network-assigned integer ID (from <see cref="NetworkIdentity"/>).</summary>
    public long NetworkId { get; init; }
}

/// <summary>
/// One component field shown in the inspector panel.  Pure data.
/// </summary>
public sealed class InspectorField
{
    public string Name  { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// The view model for the inspector panel.  Pure data populated by
/// <see cref="StrideInspectorViewModel.BuildInspector"/>.
/// </summary>
public sealed class InspectorViewModel
{
    /// <summary>Title row — entity name / type.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>All fields to display.</summary>
    public IReadOnlyList<InspectorField> Fields { get; init; } = Array.Empty<InspectorField>();
}

// ── Shared selection state (STR-P5-T3, BATCH-23) ────────────────────────────

/// <summary>
/// Shared, single-threaded selection state for the <c>editor_stride</c> dual-window pair.
///
/// <para>
/// The inspector window (raylib/ImGui, <see cref="StrideInspectorWindow"/>) is the <b>writer</b>:
/// clicking an entity row calls <see cref="Select"/> which stores the entity and bumps
/// <see cref="Version"/> so the reader knows something changed.
/// </para>
///
/// <para>
/// <see cref="EditorStrideSubsystem"/> / <see cref="StrideHrotGame"/> are <b>readers</b>:
/// they inspect <see cref="SelectedEntity"/> each frame and react (highlight gizmo +
/// <c>CenterOnEntityCommand</c>).
/// </para>
///
/// <para>
/// <b>Thread safety:</b> Both the Stride main thread and the inspector pump run on the same
/// host thread (BATCH-22 Option A), so no locking is required.
/// </para>
///
/// <para>
/// <b>Stale-entity protection:</b> call <see cref="ClearIfDead"/> each frame (pass the live
/// <see cref="EntityRepository"/>) to automatically clear the selection when the entity is
/// destroyed.
/// </para>
/// </summary>
public sealed class EditorSelectionState
{
    private Entity _selectedEntity = Entity.Null;

    /// <summary>
    /// The currently-selected FDP entity, or <see cref="Entity.Null"/> when nothing is selected.
    /// </summary>
    public Entity SelectedEntity => _selectedEntity;

    /// <summary>
    /// Monotonically-increasing counter.  Bumped every time <see cref="Select"/> or
    /// <see cref="Clear"/> changes the selection.  Readers can compare against their last-seen
    /// version to detect changes without polling the entity.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Returns <c>true</c> when an entity is selected (not <see cref="Entity.Null"/>).</summary>
    public bool HasSelection => _selectedEntity != Entity.Null;

    /// <summary>
    /// Sets the selected entity and bumps <see cref="Version"/>.
    /// Passing <see cref="Entity.Null"/> is equivalent to calling <see cref="Clear"/>.
    /// </summary>
    public void Select(Entity entity)
    {
        _selectedEntity = entity;
        Version++;
    }

    /// <summary>
    /// Clears the selection (sets to <see cref="Entity.Null"/>) and bumps <see cref="Version"/>.
    /// </summary>
    public void Clear()
    {
        if (_selectedEntity == Entity.Null) return; // already clear — don't bump version
        _selectedEntity = Entity.Null;
        Version++;
    }

    /// <summary>
    /// Checks whether the currently-selected entity is still alive in <paramref name="world"/>.
    /// If it is dead (or the world is null), the selection is cleared.
    /// Call once per frame from the host loop after the FDP kernel tick.
    /// </summary>
    public void ClearIfDead(Fdp.Core.EntityRepository? world)
    {
        if (_selectedEntity == Entity.Null) return;
        if (world == null || !world.IsAlive(_selectedEntity))
            Clear();
    }

    // ── CenterOnEntity request (one-shot flag) ────────────────────────────────

    private bool _centerRequested;

    /// <summary>
    /// Raises the "center on selected entity" request.  The next call to
    /// <see cref="ConsumeCenter"/> will return <c>true</c> and reset the flag.
    /// Safe to call when nothing is selected (the consumer checks
    /// <see cref="HasSelection"/> anyway).
    /// </summary>
    public void RequestCenter() => _centerRequested = true;

    /// <summary>
    /// Returns <c>true</c> once if a center request is pending, then resets the flag.
    /// Typically called from the Stride game loop; on <c>true</c> the caller should
    /// move the camera to frame the selected entity.
    /// </summary>
    public bool ConsumeCenter()
    {
        if (!_centerRequested) return false;
        _centerRequested = false;
        return true;
    }
}

// ── View model (pure logic, headless-testable) ───────────────────────────────

/// <summary>
/// Maps the live FDP world into display rows and inspector fields.
///
/// <para>
/// All logic is pure (no Raylib/ImGui dependency) so it can be unit-tested headlessly.
/// The window calls these methods from the rendering thread; tests call them directly.
/// </para>
/// </summary>
public static class StrideInspectorViewModel
{
    // Static TKB name table (matches UrbanCombat templates registered by
    // UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates).
    private static readonly Dictionary<long, string> s_tkbNames = new()
    {
        { 1001L, "CivilianPedestrian" },
        { 1002L, "CivilianCar"        },
        { 2001L, "MilitaryAPC"        },
        { 2002L, "InfantrySoldier"    },
        { 2003L, "Insurgent"          },
    };

    /// <summary>
    /// Builds the entity-list rows by querying all entities that have
    /// <see cref="NetworkIdentity"/> and <see cref="SimTransform"/>.
    ///
    /// <para>
    /// Safe to call with a null or empty world (returns empty list).
    /// No component is required beyond <c>NetworkIdentity</c> and <c>SimTransform</c>;
    /// optional components (<c>TkbIdentity</c>) are read with guards.
    /// </para>
    /// </summary>
    public static IReadOnlyList<EntityRow> BuildEntityList(EntityRepository? world)
    {
        if (world == null) return Array.Empty<EntityRow>();

        var rows = new List<EntityRow>();

        // Check if optional components are registered before querying them.
        bool hasTkbIdentity = world.IsComponentTypeRegistered<TkbIdentity>();
        bool hasNetworkId   = world.IsComponentTypeRegistered<NetworkIdentity>();
        bool hasSimTransform = world.IsComponentTypeRegistered<SimTransform>();

        if (!hasNetworkId || !hasSimTransform)
            return Array.Empty<EntityRow>();

        // Build the minimal query: entities with both NetworkIdentity and SimTransform.
        var query = world.Query()
            .With<NetworkIdentity>()
            .With<SimTransform>()
            .Build();

        foreach (var entity in query)
        {
            if (!world.IsAlive(entity)) continue;

            ref readonly var netId     = ref world.GetComponentRO<NetworkIdentity>(entity);
            ref readonly var transform = ref world.GetComponentRO<SimTransform>(entity);

            long tkbType = 0L;
            if (hasTkbIdentity && world.HasComponent<TkbIdentity>(entity))
            {
                ref readonly var tkbId = ref world.GetComponentRO<TkbIdentity>(entity);
                tkbType = tkbId.TkbType;
            }

            string displayName = BuildDisplayName(tkbType, netId.Value);

            rows.Add(new EntityRow
            {
                Entity      = entity,
                TkbType     = tkbType,
                DisplayName = displayName,
                Position    = new System.Numerics.Vector3(
                    transform.Position.X,
                    transform.Position.Y,
                    transform.Position.Z),
                NetworkId   = netId.Value,
            });
        }

        return rows;
    }

    /// <summary>
    /// Builds an <see cref="InspectorViewModel"/> for the given entity by reading its
    /// <see cref="SimTransform"/>, <see cref="SimVelocity"/>, and <see cref="NetworkIdentity"/>
    /// components.  Returns an empty model when the entity is not alive or components are absent.
    /// </summary>
    public static InspectorViewModel BuildInspector(EntityRepository? world, Entity entity)
    {
        if (world == null || !world.IsAlive(entity))
            return new InspectorViewModel { Title = "(no selection)" };

        var fields = new List<InspectorField>();

        // ── NetworkIdentity ──────────────────────────────────────────────────
        if (world.IsComponentTypeRegistered<NetworkIdentity>()
            && world.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var netId = ref world.GetComponentRO<NetworkIdentity>(entity);
            fields.Add(new InspectorField { Name = "NetworkId", Value = netId.Value.ToString() });
        }

        // ── TkbIdentity ──────────────────────────────────────────────────────
        long tkbType = 0L;
        if (world.IsComponentTypeRegistered<TkbIdentity>()
            && world.HasComponent<TkbIdentity>(entity))
        {
            ref readonly var tkbId = ref world.GetComponentRO<TkbIdentity>(entity);
            tkbType = tkbId.TkbType;
            fields.Add(new InspectorField { Name = "TkbType", Value = tkbType.ToString() });
        }

        // ── SimTransform ─────────────────────────────────────────────────────
        if (world.IsComponentTypeRegistered<SimTransform>()
            && world.HasComponent<SimTransform>(entity))
        {
            ref readonly var t = ref world.GetComponentRO<SimTransform>(entity);
            fields.Add(new InspectorField
            {
                Name  = "SimTransform.Position",
                Value = $"({t.Position.X:F2}, {t.Position.Y:F2}, {t.Position.Z:F2})",
            });
            var euler = QuaternionToEulerDeg(t.Rotation);
            fields.Add(new InspectorField
            {
                Name  = "SimTransform.Rotation",
                Value = $"yaw={euler.Y:F1}° pitch={euler.X:F1}° roll={euler.Z:F1}°",
            });
        }

        // ── SimVelocity ──────────────────────────────────────────────────────
        if (world.IsComponentTypeRegistered<SimVelocity>()
            && world.HasComponent<SimVelocity>(entity))
        {
            ref readonly var v = ref world.GetComponentRO<SimVelocity>(entity);
            float speed = v.Linear.Length();
            fields.Add(new InspectorField
            {
                Name  = "SimVelocity",
                Value = $"({v.Linear.X:F2}, {v.Linear.Y:F2}, {v.Linear.Z:F2}) |v|={speed:F2}",
            });
        }

        // ── NavigationStatus ─────────────────────────────────────────────────
        if (world.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.NavigationStatus>()
            && world.HasComponent<Fdp.Toolkit.Navigation.NavigationStatus>(entity))
        {
            ref readonly var nav = ref world.GetComponentRO<Fdp.Toolkit.Navigation.NavigationStatus>(entity);
            fields.Add(new InspectorField
            {
                Name  = "NavigationStatus",
                Value = $"{nav.Result} phase={nav.Phase}",
            });
        }

        // ── Authority ────────────────────────────────────────────────────────
        if (world.IsComponentTypeRegistered<SimTransform>())
        {
            bool owned = world.HasAuthority<SimTransform>(entity);
            fields.Add(new InspectorField { Name = "Authority(SimTransform)", Value = owned ? "OWNED" : "remote" });
        }

        string title = BuildDisplayName(tkbType,
            (world.IsComponentTypeRegistered<NetworkIdentity>() && world.HasComponent<NetworkIdentity>(entity))
                ? world.GetComponentRO<NetworkIdentity>(entity).Value
                : 0L);

        return new InspectorViewModel { Title = title, Fields = fields };
    }

    /// <summary>
    /// Produces a human-readable name from TKB type + network ID.
    /// Falls back to "Entity #<networkId>" when the TKB type is unknown.
    /// </summary>
    public static string BuildDisplayName(long tkbType, long networkId)
    {
        if (tkbType != 0 && s_tkbNames.TryGetValue(tkbType, out var name))
            return $"{name} #{networkId}";
        if (tkbType != 0)
            return $"TKB:{tkbType} #{networkId}";
        return $"Entity #{networkId}";
    }

    // Minimal quaternion → Euler (YXZ / yaw-pitch-roll) for display only.
    // Not authoritative; just for the inspector readout.
    public static System.Numerics.Vector3 QuaternionToEulerDeg(System.Numerics.Quaternion q)
    {
        // Extract yaw (Y), pitch (X), roll (Z) in degrees.
        // Using the standard formula for YXZ Euler:
        //   pitch = asin(2*(qw*qx - qy*qz))
        //   yaw   = atan2(2*(qw*qy + qx*qz), 1 - 2*(qx²+qy²))
        //   roll  = atan2(2*(qw*qz + qx*qy), 1 - 2*(qy²+qz²))
        float sinP = 2f * (q.W * q.X - q.Y * q.Z);
        sinP = Math.Clamp(sinP, -1f, 1f);
        float pitch = (float)(Math.Asin(sinP) * (180.0 / Math.PI));

        float yaw = (float)(Math.Atan2(
            2f * (q.W * q.Y + q.X * q.Z),
            1f - 2f * (q.X * q.X + q.Y * q.Y)) * (180.0 / Math.PI));

        float roll = (float)(Math.Atan2(
            2f * (q.W * q.Z + q.X * q.Y),
            1f - 2f * (q.Y * q.Y + q.Z * q.Z)) * (180.0 / Math.PI));

        return new System.Numerics.Vector3(pitch, yaw, roll);
    }
}

// ── Window (Raylib/ImGui, only constructed when enabled) ─────────────────────

/// <summary>
/// A second OS window showing the FDP entity list and a basic inspector,
/// driven per-frame from <see cref="StrideHrotGame.Update(Stride.Games.GameTime)"/>
/// on the same host thread as the Stride window (STR-P5-T2, BATCH-22).
///
/// <para>
/// <b>Windowing compatibility (Option A — same thread):</b>
/// Stride uses Direct3D (Windows) for its window; raylib uses GLFW + OpenGL for its
/// window.  These are completely separate graphics APIs with separate device contexts and
/// separate OS window handles.  Both can be pumped sequentially on one thread:
/// raylib's <c>BeginDrawing()</c>/<c>EndDrawing()</c>/<c>PollInputEvents()</c> are
/// per-frame, non-blocking calls — they do NOT call into SDL2 or affect Stride's D3D
/// context.  Stride's <c>Update()</c>/<c>Draw()</c> do not call into OpenGL.
/// The design doc §8.3 confirms this: "Graphics contexts don't conflict."
/// </para>
///
/// <para>
/// <b>Lifecycle:</b>
/// <list type="number">
///   <item>Construct after <see cref="EditorStrideSubsystem"/> is booted (world is live).</item>
///   <item>Call <see cref="Open"/> to create the GLFW/OpenGL window and initialize ImGui.</item>
///   <item>Call <see cref="PumpFrame"/> once per render frame (from <c>Update</c>) to poll
///     events, render one ImGui frame, and present.</item>
///   <item>Call <see cref="Close"/> (or <see cref="IDisposable.Dispose"/>) to shut down.</item>
/// </list>
/// </para>
///
/// <para>
/// The window is read-only for v1 (inspect only); command/write support is a follow-up.
/// </para>
/// </summary>
public sealed class StrideInspectorWindow : IDisposable
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    private readonly EditorStrideSubsystem _subsystem;
    private readonly EditorSelectionState _selection;
    private readonly int _width;
    private readonly int _height;

    private bool _opened;
    private bool _disposed;

    // Cached rows updated each PumpFrame call.
    private IReadOnlyList<EntityRow> _rows = Array.Empty<EntityRow>();

    // Stage-4.1: real-editor UI host (non-null only when HostRealEditor + editor window both active).
    // Constructed lazily in Open() once the ImGui context is live.
    private StrideEditorUiHost? _editorUiHost;

    /// <summary>
    /// Constructs the inspector window.  Does NOT open the window yet.
    /// Call <see cref="Open"/> to create the OS window.
    /// </summary>
    /// <param name="subsystem">The live FDP subsystem whose world is inspected.</param>
    /// <param name="selection">
    /// Shared selection state (writer: this window; reader: <see cref="EditorStrideSubsystem"/>
    /// for highlight + CenterOnEntityCommand).
    /// </param>
    /// <param name="width">Window width in pixels (default 700).</param>
    /// <param name="height">Window height in pixels (default 600).</param>
    public StrideInspectorWindow(
        EditorStrideSubsystem subsystem,
        EditorSelectionState  selection,
        int width  = 700,
        int height = 600)
    {
        _subsystem = subsystem ?? throw new ArgumentNullException(nameof(subsystem));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _width  = width;
        _height = height;
    }

    /// <summary>
    /// Opens the raylib/ImGui window.  Must be called from the same thread that will
    /// call <see cref="PumpFrame"/>.
    ///
    /// <para>
    /// Creates a new GLFW/OpenGL window via <c>Raylib.InitWindow</c>.
    /// Raylib supports multiple windows via GLFW multi-window (each <c>InitWindow</c>
    /// call creates an independent GLFW context).
    /// </para>
    /// </summary>
    public void Open()
    {
        if (_opened) return;

        // Raylib/GLFW multi-window: InitWindow while another window already exists
        // works via GLFW's glfwCreateWindow — each call creates an independent OS
        // window and OpenGL context.  IMPORTANT: call SetWindowFocused/SetTargetFPS
        // before the first BeginDrawing so the frame budget is not conflated with
        // Stride's throttler.  We set target FPS to 0 (unlimited) so the raylib
        // PollEvents/BeginDrawing/EndDrawing calls return immediately — Stride's
        // internal throttler governs the overall frame rate.
        Raylib_cs.Raylib.SetConfigFlags(
            ConfigFlags.ResizableWindow | ConfigFlags.UnfocusedWindow);
        Raylib_cs.Raylib.InitWindow(_width, _height, "FDP Inspector — Stride editor_stride");
        Raylib_cs.Raylib.SetTargetFPS(0); // unlimited — driven by Stride's loop

        // Initialize ImGui for this window.  rlImGui.Setup creates the ImGui context and
        // binds it to the current (most-recently-opened) GLFW/OpenGL window.
        // NOTE: if Stride's rlImGui is already initialized for a different context, this
        // creates a SECOND ImGui context bound to our OpenGL window — they are independent.
        rlImGui.Setup(true); // dark theme

        // Stage-4.1: if hosted mode is active, wire up the real editor's ImGui panels.
        // HostedEditorLogic is non-null when STRIDE_HOST_REAL_EDITOR=1 and Initialize() has run.
        if (_subsystem.HostRealEditor && _subsystem.HostedEditorLogic != null)
        {
            _editorUiHost = new StrideEditorUiHost(_subsystem.HostedEditorLogic);
            Log.Info("[StrideInspectorWindow] StrideEditorUiHost wired — editor panels active.");
        }

        _opened = true;
        Log.Info("[StrideInspectorWindow] Raylib/ImGui inspector window opened ({0}x{1}).", _width, _height);
    }

    /// <summary>
    /// Pumps one frame of the inspector window.
    /// Must be called from the same thread as <see cref="Open"/>.
    ///
    /// <para>
    /// If the window has been closed by the user (X button), this call is a no-op;
    /// <see cref="IsOpen"/> returns <c>false</c> and the caller can stop calling.
    /// </para>
    /// </summary>
    public void PumpFrame()
    {
        if (!_opened || _disposed) return;
        if (Raylib_cs.Raylib.WindowShouldClose()) return; // user closed, skip gracefully

        // Rebuild entity list from the live world each frame.
        _rows = StrideInspectorViewModel.BuildEntityList(_subsystem.World);

        Raylib_cs.Raylib.BeginDrawing();
        Raylib_cs.Raylib.ClearBackground(new Color(30, 30, 30, 255));

        rlImGui.Begin();
        DrawInspectorUi();
        rlImGui.End();

        Raylib_cs.Raylib.EndDrawing();
    }

    /// <summary>Returns <c>true</c> if the window is open and has not been closed by the user.</summary>
    public bool IsOpen =>
        _opened && !_disposed && !Raylib_cs.Raylib.WindowShouldClose();

    /// <summary>
    /// Draws the inspector window UI.
    ///
    /// <para>
    /// Layout when hosted mode is active (<c>STRIDE_HOST_REAL_EDITOR=1</c>
    /// + <c>STRIDE_EDITOR_WINDOW=1</c>):
    /// <list type="bullet">
    ///   <item><b>Left column (45%)</b> — simple entity list (unchanged).</item>
    ///   <item><b>Right column (55%)</b> — split vertically:
    ///     <list type="bullet">
    ///       <item>Top section: real editor panels (<see cref="StrideEditorUiHost"/>
    ///         — toolbar + orbat, Stage-4.1).</item>
    ///       <item>Bottom section: existing simple component inspector (unchanged,
    ///         collapsible header).</item>
    ///     </list>
    ///   </item>
    /// </list>
    /// When hosted mode is off, layout is identical to the pre-4.1 behaviour.
    /// </para>
    /// </summary>
    private void DrawInspectorUi()
    {
        // Full-window dockspace so panels fill the window.
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);

        var windowFlags =
            ImGuiWindowFlags.NoTitleBar    |
            ImGuiWindowFlags.NoCollapse    |
            ImGuiWindowFlags.NoResize      |
            ImGuiWindowFlags.NoMove        |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("##root", windowFlags);
        ImGui.PopStyleVar();

        // Two columns: left = entity list, right = inspector (+ editor panels when hosted).
        float listWidth = _width * 0.45f;

        // ── LEFT: entity list ──────────────────────────────────────────────
        ImGui.BeginChild("##entity_list", new Vector2(listWidth, 0), ImGuiChildFlags.Borders);
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), $"Entities ({_rows.Count})");
        ImGui.Separator();

        foreach (var row in _rows)
        {
            bool selected = _selection.SelectedEntity == row.Entity;
            string label = $"{row.DisplayName}  ({row.Position.X:F1},{row.Position.Y:F1},{row.Position.Z:F1})";

            if (ImGui.Selectable(label + $"##{row.NetworkId}", selected))
                _selection.Select(row.Entity);
        }

        if (_rows.Count == 0)
        {
            ImGui.TextDisabled("(no entities)");
        }

        ImGui.EndChild();

        // ── RIGHT: editor panels (hosted mode) + simple inspector ──────────
        ImGui.SameLine();
        ImGui.BeginChild("##right_pane", Vector2.Zero, ImGuiChildFlags.None);

        // Stage-4.1: real editor toolbar + orbat panels (only when hosted mode active).
        if (_editorUiHost != null)
        {
            _editorUiHost.DrawEditorPanels();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        // Simple component inspector (always present; collapsible when editor panels shown).
        bool showSimpleInspector = _editorUiHost == null ||
            ImGui.CollapsingHeader("Simple Inspector##stride_inspector");

        if (showSimpleInspector)
        {
            DrawSimpleInspector();
        }

        ImGui.EndChild();
        ImGui.End();
    }

    /// <summary>
    /// The original simple component inspector panel (entity fields read from
    /// <see cref="StrideInspectorViewModel"/>).  Extracted so it can be shown
    /// standalone (no hosted mode) or as a collapsible section below the editor panels.
    /// </summary>
    private void DrawSimpleInspector()
    {
        ImGui.BeginChild("##inspector", Vector2.Zero, ImGuiChildFlags.Borders);

        var inspector = StrideInspectorViewModel.BuildInspector(_subsystem.World, _selection.SelectedEntity);
        ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, 1f), inspector.Title);

        // "Center" button — sets the CenterRequested flag; StrideHrotGame reads it.
        if (_selection.HasSelection)
        {
            ImGui.SameLine();
            if (ImGui.Button("Center [C]"))
                _selection.RequestCenter();
        }

        ImGui.Separator();

        foreach (var field in inspector.Fields)
        {
            ImGui.TextUnformatted(field.Name);
            ImGui.SameLine();
            ImGui.SetCursorPosX(220f);
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), field.Value);
        }

        if (inspector.Fields.Count == 0 && inspector.Title == "(no selection)")
        {
            ImGui.TextDisabled("Select an entity from the list.");
        }

        ImGui.EndChild();
    }

    /// <summary>Closes and disposes the raylib/ImGui window.</summary>
    public void Close()
    {
        if (!_opened || _disposed) return;
        _disposed = true;

        rlImGui.Shutdown();
        Raylib_cs.Raylib.CloseWindow();

        Log.Info("[StrideInspectorWindow] Closed.");
    }

    /// <inheritdoc/>
    public void Dispose() => Close();
}

// ── CenterOnEntityCommand (STR-P5-T3, BATCH-23) ──────────────────────────────

/// <summary>
/// Pure-logic camera math for the "center on selected entity" command.
///
/// <para>
/// Given an FDP entity position, computes a Stride camera <c>Transform.Position</c> and
/// <c>Transform.Rotation</c> that frames the entity at a sensible offset (6 m behind + 4 m above
/// in the FDP North direction, looking forward-downward at ~34°).  The offset is expressed in
/// Stride world space so it can be applied directly to <c>_cameraEntity.Transform</c>.
/// </para>
///
/// <para>
/// <b>Camera offset (Stride space):</b>
/// <list type="bullet">
///   <item>Entity at Stride position <c>P</c> (after FDP→Stride swizzle).</item>
///   <item>Camera placed at <c>P + (0, +4, −6)</c> — 4 m above the entity, 6 m north (−Z in Stride).</item>
///   <item>Stride cameras look along their local <b>−Z</b> axis (confirmed by
///     <c>BasicCameraController</c> which uses <c>Matrix.Forward</c> = −Z column).</item>
///   <item><see cref="RotationFromForward"/> aligns the camera's local +Z to the supplied vector,
///     so we pass <c>normalize(camPos − target)</c> (the "backward" direction) — making +Z point
///     away from the target and therefore −Z pointing <em>toward</em> the target.</item>
///   <item>Pitch ≈ −atan2(4, 6) ≈ −33.7° (looking slightly downward).</item>
///   <item>Yaw = 0° (facing +Z, i.e. looking North in Stride).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Testable without GPU:</b> all math is pure; the Stride <c>Quaternion</c> and
/// <c>Vector3</c> types are available in the test project via <c>Stride.Core.Mathematics</c>.
/// </para>
/// </summary>
public static class CenterOnEntityCommand
{
    /// <summary>
    /// Camera offset from the target in Stride world space:
    /// (0, +4, −6) = 4 m above, 6 m south (camera is behind-and-above looking forward).
    /// Doubled vs the original (0, +2, −3) to give a more comfortable framing distance.
    /// </summary>
    public static readonly Stride.Core.Mathematics.Vector3 CameraOffset =
        new Stride.Core.Mathematics.Vector3(0f, 4f, -6f);

    /// <summary>
    /// Computes the Stride camera position and rotation to frame the given FDP entity position.
    /// </summary>
    /// <param name="fdpEntityPosition">
    /// The entity's world position in FDP coordinates (X=East, Y=North, Z=Up).
    /// </param>
    /// <param name="cameraPosition">
    /// Output: the new camera position in Stride world space.
    /// </param>
    /// <param name="cameraRotation">
    /// Output: the new camera rotation in Stride space (look-at quaternion).
    /// </param>
    public static void Compute(
        System.Numerics.Vector3 fdpEntityPosition,
        out Stride.Core.Mathematics.Vector3 cameraPosition,
        out Stride.Core.Mathematics.Quaternion cameraRotation)
    {
        // 1. Convert FDP entity position → Stride world space.
        var strideTarget = Hrot.Stride.Core.FdpStrideTransform.ToStridePosition(fdpEntityPosition);

        // 2. Camera placed at target + offset.
        cameraPosition = strideTarget + CameraOffset;

        // 3. Look-at quaternion: camera must look AT the target.
        //
        //    Stride cameras look along their LOCAL −Z axis (BasicCameraController uses
        //    Matrix.Forward which is the −Z column of the rotation matrix).
        //    RotationFromForward(v) aligns the camera's local +Z to v.
        //    Therefore, to make −Z point toward the target we pass the BACKWARD direction:
        //      backward = normalize(camPos − target)
        //    so that:
        //      +Z → backward  (away from target)
        //      −Z → toward target  ✓
        var backward = Stride.Core.Mathematics.Vector3.Normalize(cameraPosition - strideTarget);
        cameraRotation = RotationFromForward(backward);
    }

    /// <summary>
    /// Builds a Stride rotation quaternion that orients the camera's local +Z axis to point in
    /// <paramref name="forward"/> direction, using world Y-up so the camera is never rolled.
    ///
    /// <para>
    /// <b>Stride camera convention:</b> Stride cameras look along local <b>−Z</b>.
    /// To make the camera look AT a target, pass <c>normalize(camPos − target)</c> (the backward
    /// direction) so that +Z points away from the target and −Z points toward it.
    /// </para>
    ///
    /// <para>
    /// <b>Y-up decomposition (fixes the upside-down / roll bug):</b>
    /// Instead of a shortest-arc rotation between (0,0,1) and <c>forward</c> (which can introduce
    /// roll when <c>forward</c> has a Y component), this method decomposes into independent
    /// yaw (around Y) and pitch (around X) quaternions.  This matches the convention used by
    /// <c>BasicCameraController</c> which stores separate Yaw/Pitch floats and composes them as
    /// <c>Quaternion.RotationY(Yaw) * Quaternion.RotationX(Pitch)</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Degenerate case:</b> when <paramref name="forward"/> is nearly parallel to world ±Y
    /// (straight up or down), yaw is undefined; the method keeps the current yaw at 0° so the
    /// camera remains upright without a roll artifact.
    /// </para>
    ///
    /// <para>
    /// Used internally by <see cref="Compute"/>; exposed for unit tests.
    /// </para>
    /// </summary>
    public static Stride.Core.Mathematics.Quaternion RotationFromForward(
        Stride.Core.Mathematics.Vector3 forward)
    {
        // Decompose forward into yaw (rotation around world Y) and pitch (rotation around local X).
        // This guarantees zero roll — the same convention BasicCameraController uses.
        //
        // Yaw = atan2(forward.X, forward.Z)   (horizontal heading, measured from +Z)
        // Pitch = asin(-forward.Y)              (vertical tilt; −Y means looking up)
        //
        // Degenerate: forward nearly straight up (Y≈+1) or down (Y≈−1).
        // In that case XZ length ≈ 0 → yaw is undefined; we default yaw to 0° (looking toward +Z),
        // clamp pitch to ±90°.  The camera stays upright (no roll).

        float xzLen = MathF.Sqrt(forward.X * forward.X + forward.Z * forward.Z);

        float yaw;
        float pitch;

        if (xzLen < 1e-4f)
        {
            // Degenerate: looking straight up or straight down.
            yaw   = 0f;
            pitch = forward.Y > 0f ? -MathF.PI * 0.5f : MathF.PI * 0.5f;
        }
        else
        {
            yaw   = MathF.Atan2(forward.X, forward.Z);
            // pitch = asin(-forward.Y), but clamp to avoid NaN from float drift.
            float sinPitch = System.Math.Clamp(-forward.Y, -1f, 1f);
            pitch = MathF.Asin(sinPitch);
        }

        // Compose: pitch around local X FIRST, then yaw around world Y.
        //
        // Stride's quaternion convention: Transform(v, q1*q2) applies q1 first, then q2
        // (left-multiplication order — opposite to the algebraic convention).
        // To achieve "yaw around world Y, then pitch around local X" we must write
        //   qPitch * qYaw
        // which Stride reads as: apply qPitch first, then qYaw.
        //
        // Note: BasicCameraController stores Yaw/Pitch as separate floats and constructs the
        // final rotation differently (via Entity.Transform.Rotation = RotationY*RotationX in
        // its own code path). Matching its VISUAL outcome here (no roll) is what matters, not
        // necessarily the literal string "RotationY * RotationX".
        var qYaw   = Stride.Core.Mathematics.Quaternion.RotationY(yaw);
        var qPitch = Stride.Core.Mathematics.Quaternion.RotationX(pitch);
        return qPitch * qYaw;
    }
}
