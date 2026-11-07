using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Replication;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// Recursive ImGui renderer for an <see cref="IEditSession"/> document tree.
/// Draws each <see cref="EditNode"/> as one or more rows in the two-column
/// "Property | Value" table owned by <see cref="ComponentEditWindow"/>.
/// </summary>
public sealed partial class ComponentEditDrawer
{
    private readonly IEditSession _session;
    private readonly IMapPickContext? _pickerCtx;
    private readonly Types.TypeCatalogue _types;                     // ⭐ S3 (CE-3378) — every cell by TYPE (or by field rule), registered once; a host adds its own through an overlay it built once
    private readonly IServiceProvider? _services;                    // ⭐ S3 — what a domain type's cell reads (the asset, the registry), instead of a per-host drawer constructor
    private readonly ReferenceLeafDrawers.State _refState = new();   // ⭐ CE-4206 — the form a person switched a link field to, the key being typed
    private readonly IParamBindContext? _bindCtx;                    // ⭐ CE-4210 MA-1 — the params in scope, null ⇒ no bind affordance anywhere
    private readonly Stack<object?> _containers = new();             // ⭐ CE-4210 MA-2 — the values of the containers being drawn: the nearest one decides the home
    private readonly HashSet<int> _hoveredRows = new();              // ⭐ CE-4210 MA-1 — last frame's hovered bindable rows (the label tint reads it before the row is drawn)

    /// <param name="paramBindContext">⭐ CE-4210 P2 (MA-1) — the params a field may be fed from; <c>null</c> (the default) ⇒ nothing changes. ⛔ A host that HAS the world must pass it (the silent-default rule).</param>
    public ComponentEditDrawer(
        IEditSession session,
        IMapPickContext? pickerCtx,
        IParamBindContext? paramBindContext = null,
        Types.TypeCatalogue? types = null,
        IServiceProvider? services = null,
        StructEdit.Core.Defaults.IFieldDefaults? defaults = null,
        bool readOnly = false)
    {
        _session            = session;
        _pickerCtx          = pickerCtx;
        _bindCtx            = paramBindContext;
        _types              = types ?? Types.TypeCatalogue.Standard;
        _services           = services;
        _defaults           = defaults;
        ReadOnly            = readOnly;
    }

    /// <summary>⭐ N-4 (CE-3381, S7) — where each field's DEFAULT comes from (a document over <c>new T()</c>, a host's Fill source); null ⇒ no default marks. A leaf equal to its default draws its value grey; a changed one an amber bar and a ↺ that writes the default back.
    /// ⛔ A host that HAS a default source must pass it (the silent-default rule).</summary>
    public StructEdit.Core.Defaults.IFieldDefaults? Defaults => _defaults;
    private readonly StructEdit.Core.Defaults.IFieldDefaults? _defaults;

    /// <summary>⭐ N-4 (S8) — the whole table read-only (a host in Live / Replay): every value cell draws disabled and refuses input; a node's own <c>[EditReadOnly]</c> does the same for its subtree. The show-on-map glyph is not an edit and stays live.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>⭐ S3 — the catalogue this table draws its cells from (the forwarding rails assert on the CONSTRUCTED object).</summary>
    public Types.TypeCatalogue Catalogue => _types;

