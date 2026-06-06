using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.ReplayBrowser.Search;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// Recursive ImGui renderer for an <see cref="IEditSession"/> document tree.
/// Draws each <see cref="EditNode"/> as one or more rows in the two-column
/// "Property | Value" table owned by <see cref="ComponentEditWindow"/>.
/// </summary>
public sealed class ComponentEditDrawer
{
    private readonly IEditSession _session;
    private readonly IComponentPickerContext? _pickerCtx;
    private readonly IReadOnlyDictionary<Type, IImGuiFieldDrawer> _customDrawers;
    private readonly ISpatialPickerContext? _spatialPickerCtx;

    public ComponentEditDrawer(
        IEditSession session,
        IComponentPickerContext? pickerCtx,
        IReadOnlyDictionary<Type, IImGuiFieldDrawer>? customDrawers = null,
        ISpatialPickerContext? spatialPickerCtx = null)
    {
        _session            = session;
        _pickerCtx          = pickerCtx;
        _customDrawers      = customDrawers ?? new Dictionary<Type, IImGuiFieldDrawer>();
        _spatialPickerCtx   = spatialPickerCtx;
    }

    // ── Public API ────────────────────────────────────────────────────────────

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

        bool opened = ImGuiApi.TreeNodeEx(
            node.Name,
            ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen);

        ImGuiApi.TableSetColumnIndex(1);
        bool canDelete = parentContainer != null && parentContainer.CanResize && elementIndex >= 0;

