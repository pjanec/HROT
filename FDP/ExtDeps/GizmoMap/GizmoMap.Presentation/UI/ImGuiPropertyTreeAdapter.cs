using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;
using ImGuiNET;
using StructEdit.Core;
using StructEdit.Json;

[assembly: InternalsVisibleTo("GizmoMap.Presentation.Tests")]

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Minimal adapter that schedules StructInspector rendering via ImGui.
    /// Call <see cref="Schedule"/> from the Raylib 2D pass; call <see cref="DrawScheduled"/>
    /// from the ImGui pass.
    /// When a <see cref="GizmoSchemaRegistry"/> is provided the adapter renders the full
    /// <see cref="EditDocument"/> property tree via ImGui tree nodes instead of a stub label.
    /// </summary>
    public sealed class ImGuiPropertyTreeAdapter
    {
        private readonly List<ScheduledItem> _items = new();
        private readonly GizmoSchemaRegistry? _registry;

        internal enum InspectorState { Viewing, Editing }
        internal readonly Dictionary<(long, uint), InspectorState> _inspectorStates = new();

        private readonly struct ScheduledItem
        {
            public readonly long         NetworkId;
            public readonly uint         SchemaHash;
            public readonly uint         GizmoTypeId;
            public readonly ScreenAnchor Anchor;
            public readonly float        OffsetX;
            public readonly float        OffsetY;
            public readonly SizeMode     SizeMode;
            public readonly bool         IsReadOnly;

            public ScheduledItem(long networkId, uint schemaHash, uint gizmoTypeId, ScreenAnchor anchor, float offsetX, float offsetY, SizeMode sizeMode, bool isReadOnly)
            {
                NetworkId  = networkId;
                SchemaHash = schemaHash;
                GizmoTypeId = gizmoTypeId;
                Anchor     = anchor;
                OffsetX    = offsetX;
                OffsetY    = offsetY;
                SizeMode   = sizeMode;
                IsReadOnly = isReadOnly;
            }
        }

        public ImGuiPropertyTreeAdapter(GizmoSchemaRegistry? registry = null)
        {
            _registry = registry;
        }

        public void Schedule(long networkId, uint schemaHash, uint gizmoTypeId, ScreenAnchor anchor, float offsetX, float offsetY, SizeMode sizeMode, bool isReadOnly)
        {
            _items.Add(new ScheduledItem(networkId, schemaHash, gizmoTypeId, anchor, offsetX, offsetY, sizeMode, isReadOnly));
        }

        // Legacy overload without anchor positioning (defaults to TopLeft, ScreenPixels).
        public void Schedule(long networkId, uint schemaHash, uint gizmoTypeId, float screenX, float screenY, bool isReadOnly)
        {
            _items.Add(new ScheduledItem(networkId, schemaHash, gizmoTypeId, ScreenAnchor.TopLeft, screenX, screenY, SizeMode.ScreenPixels, isReadOnly));
        }

        // Formats the ImGui window title for the given item parameters.
        // Uses ImGui ### syntax to decouple the visible title from the stable window ID.
        // ⭐ CE-1066 — the stable ID is the GIZMO TYPE alone (a class hash, the same every run), so a docked panel returns to its dock
        //   slot after a restart (imgui.ini keys by it); the instance id is appended only when it is needed to tell two panels apart
        //   (an unstamped type 0, or two panels of one type open in the same frame). ⛔ The id used to lead it, and for a global gizmo
        //   it is a per-run counter (GlobalGizmoManager.NewId). The visible title is the schema's name made readable.
        internal static string MakeWindowTitle(string? rootName, long networkId, uint schemaHash, uint gizmoTypeId, bool hasSchema,
                                               bool disambiguate = false)
        {
            string id = gizmoTypeId == 0 || disambiguate ? $"StructInsp_{gizmoTypeId}_{networkId}" : $"StructInsp_{gizmoTypeId}";
            return hasSchema
                ? $"{FriendlyName(rootName)}{(disambiguate ? $" ({networkId})" : "")}###{id}"
                : $"Inspector {networkId} (0x{schemaHash:X})###{id}";
        }

        /// <summary>⭐ CE-1066 — "LayerControlDto" → "Layer Control": the trailing "Dto" dropped, words split at capitals.</summary>
        internal static string FriendlyName(string? rootName)
        {
            if (string.IsNullOrEmpty(rootName)) return "Inspector";
            string name = rootName.EndsWith("Dto", StringComparison.Ordinal) && rootName.Length > 3 ? rootName[..^3] : rootName;
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                    sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }

        /// <summary>⭐ CE-1066 — how many frames a panel the operator closed stays hidden while its gizmo hears the close; after it,
        /// a panel still scheduled shows again (a gizmo that ignores the close is not hidden for ever).</summary>
        internal const int CloseGraceFrames = 30;

        // ⭐ CE-1066 — panels closed with their [X]: hidden locally until the gizmo stops emitting them (or the grace runs out).
        internal readonly Dictionary<(long, uint), int> _closing = new();

        // Panels of one gizmo type scheduled more than once this frame need their instance id in the window id.
        private bool IsDuplicateType(uint gizmoTypeId)
        {
            int n = 0;
            foreach (var it in _items) if (it.GizmoTypeId == gizmoTypeId && ++n > 1) return true;
            return false;
        }

        /// <summary>⭐ CE-1066 — the close bookkeeping shared by both draw paths: forget a closed panel its gizmo no longer emits,
        /// count down the grace. True when <paramref name="key"/> is closing and must not be drawn this frame.</summary>
        private bool IsClosing((long, uint) key)
        {
            if (!_closing.TryGetValue(key, out int left)) return false;
            if (left <= 0) { _closing.Remove(key); return false; }
            _closing[key] = left - 1;
            return true;
        }

        private void ForgetClosedNoLongerScheduled(HashSet<(long, uint)> scheduled)
        {
            foreach (var k in _closing.Keys.Where(k => !scheduled.Contains(k)).ToList())
                _closing.Remove(k);
        }

        /// <summary>⭐ CE-1066 — the operator closed the panel: tell its gizmo, stop editing, hide it while the close travels.</summary>
        private void Close(in ScheduledItem item, Action<long, uint>? onClosed)
        {
            var key = (item.NetworkId, item.GizmoTypeId);
            _inspectorStates.Remove(key);
            _closing[key] = CloseGraceFrames;
            onClosed?.Invoke(item.NetworkId, item.GizmoTypeId);
        }

        public void DrawScheduled(Action<long, uint, string>? onStructUpdate = null, Action<long, uint>? onClosed = null)
        {
            // Remove stale entries whose (NetworkId, GizmoTypeId) is not in the current frame.
            var keys = new HashSet<(long, uint)>(_items.Select(it => (it.NetworkId, it.GizmoTypeId)));
            foreach (var k in _inspectorStates.Keys.Except(keys).ToList())
                _inspectorStates.Remove(k);
            ForgetClosedNoLongerScheduled(keys);   // ⭐ CE-1066

            var viewport = ImGui.GetMainViewport();

            foreach (var item in _items)
            {
                if (IsClosing((item.NetworkId, item.GizmoTypeId))) continue;   // ⭐ CE-1066 — closed with its [X], waiting for the gizmo

                // Resolve offset units against viewport.
                float deltaX = item.SizeMode == SizeMode.ScreenPercent ? item.OffsetX * viewport.WorkSize.X : item.OffsetX;
                float deltaY = item.SizeMode == SizeMode.ScreenPercent ? item.OffsetY * viewport.WorkSize.Y : item.OffsetY;

                // Resolve anchor base position and ImGui pivot.
                var basePos = viewport.WorkPos;
                var pivot   = new Vector2(0f, 0f);

                switch (item.Anchor)
                {
                    case ScreenAnchor.TopCenter:
                        basePos.X += viewport.WorkSize.X * 0.5f;
                        pivot = new Vector2(0.5f, 0f);
                        break;
                    case ScreenAnchor.TopRight:
                        basePos.X += viewport.WorkSize.X;
                        pivot = new Vector2(1f, 0f);
                        break;
                    case ScreenAnchor.Center:
                        basePos.X += viewport.WorkSize.X * 0.5f;
                        basePos.Y += viewport.WorkSize.Y * 0.5f;
                        pivot = new Vector2(0.5f, 0.5f);
                        break;
                    case ScreenAnchor.BottomLeft:
                        basePos.Y += viewport.WorkSize.Y;
                        pivot = new Vector2(0f, 1f);
                        break;
                    case ScreenAnchor.BottomCenter:
                        basePos.X += viewport.WorkSize.X * 0.5f;
                        basePos.Y += viewport.WorkSize.Y;
                        pivot = new Vector2(0.5f, 1f);
                        break;
                    case ScreenAnchor.BottomRight:
                        basePos.X += viewport.WorkSize.X;
                        basePos.Y += viewport.WorkSize.Y;
                        pivot = new Vector2(1f, 1f);
                        break;
                    // TopLeft: default (no adjustment)
                }

                // ImGuiCond.Appearing applies the layout intent once, but permits manual user drag afterwards.
                ImGui.SetNextWindowPos(basePos + new Vector2(deltaX, deltaY), ImGuiCond.Appearing, pivot);

                // Resolve schema early so we can use the struct name as the visible window title.
                EditDocument? doc = null;
                bool hasSchema = _registry != null && _registry.TryGet(item.SchemaHash, out doc) && doc != null;

                // Use ImGui ### syntax to decouple the visible title from the stable window ID.
                string windowTitle = MakeWindowTitle(hasSchema ? doc!.Root.Name : null, item.NetworkId, item.SchemaHash, item.GizmoTypeId, hasSchema,
                                                     IsDuplicateType(item.GizmoTypeId));

                // ⭐ CE-1066 — the panel has a close [X] (floating and docked alike); it tells the gizmo through onClosed
                bool open = true;
                bool visible = ImGui.Begin(windowTitle, ref open);
                if (!open)
                {
                    ImGui.End();
                    Close(item, onClosed);
                    continue;
                }
                if (visible)
                {
                    if (hasSchema)
                    {
                        bool edited = false;
                        foreach (var child in doc!.Root.Children)
                            edited |= DrawEditNode(child, item.IsReadOnly, openByDefault: true);

                        var stateKey = (item.NetworkId, item.GizmoTypeId);
                        bool isFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

                        if (!_inspectorStates.TryGetValue(stateKey, out var state))
                            state = InspectorState.Viewing;

                        if (state == InspectorState.Viewing && isFocused)
                        {
                            _inspectorStates[stateKey] = InspectorState.Editing;
                        }
                        else if (state == InspectorState.Editing && !isFocused)
                        {
                            _inspectorStates[stateKey] = InspectorState.Viewing;
                            if (!item.IsReadOnly && onStructUpdate != null)
                            {
                                // Enforce the canonical StructEdit JSON schema across the network boundary.
                                string json = EditDocumentJsonSerializer.Serialize(doc!);
                                onStructUpdate.Invoke(item.NetworkId, item.GizmoTypeId, json);
                            }
                        }

                        // ⭐ CE-1065 — a field edit commits AT ONCE (the operator sees each checkbox take effect and the panel stays
                        //   open); ⛔ the "Apply" button is gone — it was the only commit besides focus loss, and its gizmos closed the
                        //   panel on it, so trying a second checkbox meant reopening the panel from the menu.
                        CommitIfEdited(item, doc!, edited, onStructUpdate);
                        if (!item.IsReadOnly && onStructUpdate != null)
                        {
                            ImGui.Separator();
                            ImGui.TextDisabled("Changes apply as you click.");
                        }
                    }
                    else
                    {
                        ImGui.Text($"Schema 0x{item.SchemaHash:X} not registered.");
                        if (item.IsReadOnly)
                            ImGui.TextDisabled("(read-only)");
                    }
                }
                ImGui.End();
            }
            _items.Clear();
        }

        /// <summary>⭐ CE-1065 — a field was edited this frame: commit the document now, and stay in <c>Editing</c> (the window keeps
        /// its focus, so a later focus loss commits whatever came after).</summary>
        private void CommitIfEdited(in ScheduledItem item, EditDocument doc, bool edited, Action<long, uint, string>? onStructUpdate)
        {
            if (!edited || item.IsReadOnly || onStructUpdate == null) return;
            _inspectorStates[(item.NetworkId, item.GizmoTypeId)] = InspectorState.Editing;
            onStructUpdate.Invoke(item.NetworkId, item.GizmoTypeId, EditDocumentJsonSerializer.Serialize(doc));
        }

        // Test-only overload: runs state cleanup and state-machine transitions without any ImGui calls.
        // isFocusedOverride is invoked with the window title to determine focus per item; ⭐ CE-1065 editedOverride says whether
        // a field was edited in that window this frame (a checkbox click).
        internal void DrawScheduled(
            Action<long, uint, string>? onStructUpdate,
            Func<string, bool>? isFocusedOverride,
            Func<string, bool>? editedOverride = null,
            Func<string, bool>? closedOverride = null,
            Action<long, uint>? onClosed = null)
        {
            // Remove stale entries whose (NetworkId, GizmoTypeId) is not in the current frame.
            var keys = new HashSet<(long, uint)>(_items.Select(it => (it.NetworkId, it.GizmoTypeId)));
            foreach (var k in _inspectorStates.Keys.Except(keys).ToList())
                _inspectorStates.Remove(k);
            ForgetClosedNoLongerScheduled(keys);   // ⭐ CE-1066

            foreach (var item in _items)
            {
                if (IsClosing((item.NetworkId, item.GizmoTypeId))) continue;   // ⭐ CE-1066
                EditDocument? doc = null;
                bool hasSchema = _registry != null && _registry.TryGet(item.SchemaHash, out doc) && doc != null;

                string windowTitle = MakeWindowTitle(hasSchema ? doc!.Root.Name : null, item.NetworkId, item.SchemaHash, item.GizmoTypeId, hasSchema,
                                                     IsDuplicateType(item.GizmoTypeId));
                if (closedOverride != null && closedOverride(windowTitle)) { Close(item, onClosed); continue; }   // ⭐ CE-1066 — its [X]

                bool isFocused = isFocusedOverride != null && isFocusedOverride(windowTitle);

                var stateKey = (item.NetworkId, item.GizmoTypeId);
                if (!_inspectorStates.TryGetValue(stateKey, out var state))
                    state = InspectorState.Viewing;

                if (state == InspectorState.Viewing && isFocused)
                {
                    _inspectorStates[stateKey] = InspectorState.Editing;
                }
                else if (state == InspectorState.Editing && !isFocused)
                {
                    _inspectorStates[stateKey] = InspectorState.Viewing;
                    if (hasSchema && !item.IsReadOnly && onStructUpdate != null)
                    {
                        string json = EditDocumentJsonSerializer.Serialize(doc!);
                        onStructUpdate.Invoke(item.NetworkId, item.GizmoTypeId, json);
                    }
                }
                if (hasSchema && editedOverride != null && editedOverride(windowTitle))
                    CommitIfEdited(item, doc!, edited: true, onStructUpdate);
            }
            _items.Clear();
        }

        public void ReceiveUiState(GizmoUiState state)
        {
            if (_registry == null) return;
            if (!_registry.TryGet(state.GizmoInstanceId, out var doc) || doc == null) return;

            // Block update if ANY inspector window is currently being edited.
            // _items may be empty when this is called after DrawScheduled; checking
            // _inspectorStates directly is correct regardless of call timing.
            foreach (var kv in _inspectorStates)
            {
                if (kv.Value == InspectorState.Editing)
                    return;
            }

            EditDocumentJsonSerializer.Deserialize(state.EditDocumentJson, doc);
        }

        // Recursively renders an EditNode and its children as an ImGui tree. ⭐ CE-1065 — true when a field was edited.
        // ⭐ CE-1203 (docs/DESIGN_Terrain_Combat_Tuning.md §5e FL-4/FL-5) — a node shows its [EditDisplayName] when it has one and its
        //   [EditDoc] line on hover; the window's first-level groups open by default (the layer panel's five groups would otherwise
        //   hide every row behind a click). Every widget's id is the node id, so two rows may share a label.
        private static bool DrawEditNode(EditNode node, bool parentReadOnly, bool openByDefault = false)
        {
            bool edited = false;
            bool ro = parentReadOnly || node.IsReadOnly;
            string label = LabelOf(node);

            if (node.Children.Count > 0)
            {
                bool open = ImGui.TreeNodeEx($"{label}##{node.Id.Value}", openByDefault ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
                Tooltip(node);
                if (ro) { ImGui.SameLine(); ImGui.TextDisabled("(read-only)"); }
                if (open)
                {
                    foreach (var child in node.Children)
                        edited |= DrawEditNode(child, ro);
                    ImGui.TreePop();
                }
            }
            else
            {
                // Leaf node: show name and binding value, allow editing when not read-only.
                string valueText = TryGetBindingText(node) ?? $"<{node.Kind}>";
                if (ro)
                {
                    ImGui.TextDisabled($"{label}: {valueText}");
                }
                else if (node.Binding != null && node.Binding.ValueType == typeof(bool))
                {
                    bool val = node.Binding.GetBoxed() is bool b && b;
                    if (ImGui.Checkbox($"{label}##{node.Id.Value}", ref val))
                    {
                        node.Binding.SetBoxed(val);
                        edited = true;
                    }
                }
                else
                {
                    ImGui.Text($"{label}: {valueText}");
                }
                Tooltip(node);
            }
            return edited;
        }

        /// <summary>⭐ CE-1203 FL-5 — what the panel prints for <paramref name="node"/>: its <c>[EditDisplayName]</c>, else its name.</summary>
        internal static string LabelOf(EditNode node) => node.Metadata.DisplayName ?? node.Name;

        /// <summary>⭐ CE-1203 FL-5 — the one-line meaning a row shows on hover: its <c>[EditDoc]</c> summary, or null.</summary>
        internal static string? DocOf(EditNode node)
        {
            foreach (var a in node.Metadata.CustomAttributes)
                if (a is StructEdit.Core.Attributes.EditDocAttribute doc) return doc.Summary;
            return null;
        }

        private static void Tooltip(EditNode node)
        {
            var doc = DocOf(node);
            if (doc != null && ImGui.IsItemHovered()) ImGui.SetTooltip(doc);
        }

        private static string? TryGetBindingText(EditNode node)
        {
            if (node.Binding == null) return null;
            try
            {
                object? v = node.Binding.GetBoxed();
                return v?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}

