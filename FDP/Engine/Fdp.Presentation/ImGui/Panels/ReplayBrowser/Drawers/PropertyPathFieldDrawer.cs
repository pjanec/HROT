using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser.Drawers;

using ImGuiApi = ImGuiNET.ImGui;

internal sealed class PropertyPathFieldDrawer : IImGuiFieldDrawer
{
    private readonly IEditSession _session;
    private string _filter = string.Empty;

    public PropertyPathFieldDrawer(IEditSession session)
    {
        _session = session;
    }

    public Type TargetType => typeof(string);

    public bool DrawInput(ref object value, EditNode node)
    {
        string strVal = value as string ?? string.Empty;
        bool isPicker = node.Metadata.CustomAttributes.Any(a => a is PropertyPathPickerAttribute);

        if (!isPicker)
        {
            bool changed = ImGuiApi.InputText("##v", ref strVal, 256);
            if (changed)
                value = strVal;
            return changed;
        }

        bool valueChanged = false;
        Type? targetType = GetTargetType(node.JsonPath);

        if (targetType == null)
        {
            ImGuiApi.BeginDisabled();
            if (ImGuiApi.BeginCombo($"##path_disabled_{node.Id.Value}", "(Select Type first)"))
            {
                ImGuiApi.EndCombo();
            }
            ImGuiApi.EndDisabled();
            return false;
        }

        string preview = string.IsNullOrEmpty(strVal) ? "(empty)" : strVal;
        ImGuiApi.SetNextItemWidth(ImGuiApi.GetContentRegionAvail().X);
        string comboId = $"##path_combo_{node.Id.Value}";
        if (ImGuiApi.BeginCombo(comboId, preview))
        {
            ImGuiApi.InputTextWithHint($"##path_filter_{node.Id.Value}", "Filter...", ref _filter, 128);

            var paths = new List<string>();
            CollectPaths(targetType, string.Empty, paths, 0);
            var orderedPaths = paths.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

            if (orderedPaths.Count == 0)
            {
                ImGuiApi.Selectable($"(No properties on {targetType.Name})", false);
            }
            else
            {
                if (string.IsNullOrEmpty(_filter) ||
                    "(empty)".Contains(_filter, StringComparison.OrdinalIgnoreCase))
                {
                    if (ImGuiApi.Selectable("(empty)", strVal == string.Empty))
                    {
                        value = string.Empty;
                        valueChanged = true;
                    }
                }

                foreach (string path in orderedPaths)
                {
                    if (!string.IsNullOrEmpty(_filter) &&
                        path.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    bool selected = strVal == path;
                    if (selected)
                        ImGuiApi.SetItemDefaultFocus();

                    if (ImGuiApi.Selectable(path, selected))
                    {
                        value = path;
                        valueChanged = true;
                        ImGuiApi.CloseCurrentPopup();
                    }
                }
            }

            ImGuiApi.EndCombo();
        }

        return valueChanged;
    }

    private Type? GetTargetType(string jsonPath)
    {
        int lastDot = jsonPath.LastIndexOf('.');
        if (lastDot <= 0)
            return null;

        string parentPath = jsonPath.Substring(0, lastDot);
        EditNode? parentNode = FindNodeByPath(_session.Document.Root, parentPath);
        if (parentNode == null)
            return null;

        foreach (EditNode child in parentNode.Children)
        {
            if (child.Name == "ComponentType" ||
                child.Name == "EventType" ||
                child.Name == "NameComponentType" ||
                child.Name == "PositionComponentType")
            {
                if (child.Binding?.GetBoxed() is Type selectedType)
                    return selectedType;
            }
        }

        return null;
    }

    private static EditNode? FindNodeByPath(EditNode current, string targetPath)
    {
        if (current.JsonPath == targetPath)
            return current;

        if (targetPath.StartsWith(current.JsonPath, StringComparison.Ordinal))
        {
            foreach (EditNode child in current.Children)
            {
                EditNode? found = FindNodeByPath(child, targetPath);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static void CollectPaths(Type type, string prefix, List<string> paths, int depth)
    {
        if (depth > 5)
            return;

        var flags = BindingFlags.Public | BindingFlags.Instance;

        foreach (FieldInfo field in type.GetFields(flags))
        {
            string path = prefix + field.Name;
            paths.Add(path);
            if (IsComplexType(field.FieldType))
                CollectPaths(field.FieldType, path + ".", paths, depth + 1);
        }

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;
            string path = prefix + property.Name;
            paths.Add(path);
            if (IsComplexType(property.PropertyType))
                CollectPaths(property.PropertyType, path + ".", paths, depth + 1);
        }
    }

    private static bool IsComplexType(Type type)
    {
        return !(type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal));
    }
}