    /// <summary>⭐ CE-4210 — the bind context this drawer was given (for the host rails: the CONSTRUCTED object is what a forwarding rail asserts on).</summary>
    public IParamBindContext? ParamBindContext => _bindCtx;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>⭐ CE-1015 — child rows indent by this many pixels (about half ImGui's default 21, which read as ~5 characters).</summary>
    public const float IndentPixels = 10f;

    /// <summary>
    /// ⭐ CE-1016 — renders a DOCUMENT ROOT: a struct / class / record root shows its FIELDS directly, without a collapsible row
    /// named after its type (the window or panel already says what is being edited); any other root (a selection of several
    /// components, an array) renders as <see cref="DrawEditNode"/> does. ⭐ CE-1015 — children indent by <see cref="IndentPixels"/>.
    /// Must be called inside a two-column <c>BeginTable</c>/<c>EndTable</c> block.
    /// </summary>
    /// <summary>⭐ CE-4210 MA-1 — what the last <see cref="DrawRoot"/> drew for binding: faint buttons (unbound bindable rows), chips (bound rows), red chips. The frame rail reads it.</summary>
    public (int Buttons, int Chips, int RedChips) LastBindRows => (_btnRows, _chipRows, _redRows);
    private int _btnRows, _chipRows, _redRows;

    public void DrawRoot(EditNode root)
    {
        _btnRows = _chipRows = _redRows = 0;
        ImGuiApi.PushStyleVar(ImGuiStyleVar.IndentSpacing, IndentPixels);
        try
        {
            if (FieldsOwner(root) is { } owner)
                foreach (var child in owner.Children) DrawEditNode(child);
            else
                DrawEditNode(root);
        }
        finally { ImGuiApi.PopStyleVar(); }
    }

    /// <summary>
    /// The node whose FIELDS <see cref="DrawRoot"/> shows without a row of its own: the root itself when it is a plain struct /
    /// class / record, or the ONE such child of a selection root (a single object opened for editing). Null when the root is a
    /// selection of several (each keeps its header), an array, or polymorphic.
    /// </summary>
    public static EditNode? FieldsOwner(EditNode root)
    {
        if (root.Kind == EditNodeKind.SelectionRoot)
            return root.Children.Count == 1 && IsPlainRecord(root.Children[0]) ? root.Children[0] : null;
        return IsPlainRecord(root) ? root : null;
    }

    private static bool IsPlainRecord(EditNode node)
        => node.Kind is EditNodeKind.Struct or EditNodeKind.Class or EditNodeKind.Record
           && !node.ClrType.IsAbstract && !node.ClrType.IsInterface && node.Binding is not IContainerBinding;

    /// <summary>
    /// Renders <paramref name="node"/> and all of its descendants as table rows.
    /// Must be called inside a two-column <c>BeginTable</c>/<c>EndTable</c> block.
    /// </summary>
    /// <param name="node">The node to render.</param>
    /// <param name="parentContainer">
    /// The container binding of the parent node, when this node is an array element.
    /// <c>null</c> for non-array children and the root.
    /// </param>
    /// <param name="elementIndex">
    /// Zero-based index of this node within <paramref name="parentContainer"/>.
    /// -1 when the node is not an array element.
    /// </param>
    public void DrawEditNode(
        EditNode node,
        IContainerBinding? parentContainer = null,
        int elementIndex = -1)
    {
        if (_session.RebuildState == EditRebuildState.RebuildRequired)
            return;

        // ⭐ N-3 — a Nullable<T> of a built-in is ONE cell (a "set" box and the value), whatever kind the builder gave it
        if (Nullable.GetUnderlyingType(node.ClrType) is { } nullableOf && StructEdit.Core.Text.ValueText.Handles(nullableOf) && node.Kind != EditNodeKind.SelectionRoot)
        {
            DrawLeafNode(node, parentContainer, elementIndex);
            return;
        }

        switch (node.Kind)
        {
            case EditNodeKind.SelectionRoot:
                // Invisible wrapper: iterate children without rendering the node itself.
                foreach (var child in node.Children)
                    DrawEditNode(child);
                break;

            case EditNodeKind.Struct:
            case EditNodeKind.Class:
            case EditNodeKind.Record:
            case EditNodeKind.DynamicArray:
            case EditNodeKind.InlineArray:
            case EditNodeKind.FixedBuffer:
                DrawListNode(node);   // ⭐ N-2 (CE-3379): the list forms of the kit (ComponentEditDrawer.Lists.cs)
                break;

            case EditNodeKind.BufferView:
                DrawContainerNode(node, parentContainer, elementIndex);
                break;

            case EditNodeKind.Scalar:
            case EditNodeKind.Boolean:
            case EditNodeKind.String:
            case EditNodeKind.Enum:
            case EditNodeKind.Custom:
            case EditNodeKind.Guid:
            case EditNodeKind.DateTime:
                DrawLeafNode(node, parentContainer, elementIndex);
                break;

            default:
                DrawUnsupportedNode(node);
                break;
        }
    }

    // ── Container nodes ───────────────────────────────────────────────────────

    private void DrawContainerNode(EditNode node, IContainerBinding? parentContainer, int elementIndex)
    {
        ImGuiApi.TableNextRow();
        ImGuiApi.TableSetColumnIndex(0);
        ImGuiApi.PushID(node.Id.Value);

        // ⭐ U-7 ①② (Condition editor §8.2) — the row's name is the field's display name; a polymorphic list element is named by its KIND
        //   (never "[0]"), the kind picker stays in column 2
        bool opened;
        if (KindIconOf(node) is { } kindIcon)
        {
            // ⭐ U-7 ② — a kind with a glyph is the node's own row: chevron · coloured glyph · name, through the ONE TreeRow (E17)
            bool open = !_collapsed.Contains(node.Id.Value);
            NodeEditor.Core.Widgets.TreeRow.Draw(RowId(node.Id.Value), ContainerLabelOf(node, elementIndex),
                NodeEditor.Core.Widgets.RowIcon.OfGlyph(kindIcon), 0, hasChildren: true, ref open, selected: false);
            if (open) _collapsed.Remove(node.Id.Value); else _collapsed.Add(node.Id.Value);
            if (open) ImGuiApi.TreePush(RowId(node.Id.Value));
            opened = open;
        }
        else
            opened = ImGuiApi.TreeNodeEx(
                ContainerLabelOf(node, elementIndex),
                ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen);

        ImGuiApi.TableSetColumnIndex(1);
        bool canDelete = parentContainer != null && parentContainer.CanResize && elementIndex >= 0;

        var containerBinding = node.Binding as IContainerBinding;
        if (containerBinding != null)
        {
            ImGuiApi.TextDisabled(CountLabel(containerBinding.Count));

            if (containerBinding.CanResize)
            {
                // ⭐ U-7 ⑦ — "Add <element>" named by its element type, at the right of the row
                string add = AddLabelOf(node);
                ImGuiApi.SameLine(ImGuiApi.GetContentRegionAvail().X - ImGuiApi.CalcTextSize(add).X - 36f);
                if (ImGuiApi.SmallButton(add))
                {
                    containerBinding.Resize(containerBinding.Count + 1);
                    _session.MarkStructuralChange();
                }
            }
            if (canDelete)
            {
                if (TrashButton(node))
                {
                    RemoveElementAtIndex(parentContainer!, elementIndex);
                    _session.MarkStructuralChange();
                }
            }
        }
        else if (node.ClrType.IsAbstract || node.ClrType.IsInterface)
        {
            object? currentObj = node.Binding?.GetBoxed();
            string preview = currentObj != null ? StructEdit.Core.Attributes.DisplayNames.Of(currentObj.GetType()) : "(none)";   // ⭐ U-7 ① — the kind by name

            float comboWidth = ImGuiApi.GetContentRegionAvail().X;
            if (canDelete) comboWidth -= 30f;
            if (comboWidth < 60f) comboWidth = 60f;

            ImGuiApi.SetNextItemWidth(comboWidth);
            if (ImGuiApi.BeginCombo("##poly", preview))
            {
                var derivedAttrs = node.ClrType.GetCustomAttributes(
                    typeof(System.Text.Json.Serialization.JsonDerivedTypeAttribute), true);

                if (currentObj != null)
                {
                    if (ImGuiApi.Selectable("(none)", false))
                    {
                        node.Binding?.SetBoxed(null);
                        _session.MarkStructuralChange();
                    }
                }

                foreach (var attrObj in derivedAttrs)
                {
                    if (attrObj is System.Text.Json.Serialization.JsonDerivedTypeAttribute attr)
                    {
                        var t = attr.DerivedType;
                        bool isSelected = currentObj != null && currentObj.GetType() == t;
                        if (ImGuiApi.Selectable(StructEdit.Core.Attributes.DisplayNames.Of(t), isSelected))
                        {
                            if (!isSelected)
                            {
                                var newInst = Activator.CreateInstance(t);
                                node.Binding?.SetBoxed(newInst);
                                _session.MarkStructuralChange();
                            }
                        }
                    }
                }
                ImGuiApi.EndCombo();
            }
            if (canDelete)
            {
                if (TrashButton(node))
                {
                    RemoveElementAtIndex(parentContainer!, elementIndex);
                    _session.MarkStructuralChange();
                }
            }
        }
        else
        {
            if (canDelete)
            {
                if (TrashButton(node))
                {
                    RemoveElementAtIndex(parentContainer!, elementIndex);
                    _session.MarkStructuralChange();
                }
            }
        }

        // ⭐ U-7 ⑥ — a COLLAPSED node shows its one-line summary, dimmed, in column 2 (the ONE hook: EditSummaries; the condition
        //   kinds fill it from ConditionSentence in U-14)
        if (!opened && node.Binding?.GetBoxed() is { } collapsedValue && EditSummaries.Of(collapsedValue) is { } summary)
        {
            ImGuiApi.TableSetColumnIndex(1);
            ImGuiApi.SameLine();
            ImGuiApi.TextDisabled(summary);
        }

        if (opened)
        {
            // ⭐ CE-4210 MA-2 — the container's VALUE is on the stack while its children draw: a property of a BindableNode binds through the node's Bind map
            _containers.Push(_bindCtx is null ? null : node.Binding?.GetBoxed());
            try
            {
                int i = 0;
                object? parentValue = node.Binding?.GetBoxed();
                foreach (var child in node.Children)
                {
                    if (HidesChild(parentValue, child)) { i++; continue; }   // ⭐ U-7 ③
                    DrawEditNode(child, containerBinding, i);
                    i++;
                }
            }
            finally { _containers.Pop(); }
            ImGuiApi.TreePop();
        }

        ImGuiApi.PopID();
    }

    // ── Leaf nodes ────────────────────────────────────────────────────────────

    /// <summary>For rails: the bytes the last leaf row allocated (R-2084: 0 in a steady frame).</summary>
    public long LeafRowBytes { get; private set; }

    private void DrawLeafNode(EditNode node, IContainerBinding? parentContainer, int elementIndex)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        DrawLeafNodeCore(node, parentContainer, elementIndex);
        LeafRowBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private void DrawLeafNodeCore(EditNode node, IContainerBinding? parentContainer, int elementIndex)
    {
        ImGuiApi.TableNextRow();
        // ⭐ N-3 — a refused value tints its row (the mockup's error row)
        if (_cells.TryGetValue(node.Id.Value, out var errCell) && errCell.Error != null)
            ImGuiApi.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGuiApi.GetColorU32(NodeEditor.Core.Widgets.IconPalette.Error with { W = 0.18f }));
        ImGuiApi.TableSetColumnIndex(0);
        ImGuiApi.PushID(node.Id.Value);

        // ⭐ CE-4210 MA-1 — is this field bindable, and bound? (no context ⇒ BindView.NotBindable: the row below is exactly what it was)
        var bind = _bindCtx is null ? BindView.NotBindable
            : ParamBindModel.View(node.ClrType, node.Binding?.GetBoxed(), node.JsonPath, node.Name, _containers.Count > 0 ? _containers.Peek() : null, _bindCtx);
        bool hoverTint = bind.Bindable && _hoveredRows.Contains(node.Id.Value);
        if (hoverTint) ImGuiApi.PushStyleColor(ImGuiCol.Text, BindTint);
        ImGuiApi.TreeNodeEx(
            _rowLabel ?? LabelOf(node),   // ⭐ U-7 ① — the display name ([EditDisplayName], else the humanized field name); a fixed buffer's element rows are named "Slot 1" (N-2 B4)
            ImGuiTreeNodeFlags.Leaf |
            ImGuiTreeNodeFlags.NoTreePushOnOpen |
            ImGuiTreeNodeFlags.SpanAvailWidth);
        if (hoverTint) ImGuiApi.PopStyleColor();
        float rowTop = ImGuiApi.GetItemRectMin().Y, rowBottom = ImGuiApi.GetItemRectMax().Y;
        bool labelRightClicked = bind.Bindable && ImGuiApi.IsItemClicked(ImGuiMouseButton.Right);   // a SHORTCUT to the popup, never the only way

        ImGuiApi.TableSetColumnIndex(1);
        bool canDelete = parentContainer != null && parentContainer.CanResize && elementIndex >= 0;

        // ⭐ N-4 — the changed bar (its slot is reserved only where a default source exists) and the read-only state of this row
        bool ro = ReadOnly || node.IsReadOnly;
        bool changedFromDefault = _defaults != null && IsChangedFromDefault(node);
        if (_defaults != null) NodeEditor.Core.Widgets.RowMarks.ChangedBar(changedFromDefault && !ro);

        // the chip of a bound field sits in front of the value; the faint button of an unbound bindable one at the right edge
        var parentValue = _containers.Count > 0 ? _containers.Peek() : null;
        if (bind.ShowsChip) { _chipRows++; if (bind.ChipIsRed) _redRows++; }
        if (bind.ShowsChip) DrawBindChip(node, bind, parentValue);
        if (bind.ShowsChip) ImGuiApi.SameLine();
        float buttonReserve = bind.ShowsButton ? BindButtonWidth + ImGuiApi.GetStyle().ItemSpacing.X : 0f;
        float inputWidth = ImGuiApi.GetContentRegionAvail().X;
        if (canDelete) inputWidth -= 30f;
        inputWidth -= buttonReserve;
        if (changedFromDefault && !ro) inputWidth -= ImGuiApi.GetFrameHeight() + 8f;   // room for the ↺

        bool changed;
        if (ReferenceLeafDrawers.Handles(node.ClrType))
        {
            // ⭐ CE-4206 R1 (F1) — the reference types (EntityLink, PointLink, EntityRef, WorldPoint) are drawn by ONE class, one method per type:
            //   the TYPE makes the field pickable (DESIGN_Entity_Reference D5, DESIGN_Map_Picking_Unification P5); the attribute only narrows the pick.
            string? boundBefore = bind.Home == BindHome.RefParamForm ? bind.BoundParam : null;
            changed = ReferenceLeafDrawers.Draw(node, (canDelete ? 30f : 0f) + buttonReserve + (changedFromDefault && !ro ? ImGuiApi.GetFrameHeight() + 8f : 0f), _pickerCtx, _refState, ro);
            if (changed) _epoch++;
            // ⭐ CE-4210 — editing the LITERAL of a bound link keeps its binding (SR-168: the literal is the value used when the macro is opened alone)
            if (changed && boundBefore is not null && ParamBindModel.BoundParamOfLink(node.Binding?.GetBoxed()) is null)
                node.Binding?.SetBoxed(ParamBindModel.LinkWithParam(node.Binding.GetBoxed(), boundBefore));
        }
        else
        {
            if (inputWidth < 60f) inputWidth = 60f;
            ImGuiApi.SetNextItemWidth(inputWidth);

            // ⭐ N-3 / N-4 — a text / number / nullable / flags cell parses through ValueText (a refusal keeps the typed text and the old value, with its line under the row);
            //   a value at its default draws grey, a read-only row disabled
            if (ro) ImGuiApi.BeginDisabled();
            if (_defaults != null && !changedFromDefault) ImGuiApi.PushStyleColor(ImGuiCol.Text, ImGuiApi.GetColorU32(ImGuiCol.TextDisabled));
            object? value = null;
            if (DrawCellNode(node, inputWidth, out bool cellChanged)) changed = cellChanged;
            else
            {
                value = node.Binding?.GetBoxed() ?? GetDefaultForType(node.ClrType);
                changed = DrawPrimitiveInput(node.ClrType, ref value!, node);
                if (changed && !ro) node.Binding?.SetBoxed(value);
            }
            if (_defaults != null && !changedFromDefault) ImGuiApi.PopStyleColor();
            if (ro) ImGuiApi.EndDisabled();
            if (changed) _epoch++;   // ⭐ N-2 — cached list / cell text is rebuilt
            // ⭐ CE-4206 R3 F7 — a WorldRotation keeps its Y/P/R editor and gains a muted read-out line through OrientationText
            if (node.ClrType == typeof(Fdp.Core.WorldRotation))
                ImGuiApi.TextDisabled(ReferenceLeafDrawers.RotationReadout(changed && value != null ? value : node.Binding?.GetBoxed()));
        }

        // ⭐ N-4 — ↺: write the default back (ONE Set ⇒ one undo step where the host has undo)
        if (changedFromDefault && !ro && NodeEditor.Core.Widgets.RowMarks.ResetButton("##reset")) ResetToDefault(node);

        // ⭐ CE-4210 MA-1 — the faint ⛓ button, always drawn on an unbound bindable row (left of the array X)
        bool openPopup = labelRightClicked;
        if (bind.ShowsButton)
        {
            ImGuiApi.SameLine();
            _btnRows++;
            openPopup |= DrawBindButton(node, rowHovered: _hoveredRows.Contains(node.Id.Value));
        }

        // Delete button for resizable array elements.
        if (canDelete)
        {
            if (TrashButton(node))
            {
                RemoveElementAtIndex(parentContainer!, elementIndex);
                _session.MarkStructuralChange();
            }
        }

        if (bind.Bindable)
        {
            DrawBindPopup(node, bind, parentValue, openPopup);
            // the row's hover state, kept for NEXT frame's tint (the label is drawn before the value)
            rowBottom = Math.Max(rowBottom, ImGuiApi.GetItemRectMax().Y);
            var win = ImGuiApi.GetWindowPos();
            bool hovered = ImGuiApi.IsMouseHoveringRect(new Vector2(win.X, rowTop), new Vector2(win.X + ImGuiApi.GetWindowWidth(), rowBottom));
            if (hovered) _hoveredRows.Add(node.Id.Value); else _hoveredRows.Remove(node.Id.Value);
        }

        ImGuiApi.PopID();
    }

