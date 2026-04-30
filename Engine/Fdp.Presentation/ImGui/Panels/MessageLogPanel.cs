using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using Fdp.Core.Logging;
using ImGuiNET;

namespace Fdp.Presentation.Panels
{
    /// <summary>
    /// Embeddable ImGui panel that displays a tabbed message log.
    ///
    /// <para>Each registered <see cref="IMessageLogSource"/> becomes one tab.
    /// Features per tab:</para>
    /// <list type="bullet">
    ///   <item>Substring filter (message text + logger name).</item>
    ///   <item>Severity checkbox-dropdown filter (Trace/Debug/Info/Warning/Error/Critical).</item>
    ///   <item>Optional timestamp and logger-name columns (global toggles).</item>
    ///   <item>Colour-coded rows per severity.</item>
    ///   <item>Single- and multi-select (Ctrl+click); Ctrl+C copies to clipboard.</item>
    ///   <item>Right-click context menu: Copy / Clear.</item>
    ///   <item>Double-click on rows with <c>FilePath</c> opens the file in the OS default editor.</item>
    ///   <item>Horizontal scrollbar so long lines are fully visible.</item>
    ///   <item>Auto-scroll that tracks the tail; disabled when the user clicks a row or scrolls up.</item>
    ///   <item>Per-tab attention badge (red tab colour) for unseen WARNING/ERROR/CRITICAL messages.
    ///         Toggle-able via right-click context menu on the tab.
    ///         Badge cleared when the tab is active and scroll is at the bottom.</item>
    /// </list>
    ///
    /// <para>Call <see cref="DrawContent"/> from inside a
    /// <see cref="Fdp.Presentation.WindowManager.ManagedWindow.DrawClientArea"/> override.</para>
    /// </summary>
    public sealed class MessageLogPanel
    {
        // ── State ────────────────────────────────────────────────────────────

        private readonly MessageLogRegistry _registry;

        // Global display toggles (shared across all tabs)
        private bool _showTimestamp = true;
        private bool _showLogger    = true;

        // Per-source UI state, lazily created in DrawContent()
        private readonly Dictionary<string, TabState> _tabStates = new();

        // ── Static color constants (avoids per-frame struct construction) ──────
        private static readonly Vector4 s_colorTimestamp   = new(1.00f, 1.00f, 1.00f, 1f);
        private static readonly Vector4 s_colorLogger      = new(0.60f, 0.60f, 0.60f, 1f);
        private static readonly Vector4 s_colorNumber      = new(0.30f, 0.80f, 1.00f, 1f); // Cyan
        private static readonly Vector4 s_colorPunctuation = new(0.90f, 0.60f, 0.20f, 1f); // Amber

        // ── Tab state ────────────────────────────────────────────────────────
        private sealed class TabState
        {
            // Thread-safe attention flag (set by OnMessageAdded from NLog thread)
            public volatile bool HasUnobservedAttention;
            public bool NotificationsEnabled;

            // Severities currently hidden (empty = show all)
            public readonly HashSet<LogSeverity> HiddenSeverities = new();

            // Original-list indices of selected messages (unsorted during editing)
            public readonly List<int> SelectedIndices = new();

            // One-shot flag: set by the "scroll to bottom" button to force a
            // single jump even when the user has scrolled up.
            public bool ForceScrollToBottom;
            public string FilterText = string.Empty;
        }

        // ── Construction ─────────────────────────────────────────────────────
        public MessageLogPanel(MessageLogRegistry registry)
        {
            _registry = registry;
        }