        var containerBinding = node.Binding as IContainerBinding;
        if (containerBinding != null)
        {
            ImGuiApi.TextDisabled($"[{containerBinding.Count}]");

            if (containerBinding.CanResize)
            {
                ImGuiApi.SameLine(ImGuiApi.GetContentRegionAvail().X - 60f);
                if (ImGuiApi.SmallButton("+Add"))
                {
                    containerBinding.Resize(containerBinding.Count + 1);
                    _session.MarkStructuralChange();
                }
            }
            if (canDelete)
            {
                ImGuiApi.SameLine(ImGuiApi.GetContentRegionAvail().X - 30f);
                if (ImGuiApi.SmallButton($"X##del_{node.Id.Value}"))
                {
                    RemoveElementAtIndex(parentContainer!, elementIndex);
                    _session.MarkStructuralChange();
                }
            }
        }
        else if (node.ClrType.IsAbstract || node.ClrType.IsInterface)
        {
            object? currentObj = node.Binding?.GetBoxed();
            string preview = currentObj != null ? currentObj.GetType().Name : "(null)";

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
                    if (ImGuiApi.Selectable("(null)", false))
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
                        if (ImGuiApi.Selectable(t.Name, isSelected))
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
                ImGuiApi.SameLine();
                if (ImGuiApi.SmallButton($"X##del_{node.Id.Value}"))
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
                ImGuiApi.SameLine(ImGuiApi.GetContentRegionAvail().X - 30f);
                if (ImGuiApi.SmallButton($"X##del_{node.Id.Value}"))
                {
                    RemoveElementAtIndex(parentContainer!, elementIndex);
                    _session.MarkStructuralChange();
                }
            }
        }

        // Picker: bounding box area (spatial search) for container-style nodes.
        var bboxAttr = node.Metadata.CustomAttributes
            .OfType<MapPickableBoundingBoxAttribute>().FirstOrDefault();
        if (bboxAttr != null && _spatialPickerCtx != null)
        {
            ImGuiApi.SameLine();
            if (_spatialPickerCtx.IsPickPendingFor(node.JsonPath))
            {
                ImGuiApi.TextColored(new Vector4(1f, 1f, 0f, 1f), "[Picking on Map...]");
            }
            else
            {
                if (ImGuiApi.Button($"...##{node.Id.Value}"))
                    _spatialPickerCtx.RequestBoundingBoxPick(node.JsonPath);
                if (ImGuiApi.IsItemHovered(ImGuiHoveredFlags.DelayNormal))
                    ImGuiApi.SetTooltip("Define area boundaries on map");
            }

            if (_spatialPickerCtx.TryConsumeBoundingBoxPick(node.JsonPath, out var pickedBox))
            {
                node.Binding?.SetBoxed(pickedBox);
            }
        }

        if (opened)
        {
            int i = 0;
            foreach (var child in node.Children)
            {
                DrawEditNode(child, containerBinding, i);
                i++;
            }
            ImGuiApi.TreePop();
        }

        ImGuiApi.PopID();
    }

    // ── Leaf nodes ────────────────────────────────────────────────────────────

    private void DrawLeafNode(EditNode node, IContainerBinding? parentContainer, int elementIndex)
    {
        ImGuiApi.TableNextRow();
        ImGuiApi.TableSetColumnIndex(0);
        ImGuiApi.PushID(node.Id.Value);

        ImGuiApi.TreeNodeEx(
            node.Name,
            ImGuiTreeNodeFlags.Leaf |
            ImGuiTreeNodeFlags.NoTreePushOnOpen |
            ImGuiTreeNodeFlags.SpanAvailWidth);

        ImGuiApi.TableSetColumnIndex(1);
        bool canDelete = parentContainer != null && parentContainer.CanResize && elementIndex >= 0;
        float inputWidth = ImGuiApi.GetContentRegionAvail().X;
        if (canDelete) inputWidth -= 30f;

        var entityAttr = node.Metadata.CustomAttributes.OfType<MapPickableEntityAttribute>().FirstOrDefault();
        if (entityAttr != null && _pickerCtx != null) inputWidth -= 90f;

        var locationAttr = node.Metadata.CustomAttributes.OfType<MapPickableWorldLocationAttribute>().FirstOrDefault();
        if (locationAttr != null && _pickerCtx != null) inputWidth -= 90f;

        if (inputWidth < 60f) inputWidth = 60f;
        ImGuiApi.SetNextItemWidth(inputWidth);

        object value = node.Binding?.GetBoxed() ?? GetDefaultForType(node.ClrType);
        bool changed = DrawPrimitiveInput(node.ClrType, ref value, node);

        if (changed)
            node.Binding?.SetBoxed(value);

        // Delete button for resizable array elements.
        if (canDelete)
        {
            ImGuiApi.SameLine();
            if (ImGuiApi.SmallButton($"X##del_{node.Id.Value}"))
            {
                RemoveElementAtIndex(parentContainer!, elementIndex);
                _session.MarkStructuralChange();
            }
        }

        // Picker: entity reference.
        if (entityAttr != null && _pickerCtx != null)
        {
            ImGuiApi.SameLine();
            if (_pickerCtx.IsPickPendingFor(node.JsonPath))
            {
                ImGuiApi.TextColored(new Vector4(1f, 1f, 0f, 1f), "[Picking...]");
            }
            else if (ImGuiApi.Button($"Pick Entity##{node.Id.Value}"))
            {
                _pickerCtx.RequestEntityPick(
                    node.JsonPath,
                    entityAttr.FilterPresets.Length > 0 ? entityAttr.FilterPresets : null);
            }

            if (_pickerCtx.TryConsumeEntityPick(node.JsonPath, out var pickedEntity))
            {
                node.Binding?.SetBoxed(pickedEntity);
                changed = true;
            }
        }

        // Picker: world location.
        if (locationAttr != null && _pickerCtx != null)
        {
            ImGuiApi.SameLine();
            if (_pickerCtx.IsPickPendingFor(node.JsonPath))
            {
                ImGuiApi.TextColored(new Vector4(1f, 1f, 0f, 1f), "[Picking...]");
            }
            else if (ImGuiApi.Button($"Pick Map##{node.Id.Value}"))
            {
                _pickerCtx.RequestLocationPick(node.JsonPath);
            }

            if (_pickerCtx.TryConsumeLocationPick(node.JsonPath, out var location))
            {
                node.Binding?.SetBoxed(location);
                changed = true;
            }
        }

        // Picker: bounding box area (spatial search).
        var bboxAttr = node.Metadata.CustomAttributes
            .OfType<MapPickableBoundingBoxAttribute>().FirstOrDefault();
        if (bboxAttr != null && _spatialPickerCtx != null)
        {
            ImGuiApi.SameLine();
            if (_spatialPickerCtx.IsPickPendingFor(node.JsonPath))
            {
                ImGuiApi.TextColored(new Vector4(1f, 1f, 0f, 1f), "[Picking on Map...]");
            }
            else
            {
                if (ImGuiApi.Button($"...##{node.Id.Value}"))
                    _spatialPickerCtx.RequestBoundingBoxPick(node.JsonPath);
                if (ImGuiApi.IsItemHovered(ImGuiHoveredFlags.DelayNormal))
                    ImGuiApi.SetTooltip("Define area boundaries on map");
            }

            if (_spatialPickerCtx.TryConsumeBoundingBoxPick(node.JsonPath, out var pickedBox))
            {
                node.Binding?.SetBoxed(pickedBox);
                changed = true;
            }
        }

        ImGuiApi.PopID();
    }

    // ── Unsupported / fallback nodes ──────────────────────────────────────────

    private static void DrawUnsupportedNode(EditNode node)
    {
        ImGuiApi.TableNextRow();
        ImGuiApi.TableSetColumnIndex(0);
        ImGuiApi.PushID(node.Id.Value);

        ImGuiApi.TreeNodeEx(
            node.Name,
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
        if (_customDrawers.TryGetValue(type, out var customDrawer))
            return customDrawer.DrawInput(ref value, node);

        var meta = node.Metadata;

        if (type == typeof(float))
        {
            float v = value is float f ? f : 0f;
            bool ok = (meta.Min.HasValue && meta.Max.HasValue)
                ? ImGuiApi.SliderFloat("##v", ref v, (float)meta.Min.Value, (float)meta.Max.Value)
                : ImGuiApi.InputFloat("##v", ref v, 0f, 0f);
            if (ok) value = v;
            return ok;
        }

        if (type == typeof(int))
        {
            int v = value is int i ? i : 0;
            bool ok = (meta.Min.HasValue && meta.Max.HasValue)
                ? ImGuiApi.SliderInt("##v", ref v, (int)meta.Min.Value, (int)meta.Max.Value)
                : ImGuiApi.InputInt("##v", ref v);
            if (ok) value = v;
            return ok;
        }

        if (type == typeof(double))
        {
            double v = value is double d ? d : 0.0;
            bool ok = ImGuiApi.InputDouble("##v", ref v);
            if (ok) value = v;
            return ok;
        }

        if (type == typeof(long))
        {
            string strVal = value is long l ? l.ToString() : "0";
            bool ok = ImGuiApi.InputText("##v", ref strVal, 64);
            if (ok && long.TryParse(strVal, out long parsed))
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
}