    // ── ⭐ CE-4210 MA-1 — the bind affordance ─────────────────────────────────────────────────────────────────────────────

    private const float BindButtonWidth = 22f;
    private static readonly Vector4 BindTint = new(0.55f, 0.75f, 1f, 1f);
    private static readonly Vector4 BindRed = new(0.9f, 0.25f, 0.2f, 1f);

    /// <summary>The chain glyph, drawn (the editor font has no ⛓): two interlocked rounded links in <paramref name="col"/>.</summary>
    internal static void DrawChain(Vector2 min, Vector2 max, uint col)
    {
        var dl = ImGuiApi.GetWindowDrawList();
        float w = max.X - min.X, h = max.Y - min.Y, s = MathF.Min(w, h) * 0.5f, cy = (min.Y + max.Y) * 0.5f, cx = (min.X + max.X) * 0.5f;
        float r = s * 0.45f;
        dl.AddRect(new Vector2(cx - s * 0.95f, cy - r), new Vector2(cx + s * 0.15f, cy + r), col, r, ImDrawFlags.None, 1.6f);
        dl.AddRect(new Vector2(cx - s * 0.15f, cy - r), new Vector2(cx + s * 0.95f, cy + r), col, r, ImDrawFlags.None, 1.6f);
    }

    private bool DrawBindButton(EditNode node, bool rowHovered)
    {
        bool clicked = ImGuiApi.InvisibleButton($"##bind_{node.Id.Value}", new Vector2(BindButtonWidth, ImGuiApi.GetFrameHeight()));
        bool itemHovered = ImGuiApi.IsItemHovered();
        // FAINT always (the user: visible without hovering); brighter while the row (or the button) is hovered
        float alpha = itemHovered ? 1f : rowHovered ? 0.8f : 0.38f;
        var col = ImGuiApi.GetColorU32(new Vector4(0.62f, 0.75f, 0.95f, alpha));
        DrawChain(ImGuiApi.GetItemRectMin(), ImGuiApi.GetItemRectMax(), col);
        if (itemHovered || rowHovered && ImGuiApi.IsItemHovered()) ImGuiApi.SetTooltip("Feed from a parameter");
        return clicked;
    }