        // ── Public draw entry point ──────────────────────────────────────────
        /// <summary>
        /// Renders the full message log content. Call from
        /// <c>ManagedWindow.DrawClientArea()</c>.
        /// </summary>
        public void DrawContent()
        {
            // Lazily subscribe to sources that were registered after construction
            foreach (var source in _registry.Sources)
                EnsureTabState(source);

            // Global options toolbar
            Gui.Checkbox("Timestamp", ref _showTimestamp);
            Gui.SameLine();
            Gui.Checkbox("Logger", ref _showLogger);
            Gui.Separator();

            if (!Gui.BeginTabBar("##MsgLogTabs", ImGuiTabBarFlags.None))
                return;

            foreach (var source in _registry.Sources)
            {
                if (!_tabStates.TryGetValue(source.SourceId, out var state))
                    continue;

                DrawTab(source, state);
            }

            Gui.EndTabBar();
        }

        // ── Private: tab lifecycle ───────────────────────────────────────────

        private void EnsureTabState(IMessageLogSource source)
        {
            if (_tabStates.ContainsKey(source.SourceId))
                return;

            var state = new TabState
            {
                // NLog global source is very noisy; notifications off by default
                NotificationsEnabled = source.SourceId != "global_nlog",
            };
            _tabStates[source.SourceId] = state;

            // Subscribe on the creation thread (main thread); handler may be called
            // from a background thread so only write the volatile bool.
            source.OnMessageAdded += entry =>
            {
                if (state.NotificationsEnabled && entry.Severity >= LogSeverity.Warning)
                    state.HasUnobservedAttention = true;
            };
        }

        private void DrawTab(IMessageLogSource source, TabState state)
        {
            // Attention badge: push red tab colour
            if (state.HasUnobservedAttention)
                Gui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.65f, 0.13f, 0.13f, 1f));

            bool tabOpen = Gui.BeginTabItem(
                $"{source.DisplayName}##tab_{source.SourceId}");

            if (state.HasUnobservedAttention)
                Gui.PopStyleColor();

            // Tab right-click context menu
            if (Gui.BeginPopupContextItem($"##tabctx_{source.SourceId}"))
            {
                bool notify = state.NotificationsEnabled;
                if (Gui.Checkbox("Enable Notifications", ref notify))
                    state.NotificationsEnabled = notify;
                Gui.Separator();
                if (Gui.MenuItem("Clear"))
                {
                    source.Clear();
                    state.SelectedIndices.Clear();
                    state.HasUnobservedAttention = false;
                }
                Gui.EndPopup();
            }

