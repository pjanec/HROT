#nullable enable
using System;
using System.Collections.Generic;
using Hrot.Stride.Core;
using Hrot.Stride.Core.TestHarness;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace HrotStrideApp;

/// <summary>
/// In-app manual test harness overlay for the <c>editor_stride</c> GPU app
/// (BATCH-12, STR-TEST-1).
///
/// <para>
/// Drives an extensible <see cref="TestHarnessRegistry"/> of <see cref="VisualTestCase"/>s
/// through two trigger paths that both call the same <see cref="VisualTestCase.Run"/>:
/// <list type="bullet">
///   <item><b>Clickable buttons</b> — one Stride.UI <see cref="Button"/> per case, laid out
///     in a left-hand column on a full-screen <see cref="Canvas"/> attached to a
///     <see cref="UIComponent"/> on a scene entity. The buttons are tinted rectangles (no
///     text) because the project ships no compiled <c>SpriteFont</c> asset (see
///     BATCH-12-REPORT); their human-readable labels are drawn alongside them via
///     <see cref="Stride.Profiling.DebugTextSystem"/>.</item>
///   <item><b>Keyboard shortcuts</b> — every registered case is bound to a key via
///     <see cref="TryGetCaseKey"/>: indices 0–8 → D1–D9, index 9 → D0, indices 10+ → F1,
///     F2, … (up to F12 for index 21). The same mapping is used by both
///     <see cref="PollKeyboard"/> and <see cref="DrawStatus"/> so the on-screen list is
///     always consistent with what the keyboard actually does.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>On-screen status</b> (always via DebugText, which uses Stride's built-in debug font and
/// needs no compositor UI stage): a title line, the numbered case list with shortcuts, the
/// last-triggered action, the live entity/visual counts, the active continuous-hook count,
/// and the last few harness log lines.
/// </para>
///
/// <para>
/// <b>Verification status:</b> this class is compiled and wired but cannot be verified
/// rendering headlessly (no GPU). The DebugText + keyboard path is the robust guaranteed
/// channel; the UI buttons depend on the GraphicsCompositor's UI render feature (present in
/// <c>Assets/GraphicsCompositor.sdgfxcomp</c>, see BATCH-12-REPORT).
/// </para>
/// </summary>
public sealed class StrideTestHarness
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    // ── Layout constants (virtual pixels) ─────────────────────────────────
    private const int ButtonLeft     = 12;
    private const int ButtonTop      = 90;
    private const int ButtonWidth    = 24;
    private const int ButtonHeight   = 22;
    private const int ButtonGap      = 6;
    private const int LabelTextLeft  = ButtonLeft + ButtonWidth + 8;
    private const int TitleTop       = 12;
    private const int StatusRightCol = 360;

    private static readonly Color ButtonColor      = new(60, 120, 200, 255);
    private static readonly Color4 TitleColor      = new Color(255, 220, 120, 255).ToColor4();
    private static readonly Color4 CaseColor       = new Color(220, 220, 220, 255).ToColor4();
    private static readonly Color4 StatusColor     = new Color(160, 230, 160, 255).ToColor4();
    private static readonly Color4 LogColor        = new Color(180, 180, 180, 255).ToColor4();

    private readonly Game                _game;
    private readonly TestHarnessRegistry _registry;
    private readonly TestHarnessContext  _context;

    // Rolling buffer of recent harness log lines for the on-screen echo.
    private readonly LinkedList<string> _recentLog = new();
    private const int MaxRecentLog = 6;

    private string _lastAction = "(none yet)";

    /// <summary>The registry this harness drives (exposed so callers can register cases).</summary>
    public TestHarnessRegistry Registry => _registry;

    /// <summary>The context handed to every triggered case.</summary>
    public TestHarnessContext Context => _context;

    /// <summary>
    /// Constructs the harness. Call <see cref="BuildUi"/> once after construction to attach
    /// the button overlay, then <see cref="Update"/> every frame from <c>Game.Update</c>.
    /// </summary>
    /// <param name="game">The running game (provides Input + DebugTextSystem).</param>
    /// <param name="registry">The (already-populated) test-case registry.</param>
    /// <param name="context">The execution context for triggered cases.</param>
    public StrideTestHarness(Game game, TestHarnessRegistry registry, TestHarnessContext context)
    {
        _game     = game     ?? throw new ArgumentNullException(nameof(game));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _context  = context  ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Builds the Stride.UI button overlay and attaches it to a new entity in the supplied
    /// scene via a full-screen <see cref="UIComponent"/>. One tinted <see cref="Button"/> per
    /// registered case, positioned in a left-hand column; clicking a button triggers its case.
    ///
    /// <para>
    /// Returns the created overlay entity (also added to the scene) so the caller can keep a
    /// reference / remove it later. If the registry is empty no buttons are created but the
    /// overlay entity is still added (harmless).
    /// </para>
    /// </summary>
    /// <param name="scene">The scene to add the overlay entity to.</param>
    public Entity BuildUi(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));

        var canvas = new Canvas();

        for (int i = 0; i < _registry.Count; i++)
        {
            int index = i; // capture for the closure
            var button = new Button
            {
                // No SpriteFont asset ships with the project, so a text Content cannot be
                // rendered. Render the button as a fixed-size tinted rectangle instead; the
                // DebugText overlay supplies the label next to it.
                SizeToContent   = false,
                Width           = ButtonWidth,
                Height          = ButtonHeight,
                BackgroundColor = ButtonColor,
                Name            = $"HarnessButton_{index}",
            };

            // Click and keyboard D1–D9 both call TriggerCase → same VisualTestCase.Run.
            button.Click += (sender, e) => TriggerCase(index, "click");

            int y = ButtonTop + i * (ButtonHeight + ButtonGap);
            button.SetCanvasAbsolutePosition(new Vector3(ButtonLeft, y, 0f));
            button.SetCanvasPinOrigin(new Vector3(0f, 0f, 0f));

            canvas.Children.Add(button);
        }

        var overlayEntity = new Entity("TestHarnessUI");
        overlayEntity.Add(new UIComponent
        {
            Page         = new UIPage { RootElement = canvas },
            IsFullScreen = true,
            IsBillboard  = false,
        });

        scene.Entities.Add(overlayEntity);

        Log.Info("[harness] UI overlay built with {0} button(s).", _registry.Count);
        return overlayEntity;
    }

    /// <summary>
    /// Per-frame driver. Call from <c>Game.Update</c>. Polls keyboard shortcuts (D1–D9),
    /// pumps continuous case hooks, and draws the on-screen status via DebugText.
    /// </summary>
    /// <param name="dt">Frame delta-time in seconds.</param>
    public void Update(float dt)
    {
        PollKeyboard();
        _context.PumpUpdates(dt);
        DrawStatus();
    }

    // ── Trigger path (shared by buttons + keyboard) ───────────────────────

    /// <summary>
    /// Triggers the case at <paramref name="index"/>, logging the source ("click"/"key").
    /// Both the button <c>Click</c> handler and the keyboard path call this.
    /// </summary>
    private void TriggerCase(int index, string source)
    {
        var triggered = _registry.Trigger(index, _context);
        if (triggered == null)
            return;

        _lastAction = $"#{index + 1} {triggered.Label} ({source})";
        var line = $"[harness] triggered #{index + 1} '{triggered.Label}' via {source} — {triggered.Description}";
        Log.Info(line);
        PushRecentLog($"#{index + 1} {triggered.Label} [{source}]");
    }

    private void PollKeyboard()
    {
        var input = _game.Input;
        if (input == null)
            return;

        // BATCH-S2-AC fix: Alt/Ctrl + digit are reserved for camera bookmarks (handled in
        // StrideHrotGame). Without this guard a digit case (e.g. D1 spawn) would ALSO fire on
        // Alt+1, masking the bookmark recall.
        bool modifierHeld = input.IsKeyDown(Keys.LeftAlt)  || input.IsKeyDown(Keys.RightAlt)
                         || input.IsKeyDown(Keys.LeftCtrl) || input.IsKeyDown(Keys.RightCtrl);

        // Every registered case has a key assigned by TryGetCaseKey.
        // Both this and the button click call TriggerCase.
        for (int i = 0; i < _registry.Count; i++)
        {
            if (TryGetCaseKey(i, out var key, out _) && input.IsKeyPressed(key))
            {
                // Skip digit-key cases while a modifier is held (Alt/Ctrl+digit = camera bookmarks).
                if (modifierHeld && key >= Keys.D0 && key <= Keys.D9) continue;
                TriggerCase(i, "key");
            }
        }
    }

    // ── Key-map ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the keyboard <paramref name="key"/> and human-readable <paramref name="label"/>
    /// for the case at <paramref name="index"/>. Used by both <see cref="PollKeyboard"/> and
    /// <see cref="DrawStatus"/> so the two never drift.
    ///
    /// <para>Mapping (covers up to 16 cases):</para>
    /// <list type="bullet">
    ///   <item>Index 0–8  → D1–D9  (label "D1".."D9")</item>
    ///   <item>Index 9    → D0     (label "D0")</item>
    ///   <item>Index 10–21 → F1–F12 (label "F1".."F12")</item>
    /// </list>
    ///
    /// <para>Returns <c>false</c> for any index beyond the covered range.</para>
    /// </summary>
    /// <param name="index">Zero-based case index in the registry.</param>
    /// <param name="key">Assigned <see cref="Keys"/> value.</param>
    /// <param name="label">Display label (e.g. "D1", "D0", "F1").</param>
    /// <returns><c>true</c> when a key is assigned; <c>false</c> when the index is out of range.</returns>
    public static bool TryGetCaseKey(int index, out Keys key, out string label)
    {
        if (index is >= 0 and <= 8)           // D1..D9  (index 0..8)
        {
            key   = Keys.D1 + index;          // Keys.D1..D9 are contiguous (confirmed by original harness)
            label = $"D{index + 1}";
            return true;
        }
        if (index == 9)                       // D0  (the "10th" case)
        {
            key   = Keys.D0;
            label = "D0";
            return true;
        }
        int fIndex = index - 10;              // 0-based F-key index (index 10 → F1, etc.)
        if (fIndex is >= 0 and <= 11)         // F1..F12  (index 10..21)
        {
            key   = Keys.F1 + fIndex;         // Keys.F1..F12 are contiguous
            label = $"F{fIndex + 1}";
            return true;
        }

        key   = default;
        label = "—  ";
        return false;
    }

    // ── On-screen status (DebugText) ──────────────────────────────────────

    private void DrawStatus()
    {
        var debug = _game.DebugTextSystem;
        if (debug == null)
            return;

        debug.Print("== Stride Test Harness ==  click a button or press D1-D9/D0/F1-F6",
            new Int2(ButtonLeft, TitleTop), TitleColor);

        // Numbered case list with shortcuts, aligned with the buttons.
        for (int i = 0; i < _registry.Count; i++)
        {
            var c = _registry.Cases[i];
            TryGetCaseKey(i, out _, out string shortcut);
            int y = ButtonTop + i * (ButtonHeight + ButtonGap) + 4;
            debug.Print($"[{shortcut}] {c.Label}", new Int2(LabelTextLeft, y), CaseColor);
        }

        // Right-hand status column: last action + live counts.
        int entityCount = _context.World?.EntityCount ?? -1;
        int visualCount = _context.VisualBindingSystem?.Visuals.Count ?? -1;
        int hookCount   = _context.ActiveUpdateHookCount;

        debug.Print($"Last action : {_lastAction}",      new Int2(StatusRightCol, ButtonTop +  0), StatusColor);
        debug.Print($"FDP entities: {entityCount}",       new Int2(StatusRightCol, ButtonTop + 18), StatusColor);
        debug.Print($"Visuals     : {visualCount}",       new Int2(StatusRightCol, ButtonTop + 36), StatusColor);
        debug.Print($"Live hooks  : {hookCount}",         new Int2(StatusRightCol, ButtonTop + 54), StatusColor);

        // Recent harness log lines underneath.
        int logY = ButtonTop + 84;
        debug.Print("Recent:", new Int2(StatusRightCol, logY), LogColor);
        logY += 18;
        foreach (var entry in _recentLog)
        {
            debug.Print($"  {entry}", new Int2(StatusRightCol, logY), LogColor);
            logY += 16;
        }
    }

    private void PushRecentLog(string entry)
    {
        _recentLog.AddFirst(entry);
        while (_recentLog.Count > MaxRecentLog)
            _recentLog.RemoveLast();
    }
}
