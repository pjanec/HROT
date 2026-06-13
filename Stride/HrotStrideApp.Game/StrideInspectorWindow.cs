#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
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

    // Icon atlas texture loaded into this window's GL context after InitWindow.
    // Kept for unload on Close().
    private Raylib_cs.Texture2D _atlasTexture;

    // ── P1 frame-timing instrumentation ──────────────────────────────────────
    // Measures PumpFrame cost and its sub-phases; logs ~once per second (throttled).
    // Stopwatches are reused across frames — no allocation per frame.
    private readonly Stopwatch _timingTotal   = new();
    private readonly Stopwatch _timingDrawWorld = new();
    private readonly Stopwatch _timingUi      = new();
    private readonly Stopwatch _timingPresent = new();  // EndDrawing (GL SwapBuffers)

    // Accumulated ms for the log window (reset each log interval).
    private double _accTotal;
    private double _accDrawWorld;
    private double _accUi;
    private double _accPresent;
    private int    _timingFrameCount;

    /// <summary>
    /// How many PumpFrame calls between timing log lines.
    /// At ~60 FPS this is ~1 second.  At 30 FPS it is ~2 s — still acceptable.
    /// </summary>
    private const int TimingLogIntervalFrames = 60;

    // WindowManager constructed in Open() once the atlas is loaded.
    // Non-null from Open() onward.
    private Fdp.Presentation.WindowManager.WindowManager? _windowManager;

    /// <summary>
    /// Constructs the inspector window.  Does NOT open the window yet.
    /// Call <see cref="Open"/> to create the OS window.
    /// </summary>
    /// <param name="subsystem">The live FDP subsystem whose world is inspected.</param>
    /// <param name="selection">
    /// Shared selection state (writer: this window; reader: <see cref="EditorStrideSubsystem"/>
    /// for highlight + CenterOnEntityCommand).
    /// </param>
    /// <param name="width">Window width in pixels (default 1280).</param>
    /// <param name="height">Window height in pixels (default 800).</param>
    public StrideInspectorWindow(
        EditorStrideSubsystem subsystem,
        EditorSelectionState  selection,
        int width  = 1280,
        int height = 800)
    {
        _subsystem = subsystem ?? throw new ArgumentNullException(nameof(subsystem));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _width  = width;
        _height = height;
    }

    /// <summary>
    /// Opens the raylib/ImGui window and wires the full editor UI.
    /// Must be called from the same thread that will call <see cref="PumpFrame"/>.
    ///
    /// <para>
    /// Sequence (mirrors clusterrunner's <c>RaylibPresentationShell.InitWindow</c> +
    /// <c>SetupImGui</c> + <c>LoadIconAtlas</c>):
    /// <list type="number">
    ///   <item><c>Raylib.InitWindow</c> — creates a new GLFW/OpenGL context independent
    ///     of Stride's Direct3D context.</item>
    ///   <item><c>rlImGui.Setup</c> — creates the ImGui context bound to this GL context
    ///     and enables DockingEnable.</item>
    ///   <item>Icon atlas load — calls <c>EmbeddedAtlasResources.GetSilkAtlasPngBytes()</c>
    ///     (CPU-only, no GL) then <c>Raylib.LoadTextureFromImage</c> (GPU, MUST run after
    ///     <c>InitWindow</c> so the GL context is active) and wraps the result in an
    ///     <c>IconAtlas</c>.</item>
    ///   <item><c>new WindowManager(atlas)</c> — constructs the window manager.</item>
    ///   <item><c>editor.RegisterWindows(wm)</c> — registers ALL editor windows into the
    ///     manager (no-op when the editor is headless; non-headless when
    ///     <c>buildEditorUi=true</c> was passed to <c>EditorStrideSubsystem.Initialize</c>).</item>
    /// </list>
    /// </para>
    /// </summary>
    public void Open()
    {
        if (_opened) return;

        // ── 1. Create the GLFW/OpenGL window ─────────────────────────────────
        // Raylib/GLFW multi-window: each InitWindow call creates an independent OS window
        // and OpenGL context — separate from Stride's Direct3D context.
        // SetTargetFPS(0): unlimited — Stride's throttler governs the overall frame rate.
        // NOTE: VSyncHint is intentionally NOT included in ConfigFlags — we do not want
        // the GL swap interval locked to the monitor refresh (would block ~16ms per
        // EndDrawing and contend with Stride's DirectX present).
        Raylib_cs.Raylib.SetConfigFlags(
            ConfigFlags.ResizableWindow | ConfigFlags.UnfocusedWindow);
        Raylib_cs.Raylib.InitWindow(_width, _height, "Hrot Editor — Stride editor_stride");
        // P0-FIX (ESC crash): disable the default ESC exit key so pressing ESC in the
        // editor (close popups, etc.) does NOT flip WindowShouldClose() → true mid-session,
        // which would tear down the GL/ImGui context and trigger an "No current context"
        // assert.  Mirrors RaylibPresentationShell.InitWindow line 16.
        Raylib_cs.Raylib.SetExitKey(Raylib_cs.KeyboardKey.Null);
        Raylib_cs.Raylib.SetTargetFPS(0);

        // ── 2. Set up ImGui for this window ───────────────────────────────────
        // rlImGui.Setup creates the ImGui context bound to the current GL window.
        // Enables DockingEnable so panels dock inside the dockspace (mirrors clusterrunner).
        rlImGui.Setup(true); // dark theme
        var io = ImGuiNET.ImGui.GetIO();
        io.ConfigFlags |= ImGuiNET.ImGuiConfigFlags.DockingEnable;

        // ── 3. Load the icon atlas into THIS GL context ───────────────────────
        // EmbeddedAtlasResources.GetSilkAtlasPngBytes() is CPU-only (no GL).
        // Raylib.LoadTextureFromImage uploads to GPU — MUST be called after InitWindow.
        // Recipe mirrors RaylibPresentationShell.LoadIconAtlas() (lines ~63-71).
        byte[] pngBytes = Fdp.Presentation.Icons.EmbeddedAtlasResources.GetSilkAtlasPngBytes();
        var img = Raylib_cs.Raylib.LoadImageFromMemory(".png", pngBytes);
        _atlasTexture = Raylib_cs.Raylib.LoadTextureFromImage(img);
        Raylib_cs.Raylib.UnloadImage(img);
        var atlas = new Fdp.Presentation.Icons.IconAtlas(
            (nint)_atlasTexture.Id, _atlasTexture.Width, _atlasTexture.Height, 16f);

        Log.Info("[StrideInspectorWindow] Icon atlas loaded ({0}x{1}, texId={2}).",
            _atlasTexture.Width, _atlasTexture.Height, _atlasTexture.Id);

        // ── 4. Build the WindowManager ────────────────────────────────────────
        _windowManager = new Fdp.Presentation.WindowManager.WindowManager(atlas);

        // ── 5. Register all editor windows ───────────────────────────────────
        // HostedEditor is non-null when STRIDE_HOST_REAL_EDITOR=1.
        // RegisterWindows is a no-op when the editor is headless (Headless=true).
        // When buildEditorUi=true was passed to EditorStrideSubsystem.Initialize,
        // the editor is non-headless and RegisterWindows registers ALL panels
        // (map canvas adapters, layers, AI editor, blueprints, orbat, spawner, …).
        if (_subsystem.HostedEditor != null)
        {
            _subsystem.HostedEditor.RegisterWindows(_windowManager);
            Log.Info("[StrideInspectorWindow] editor.RegisterWindows(wm) — full editor UI wired.");
        }
        else
        {
            Log.Info("[StrideInspectorWindow] No hosted editor (STRIDE_HOST_REAL_EDITOR not set) — " +
                     "WindowManager is empty; window shows black canvas only.");
        }

        _opened = true;
        Log.Info("[StrideInspectorWindow] Window opened ({0}x{1}).", _width, _height);
    }

    /// <summary>
    /// Pumps one frame of the editor window.
    /// Must be called from the same thread as <see cref="Open"/>.
    ///
    /// <para>
    /// Canonical per-frame sequence (mirrors clusterrunner Program.cs ~281-332):
    /// <list type="number">
    ///   <item><c>BeginDrawing</c> / <c>ClearBackground</c></item>
    ///   <item><c>editor.DrawWorld()</c> — 2-D map canvas (skipped when headless)</item>
    ///   <item><c>rlImGui.Begin()</c></item>
    ///   <item>Dockspace setup (GetMainViewport → SetNextWindow* → PushStyle* → Begin/DockSpace/End → pop)</item>
    ///   <item><c>wm.Render()</c> — all registered editor windows</item>
    ///   <item><c>editor.DrawUI()</c> — menus, popups, hotkey dispatch</item>
    ///   <item><c>rlImGui.End()</c></item>
    ///   <item><c>EndDrawing</c></item>
    /// </list>
    /// When no editor is hosted, steps 2 and 5-6 are no-ops so the window shows
    /// a black canvas (same as clusterrunner in headless / no-windows mode).
    /// </para>
    /// </summary>
    public void PumpFrame()
    {
        if (!_opened || _disposed) return;
        if (Raylib_cs.Raylib.WindowShouldClose()) return;
        // FIX-CLOSE-2: Guard against any stale PumpFrame call after rlImGui.Shutdown().
        // Close() nulls _windowManager before Shutdown, so this is belt-and-suspenders.
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return;

        var editor = _subsystem.HostedEditor;
        var wm     = _windowManager;

        // ── P1 timing: total frame ────────────────────────────────────────────
        _timingTotal.Restart();

        Raylib_cs.Raylib.BeginDrawing();
        Raylib_cs.Raylib.ClearBackground(Raylib_cs.Color.Black);

        // ── DrawWorld: 2-D map canvas (no-op when editor is null or headless) ──
        _timingDrawWorld.Restart();
        editor?.DrawWorld();
        _timingDrawWorld.Stop();

        rlImGui.Begin();

        // ── Dockspace (mirrors clusterrunner Program.cs §4.1.2) ──────────────
        // Fills the entire viewport work area with a transparent passthrough dockspace
        // so all WindowManager-registered panels can be docked freely.
        var viewport      = ImGuiNET.ImGui.GetMainViewport();
        float toolbarH    = 0f;   // toolbar is inside the main menu bar (BATCH-25 convention)
        float statusBarH  = wm?.StatusBar?.Height ?? 0f;

        ImGuiNET.ImGui.SetNextWindowPos(
            Fdp.Presentation.WindowManager.DockspaceLayout.CentralPos(viewport.WorkPos, toolbarH));
        ImGuiNET.ImGui.SetNextWindowSize(
            Fdp.Presentation.WindowManager.DockspaceLayout.CentralSize(
                viewport.WorkSize.X, viewport.WorkSize.Y, toolbarH, statusBarH));
        ImGuiNET.ImGui.SetNextWindowViewport(viewport.ID);

        ImGuiNET.ImGui.PushStyleVar(ImGuiNET.ImGuiStyleVar.WindowRounding, 0f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiNET.ImGuiStyleVar.WindowBorderSize, 0f);
        ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.WindowBg, System.Numerics.Vector4.Zero);

        var dockFlags =
            ImGuiNET.ImGuiWindowFlags.NoDocking       |
            ImGuiNET.ImGuiWindowFlags.NoTitleBar       |
            ImGuiNET.ImGuiWindowFlags.NoCollapse       |
            ImGuiNET.ImGuiWindowFlags.NoResize         |
            ImGuiNET.ImGuiWindowFlags.NoMove           |
            ImGuiNET.ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiNET.ImGuiWindowFlags.NoNavFocus       |
            ImGuiNET.ImGuiWindowFlags.NoBackground;

        ImGuiNET.ImGui.Begin("##DockSpace", dockFlags);
        ImGuiNET.ImGui.PopStyleColor();
        ImGuiNET.ImGui.PopStyleVar(2);

        ImGuiNET.ImGui.DockSpace(
            ImGuiNET.ImGui.GetID("MainDockSpace"),
            Fdp.Presentation.WindowManager.DockspaceLayout.CentralSize(
                viewport.WorkSize.X, viewport.WorkSize.Y, toolbarH, statusBarH),
            ImGuiNET.ImGuiDockNodeFlags.PassthruCentralNode);

        ImGuiNET.ImGui.End();
        // ─────────────────────────────────────────────────────────────────────

        // ── WindowManager.Render() + editor.DrawUI() — timed together as "UI" ─
        _timingUi.Restart();

        // ── WindowManager.Render(): all registered editor panels ─────────────
        wm?.Render();

        // ── editor.DrawUI(): menus, popups, hotkey dispatch ───────────────────
        editor?.DrawUI();

        _timingUi.Stop();

        rlImGui.End();

        // ── EndDrawing: GL SwapBuffers — timed separately to detect vsync block ─
        // If this number is ~16ms (≈ 60Hz monitor period) even though SetTargetFPS(0)
        // was set, the driver's vsync swap interval is still 1 and is blocking here.
        // raylib-cs does not expose glfwSwapInterval(); if that is the root cause the
        // fix would require a native interop call or moving to a separate thread.
        _timingPresent.Restart();
        Raylib_cs.Raylib.EndDrawing();
        _timingPresent.Stop();

        _timingTotal.Stop();

        // ── Throttled log: ~once per second ──────────────────────────────────
        _accTotal     += _timingTotal.Elapsed.TotalMilliseconds;
        _accDrawWorld += _timingDrawWorld.Elapsed.TotalMilliseconds;
        _accUi        += _timingUi.Elapsed.TotalMilliseconds;
        _accPresent   += _timingPresent.Elapsed.TotalMilliseconds;
        _timingFrameCount++;

        if (_timingFrameCount >= TimingLogIntervalFrames)
        {
            double inv = 1.0 / _timingFrameCount;
            Log.Info(
                "[PumpFrame timing] avg over {0} frames — " +
                "PumpFrame={1:F1}ms  DrawWorld={2:F1}ms  UI(wm+drawUI)={3:F1}ms  Present(EndDrawing)={4:F1}ms",
                _timingFrameCount,
                _accTotal     * inv,
                _accDrawWorld * inv,
                _accUi        * inv,
                _accPresent   * inv);

            _accTotal = _accDrawWorld = _accUi = _accPresent = 0;
            _timingFrameCount = 0;
        }
    }

    /// <summary>Returns <c>true</c> if the window is open and has not been closed by the user.</summary>
    public bool IsOpen =>
        _opened && !_disposed && !Raylib_cs.Raylib.WindowShouldClose();

    /// <summary>Closes and disposes the raylib/ImGui window.</summary>
    public void Close()
    {
        if (!_opened || _disposed) return;
        _disposed = true;

        // FIX-CLOSE-2: Drop all references that could trigger ImGui calls BEFORE
        // rlImGui.Shutdown() tears down the ImGui context.
        // Order matters:
        //   1. Null out _windowManager so PumpFrame's wm?.Render() and editor?.DrawUI() become
        //      no-ops on any frame racing to call PumpFrame after Close() is entered.
        //   2. rlImGui.Shutdown() — destroys the ImGui context.  Nothing must call ImGui.* after this.
        //   3. UnloadTexture (GL call, still valid while the GLFW context is live).
        //   4. Raylib.CloseWindow() — tears down the GLFW/OpenGL context.
        _windowManager = null;

        // If the hosted editor is still registered (from RegisterWindows), un-register it
        // by calling rlImGui.Shutdown immediately (the WindowManager holds no back-ref into
        // the editor that survives this call — all editor ImGui pumping goes through
        // _windowManager.Render() which we just nulled out above).
        rlImGui.Shutdown();

        if (_atlasTexture.Id != 0)
            Raylib_cs.Raylib.UnloadTexture(_atlasTexture);
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