            if (tabOpen)
            {
                DrawTabContent(source, state);
                Gui.EndTabItem();
            }
        }

        // ── Private: tab content ─────────────────────────────────────────────

        private void DrawTabContent(IMessageLogSource source, TabState state)
        {
            // -- Toolbar ------
            Gui.SetNextItemWidth(220f);
            Gui.InputText($"##filter_{source.SourceId}", ref state.FilterText, 256);
            if (Gui.IsItemHovered())
                Gui.SetTooltip("Substring filter (message + logger name)");

            Gui.SameLine();
            DrawSeverityFilterButton(source.SourceId, state);

            Gui.SameLine();
            if (Gui.SmallButton($"Clear##{source.SourceId}"))
            {
                source.Clear();
                state.SelectedIndices.Clear();
                state.HasUnobservedAttention = false;
            }

            Gui.SameLine();
            if (Gui.SmallButton($"v##tail_{source.SourceId}"))
                state.ForceScrollToBottom = true;
            if (Gui.IsItemHovered())
                Gui.SetTooltip("Scroll to bottom / re-enable auto-scroll");

            Gui.Separator();

            // -- Message list ------
            DrawMessageList(source, state);
        }

        private void DrawSeverityFilterButton(string sourceId, TabState state)
        {
            int hidden = state.HiddenSeverities.Count;
            string label = hidden > 0
                ? $"Severity [{hidden} hidden]###sev_{sourceId}"
                : $"Severity###sev_{sourceId}";

            if (hidden > 0)
                Gui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.28f, 0.08f, 1f));

            if (Gui.Button(label))
                Gui.OpenPopup($"##sevpop_{sourceId}");

            if (hidden > 0)
                Gui.PopStyleColor();

            DrawSeverityFilterPopup(sourceId, state);
        }

        private static void DrawSeverityFilterPopup(string sourceId, TabState state)
        {
            if (!Gui.BeginPopup($"##sevpop_{sourceId}"))
                return;

            Gui.TextDisabled("Show / hide severity levels:");
            Gui.Separator();

            if (Gui.SmallButton("Show All"))
                state.HiddenSeverities.Clear();
            Gui.SameLine();
            if (Gui.SmallButton("Hide All"))
            {
                foreach (LogSeverity sev in Enum.GetValues<LogSeverity>())
                    state.HiddenSeverities.Add(sev);
            }
            Gui.Separator();

            foreach (LogSeverity sev in Enum.GetValues<LogSeverity>())
            {
                bool visible = !state.HiddenSeverities.Contains(sev);
                if (Gui.Checkbox(sev.ToString(), ref visible))
                {
                    if (visible) state.HiddenSeverities.Remove(sev);
                    else         state.HiddenSeverities.Add(sev);
                }
            }

            Gui.EndPopup();
        }

        // ── Private: message list ────────────────────────────────────────────

        private void DrawMessageList(IMessageLogSource source, TabState state)
        {
            var messages = source.GetMessages();

            // Build filtered index list for this frame
            var filtered = new List<int>(messages.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (state.HiddenSeverities.Contains(msg.Severity))
                    continue;
                if (!string.IsNullOrEmpty(state.FilterText) &&
                    !msg.Message.Contains(state.FilterText, StringComparison.OrdinalIgnoreCase) &&
                    !msg.LoggerName.Contains(state.FilterText, StringComparison.OrdinalIgnoreCase))
                    continue;
                filtered.Add(i);
            }

            // Scrollable child with horizontal scrollbar
            bool childVisible = Gui.BeginChild(
                $"##scroll_{source.SourceId}",
                Vector2.Zero,
                ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar);

            if (!childVisible)
            {
                Gui.EndChild();
                return;
            }

            // Keyboard Ctrl+C copy
            if (Gui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
                Gui.GetIO().KeyCtrl &&
                Gui.IsKeyPressed(ImGuiKey.C))
            {
                CopySelectedToClipboard(messages, state);
            }

            // Snapshot scroll position BEFORE rendering rows.
            // With the "sticky bottom" pattern we do not need to track the mouse
            // wheel; if the scroll bar is no longer at the bottom the flag naturally
            // becomes false and auto-following stops.
            bool wasAtBottom = Gui.GetScrollY() >= Gui.GetScrollMaxY() - 1.0f;

            if (filtered.Count == 0)
            {
                Gui.TextDisabled("(no messages)");
            }
            else
            {
                for (int fi = 0; fi < filtered.Count; fi++)
                {
                    int msgIdx = filtered[fi];
                    DrawMessageRow(msgIdx, messages[msgIdx], messages, state);
                }
            }

            // Sticky bottom: snap to tail when the view was already at the bottom
            // OR when the button was clicked once (ForceScrollToBottom).
            if (state.ForceScrollToBottom || wasAtBottom)
            {
                Gui.SetScrollHereY(1.0f);
                state.ForceScrollToBottom    = false;
                state.HasUnobservedAttention = false;
            }

            Gui.EndChild();
        }

        // ── Private: individual row ──────────────────────────────────────────

        private void DrawMessageRow(
            int msgIdx,
            MessageLogEntry msg,
            IReadOnlyList<MessageLogEntry> allMessages,
            TabState state)
        {
            bool isSelected = state.SelectedIndices.Contains(msgIdx);

            // 1. Save cursor so we can overlay colored text after the selectable.
            var startPos = Gui.GetCursorPos();

            // 2. Draw invisible selectable spanning the full row for input handling.
            //    The ##-only label renders nothing; AllowOverlap lets the context
            //    menu hit-test work on the same row.
            if (Gui.Selectable(
                    $"##sel_{msgIdx}",
                    isSelected,
                    ImGuiSelectableFlags.AllowOverlap))
            {
                if (Gui.GetIO().KeyCtrl)
                {
                    if (isSelected) state.SelectedIndices.Remove(msgIdx);
                    else            state.SelectedIndices.Add(msgIdx);
                }
                else
                {
                    state.SelectedIndices.Clear();
                    state.SelectedIndices.Add(msgIdx);
                }
            }

            // Right-click context menu on the row
            if (Gui.BeginPopupContextItem($"##rowctx_{msgIdx}"))
            {
                if (Gui.MenuItem("Copy"))
                {
                    if (state.SelectedIndices.Contains(msgIdx) && state.SelectedIndices.Count > 1)
                        CopySelectedToClipboard(allMessages, state);
                    else
                        Gui.SetClipboardText(msg.Message);
                }
                Gui.EndPopup();
            }

            // Double-click: open file in OS default editor
            if (!string.IsNullOrEmpty(msg.FilePath) &&
                Gui.IsItemHovered() &&
                Gui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                TryOpenInEditor(msg.FilePath);
            }

            // 3. Rewind cursor to overlay colored text on top of the selectable.
            Gui.SetCursorPos(startPos);

            bool needSameLine = false;

            // 4. Timestamp in white
            if (_showTimestamp)
            {
                Gui.TextColored(s_colorTimestamp, $"[{msg.Timestamp:HH:mm:ss.fff}] ");
                needSameLine = true;
            }

            // 5. Logger name in gray
            if (_showLogger)
            {
                if (needSameLine) Gui.SameLine(0, 0);
                Gui.TextColored(s_colorLogger, $"[{msg.LoggerName}] ");
                needSameLine = true;
            }

            // 6. Syntax-highlighted message body
            Vector4 baseColor = GetSeverityColor(msg.Severity);
            var chunks = msg.Chunks;

            if (chunks.Count == 0)
            {
                // Empty or unchunked message: draw the raw text
                if (needSameLine) Gui.SameLine(0, 0);
                Gui.TextColored(baseColor, msg.Message);
            }
            else
            {
                for (int ci = 0; ci < chunks.Count; ci++)
                {
                    var chunk = chunks[ci];
                    Vector4 chunkColor = chunk.Type switch
                    {
                        ChunkType.Number      => s_colorNumber,
                        ChunkType.Punctuation => s_colorPunctuation,
                        _                     => baseColor,
                    };
                    if (needSameLine || ci > 0) Gui.SameLine(0, 0);
                    Gui.TextColored(chunkColor, chunk.Text);
                }
            }
        }

        // ── Private: helpers ─────────────────────────────────────────────────

        private static void CopySelectedToClipboard(
            IReadOnlyList<MessageLogEntry> messages, TabState state)
        {
            if (state.SelectedIndices.Count == 0) return;

            var sorted = state.SelectedIndices.OrderBy(i => i).ToList();
            var sb = new StringBuilder();
            foreach (int idx in sorted)
            {
                if (idx < messages.Count)
                    sb.AppendLine(messages[idx].Message);
            }
            Gui.SetClipboardText(sb.ToString().TrimEnd('\r', '\n'));
        }

        private static void TryOpenInEditor(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch
            {
                // Silently ignore; file may not exist or no handler registered
            }
        }

        private static Vector4 GetSeverityColor(LogSeverity severity) => severity switch
        {
            LogSeverity.Trace    => new Vector4(0.55f, 0.55f, 0.55f, 1f),
            LogSeverity.Debug    => new Vector4(0.85f, 0.85f, 0.85f, 1f),
            LogSeverity.Info     => new Vector4(0.40f, 1.00f, 0.40f, 1f),
            LogSeverity.Warning  => new Vector4(1.00f, 1.00f, 0.00f, 1f),
            LogSeverity.Error    => new Vector4(1.00f, 0.40f, 0.40f, 1f),
            LogSeverity.Critical => new Vector4(1.00f, 0.20f, 1.00f, 1f),
            _                    => new Vector4(1.00f, 1.00f, 1.00f, 1f),
        };
    }
}