    private void DrawBindChip(EditNode node, BindView bind, object? parentValue)
    {
        bool red = bind.ChipIsRed;
        string label = bind.BoundParam!;
        float w = ImGuiApi.CalcTextSize("      " + label).X + 14f;
        var colBtn = red ? BindRed with { W = 0.55f } : new Vector4(0.25f, 0.45f, 0.75f, 0.7f);
        ImGuiApi.PushStyleColor(ImGuiCol.Button, colBtn);
        bool clicked = ImGuiApi.Button($"      {label}##chip_{node.Id.Value}", new Vector2(w, 0f));
        ImGuiApi.PopStyleColor();
        var min = ImGuiApi.GetItemRectMin();
        DrawChain(new Vector2(min.X + 3f, min.Y), new Vector2(min.X + 3f + 22f, ImGuiApi.GetItemRectMax().Y), ImGuiApi.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f)));
        if (ImGuiApi.IsItemHovered()) ImGuiApi.SetTooltip(red ? bind.Problem : $"Fed from parameter '{label}' (the value here is used when the macro is opened alone)");
        if (clicked) ImGuiApi.OpenPopup($"##bindpop_{node.Id.Value}");
    }

    private void DrawBindPopup(EditNode node, BindView bind, object? parentValue, bool open)
    {
        string id = $"##bindpop_{node.Id.Value}";
        if (open) ImGuiApi.OpenPopup(id);
        if (!ImGuiApi.BeginPopup(id)) return;
        ImGuiApi.TextDisabled($"Bind {node.Name} to");
        foreach (var p in bind.Fitting)
            if (ImGuiApi.Selectable($"{p.Name}   ({p.TypeId})##bp_{p.Name}", string.Equals(p.Name, bind.BoundParam, StringComparison.Ordinal)))
                ApplyBind(node, parentValue, p.Name);
        if (bind.Fitting.Count == 0) ImGuiApi.TextDisabled("(no parameter of this type)");
        ImGuiApi.Separator();
        if (_bindCtx!.CanDeclareParams && ImGuiApi.Selectable("New parameter…##newp"))
        {
            var names = _bindCtx.Params.Select(p => p.Name);
            if (_bindCtx.NewParam(node.ClrType, ParamBindModel.SuggestParamName(node.Name, names), node.Binding?.GetBoxed()) is { } created)
                ApplyBind(node, parentValue, created.Name);
        }
        ImGuiApi.BeginDisabled(bind.BoundParam is null);
        if (ImGuiApi.Selectable("Unbind##unb"))
            if (ParamBindModel.Unbind(node, parentValue, _bindCtx)) _session.MarkStructuralChange();
        ImGuiApi.EndDisabled();
        ImGuiApi.EndPopup();
    }

    private void ApplyBind(EditNode node, object? parentValue, string param)
    {
        if (ParamBindModel.Bind(node, parentValue, _bindCtx!, param)) _session.MarkStructuralChange();
    }

    // ── Unsupported / fallback nodes ──────────────────────────────────────────

    private static void DrawUnsupportedNode(EditNode node)
    {
        ImGuiApi.TableNextRow();
        ImGuiApi.TableSetColumnIndex(0);
        ImGuiApi.PushID(node.Id.Value);

        ImGuiApi.TreeNodeEx(
            LabelOf(node),
            ImGuiTreeNodeFlags.Leaf |
            ImGuiTreeNodeFlags.NoTreePushOnOpen |
            ImGuiTreeNodeFlags.SpanAvailWidth);

        ImGuiApi.TableSetColumnIndex(1);
        ImGuiApi.TextDisabled(node.Binding?.GetBoxed()?.ToString() ?? "null");

        ImGuiApi.PopID();
    }

    // ── Primitive input controls ──────────────────────────────────────────────

    private bool DrawPrimitiveInput(Type type, ref object value, EditNode node)
    {
        if (_types.TryGetFor(node, out var typeEditor) && typeEditor.CanDraw)
            return typeEditor.Draw(new Types.TypeDrawContext(node, _session, _pickerCtx, _services), ref value);

        var meta = node.Metadata;

        if (type == typeof(Fdp.Toolkit.Utility.UtilityDecisionRef))
        {
            // ⭐ CE-2068 (R-184) — the TYPE makes the field pickable: the authored decisions, by display name.
            var current = value is Fdp.Toolkit.Utility.UtilityDecisionRef d ? d : default;
            var registry = Fdp.Toolkit.Utility.UtilityDecisionCatalog.Shared;
            string preview = current.IsNone ? "(none)"
                : registry.TryGet(current.Id, out var cur, out _) && cur != null ? cur.DebugName : current.ToString();
            bool picked = false;
            if (ImGuiApi.BeginCombo("##decision", preview))
            {
                if (ImGuiApi.Selectable("(none)", current.IsNone)) { value = Fdp.Toolkit.Utility.UtilityDecisionRef.None; picked = true; }
                foreach (var (id, def) in DecisionChoices(registry))
                {
                    if (ImGuiApi.Selectable(def.DebugName + "##" + id, id == current.Id))
                    {
                        value = new Fdp.Toolkit.Utility.UtilityDecisionRef(id);
                        picked = true;
                    }
                }
                ImGuiApi.EndCombo();
            }
            return picked;
        }

        if (type == typeof(float))
        {
            float v = value is float f ? f : 0f;
            bool ok = (meta.Min.HasValue && meta.Max.HasValue)
                ? ImGuiApi.SliderFloat("##v", ref v, (float)meta.Min.Value, (float)meta.Max.Value)
                : NumberInput.Float(ref v, UnitOf(node));   // ⭐ U-7 ⑤ — shortest form + unit; MaxValue = "no limit"
            if (ok) value = v;
            return ok;
        }

        if (type == typeof(int))
        {
            int v = value is int i ? i : 0;
            bool ok = (meta.Min.HasValue && meta.Max.HasValue)
                ? ImGuiApi.SliderInt("##v", ref v, (int)meta.Min.Value, (int)meta.Max.Value)
                : NumberInput.Int(ref v, UnitOf(node));   // ⭐ U-7 ⑤
            if (ok) value = v;
            return ok;
        }

        if (type == typeof(double))
        {
            double v = value is double d ? d : 0.0;
            bool ok = NumberInput.Double(ref v, UnitOf(node));   // ⭐ U-7 ⑤
            if (ok) value = v;
            return ok;
        }

        if (type == typeof(long))
        {
            string strVal = value is long l ? l.ToString() : "0";
            bool ok = ImGuiApi.InputText("##v", ref strVal, 64);
            if (ok && int.TryParse(strVal, out int parsed))
            {
                value = parsed;
                return true;
            }
            return false;
        }

        if (type == typeof(ulong))
        {
            string strVal = value is ulong ul ? ul.ToString() : "0";
            bool ok = ImGuiApi.InputText("##v", ref strVal, 64);
            if (ok && ulong.TryParse(strVal, out ulong parsed))
            {
                value = parsed;
                return true;
            }
            return false;
        }

        if (type == typeof(short))
        {
            int v = value is short s ? s : 0;
            bool ok = ImGuiApi.InputInt("##v", ref v);
            if (ok) value = (short)Math.Clamp(v, short.MinValue, (int)short.MaxValue);
            return ok;
        }

        if (type == typeof(ushort))
        {
            int v = value is ushort us ? us : 0;
            bool ok = ImGuiApi.InputInt("##v", ref v);
            if (ok) value = (ushort)Math.Clamp(v, 0, (int)ushort.MaxValue);
            return ok;
        }

        if (type == typeof(byte))
        {
            int v = value is byte b ? b : 0;
            bool ok = ImGuiApi.InputInt("##v", ref v);
            if (ok) value = (byte)Math.Clamp(v, 0, (int)byte.MaxValue);
            return ok;
        }

        if (type == typeof(sbyte))
        {
            int v = value is sbyte sb ? sb : 0;
            bool ok = ImGuiApi.InputInt("##v", ref v);
            if (ok) value = (sbyte)Math.Clamp(v, sbyte.MinValue, (int)sbyte.MaxValue);
            return ok;
        }

        if (type == typeof(bool))
        {
            bool v = value is bool bv && bv;
            bool ok = ImGuiApi.Checkbox("##v", ref v);
            if (ok) value = v;
            return ok;
        }

        if (type == typeof(string))
        {
            string v = value as string ?? string.Empty;
            bool ok = ImGuiApi.InputText("##v", ref v, 512);
            if (ok) value = v;
            return ok;
        }

        if (type.IsEnum)
        {
            bool isFlags = Attribute.IsDefined(type, typeof(FlagsAttribute));
            string[] names  = Enum.GetNames(type);
            Array    values = Enum.GetValues(type);

            if (isFlags)
            {
                ulong currentMask = Convert.ToUInt64(value ?? Activator.CreateInstance(type)!);
                bool changed = false;

                for (int i = 0; i < values.Length; i++)
                {
                    ulong flagValue = Convert.ToUInt64(values.GetValue(i)!);
                    if (flagValue == 0) continue;

                    bool hasFlag = (currentMask & flagValue) == flagValue;
                    if (ImGuiApi.Checkbox($"{names[i]}##v_{i}", ref hasFlag))
                    {
                        if (hasFlag)
                            currentMask |= flagValue;
                        else
                            currentMask &= ~flagValue;

                        changed = true;
                    }
                }

                if (changed)
                {
                    value = Enum.ToObject(type, currentMask);
                    return true;
                }
                return false;
            }

            {
                int current = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    if (Equals(values.GetValue(i), value))
                    {
                        current = i;
                        break;
                    }
                }

                bool ok = ImGuiApi.Combo("##v", ref current, names, names.Length);
                if (ok) value = values.GetValue(current)!;
                return ok;
            }
        }

        // Unsupported type — show read-only text.
        ImGuiApi.TextDisabled(value?.ToString() ?? "null");
        return false;
    }

    // ── Element removal ───────────────────────────────────────────────────────

    /// <summary>
    /// Shifts all elements after <paramref name="index"/> one position down, then
    /// shrinks the container by one. Exposed <c>internal</c> for unit testing (T-CE07c).
    /// </summary>
    internal static void RemoveElementAtIndex(IContainerBinding container, int index)
    {
        for (int i = index; i < container.Count - 1; i++)
        {
            var next = container.GetElementBinding(i + 1).GetBoxed();
            container.GetElementBinding(i).SetBoxed(next);
        }
        container.Resize(container.Count - 1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object GetDefaultForType(Type type)
    {
        if (type == typeof(string)) return string.Empty;
        if (type.IsValueType)       return Activator.CreateInstance(type)!;
        return null!;
    }

    /// <summary>⭐ CE-2068 — the decisions a <c>UtilityDecisionRef</c> field offers: every registered one, by display name.</summary>
    internal static System.Collections.Generic.List<(int Id, Fdp.Toolkit.Utility.UtilityDecisionDef Def)> DecisionChoices(
        Fdp.Toolkit.Utility.UtilityRegistry registry)
    {
        var list = registry.Entries.ToList();
        list.Sort((a, b) => string.CompareOrdinal(a.Def.DebugName, b.Def.DebugName));
        return list;
    }

    // ── ⭐ U-7 (docs/UX/DESIGN_Condition_Editor.md §8.2) — names, add / delete, units: cached, 0 B per steady frame ─────────────────

    private static readonly Dictionary<string, (string Label, string? Unit)> FieldNames = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> AddLabels = new();
    private static readonly string[] CountLabels = BuildCountLabels(64);

    /// <summary>The row's name: the field's <c>[EditDisplayName]</c> (copied into the node's metadata), else the ONE humanizer
    /// (<see cref="StructEdit.Core.Attributes.DisplayNames"/>); a list element keeps its <c>[i]</c>.</summary>
    internal static string LabelOf(EditNode node)
    {
        if (node.Metadata.DisplayName is { Length: > 0 } dn) return dn;
        return FieldEntry(node.Name).Label;
    }

    /// <summary>The unit a number shows after it: <c>[EditUnit]</c>, else a unit word the field's name ended with; null for none.</summary>
    internal static string? UnitOf(EditNode node) => node.Metadata.Unit ?? FieldEntry(node.Name).Unit;

    private static (string Label, string? Unit) FieldEntry(string name)
    {
        if (FieldNames.TryGetValue(name, out var e)) return e;
        e = name.Length > 0 && name[0] == '[' ? (name, null)
          : (StructEdit.Core.Attributes.DisplayNames.Humanize(name, out var unit), unit);
        FieldNames[name] = e;
        return e;
    }

    private static string ContainerLabelOf(EditNode node, int elementIndex)
    {
        if (elementIndex >= 0 && (node.ClrType.IsAbstract || node.ClrType.IsInterface) && node.Binding?.GetBoxed() is { } v)
            return StructEdit.Core.Attributes.DisplayNames.Of(v.GetType());
        return LabelOf(node);
    }

    private static string AddLabelOf(EditNode node)
    {
        var element = ElementTypeOf(node.ClrType) ?? typeof(object);
        if (AddLabels.TryGetValue(element, out var label)) return label;
        string name = element == typeof(object) ? "item" : StructEdit.Core.Attributes.DisplayNames.Of(element).ToLowerInvariant();
        label = Fdp.Presentation.Fonts.IconsFontAwesome6.Plus + " Add " + name;
        AddLabels[element] = label;
        return label;
    }

    private static Type? ElementTypeOf(Type t)
    {
        if (t.IsArray) return t.GetElementType();
        foreach (var i in t.GetInterfaces())
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return i.GetGenericArguments()[0];
        return null;
    }

    private static string CountLabel(int n) => n >= 0 && n < CountLabels.Length ? CountLabels[n] : $"{n} items";

    private static string[] BuildCountLabels(int n)
    {
        var a = new string[n];
        for (int i = 0; i < n; i++) a[i] = i == 1 ? "1 item" : $"{i} items";
        return a;
    }

    private readonly HashSet<int> _collapsed = new();
    private static readonly Dictionary<int, string> RowIds = new();
    private static readonly Dictionary<Type, NodeEditor.Core.Widgets.IconGlyph?> KindIcons = new();

    private static string RowId(int id)
    {
        if (!RowIds.TryGetValue(id, out var s)) RowIds[id] = s = "kind" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return s;
    }

    /// <summary>⭐ U-7 ② — the glyph of the node's KIND (<c>[EditIcon]</c> on its value's type, a key into the ONE <c>Icons</c> table);
    /// null for a node whose type names none — it keeps the plain tree node.</summary>
    internal static NodeEditor.Core.Widgets.IconGlyph? KindIconOf(EditNode node)
    {
        // the value's runtime type (a polymorphic slot); a concrete node with no value bound (the document's root) is its declared type
        var t = node.Binding?.GetBoxed()?.GetType()
                ?? (node.ClrType.IsAbstract || node.ClrType.IsInterface ? null : node.ClrType);
        if (t is null) return null;
        if (!KindIcons.TryGetValue(t, out var icon))
        {
            icon = StructEdit.Core.Attributes.DisplayNames.IconOf(t) is { } key && NodeEditor.Core.Widgets.Icons.TryGet(key, out var g) ? g : null;
            KindIcons[t] = icon;
        }
        return icon;
    }

    /// <summary>⭐ U-7 ③ — a <see cref="Fdp.Toolkit.Scenario.BindableNode"/>'s <c>Bind</c> map is not a field: a binding shows through the
    /// field's own bind affordance, so the map is never drawn as a row.</summary>
    internal static bool HidesChild(object? parentValue, EditNode child)
        => parentValue is Fdp.Toolkit.Scenario.BindableNode && child.Name == nameof(Fdp.Toolkit.Scenario.BindableNode.Bind);

    /// <summary>⭐ U-7 ⑦ — the delete of a list element: a trash glyph, right-aligned at one x on every row.</summary>
    private bool TrashButton(EditNode node)
    {
        float w = ImGuiApi.CalcTextSize(Fdp.Presentation.Fonts.IconsFontAwesome6.TrashCan).X + ImGuiApi.GetStyle().FramePadding.X * 2f;
        ImGuiApi.SameLine();
        float avail = ImGuiApi.GetContentRegionAvail().X;   // right-aligned in the cell: every trash glyph at one x
        if (avail > w) ImGuiApi.SetCursorPosX(ImGuiApi.GetCursorPosX() + avail - w);
        ImGuiApi.PushID(node.Id.Value);
        bool clicked = ImGuiApi.SmallButton(Fdp.Presentation.Fonts.IconsFontAwesome6.TrashCan + "##del");
        bool hovered = ImGuiApi.IsItemHovered();
        ImGuiApi.PopID();
        if (hovered) ImGuiApi.SetTooltip("Delete");
        return clicked;
    }
}
