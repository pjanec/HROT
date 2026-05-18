using System;
using System.Reflection;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser.Drawers;

using ImGuiApi = ImGuiNET.ImGui;

internal sealed class PredicateValueFieldDrawer : IImGuiFieldDrawer
{
    private readonly IEditSession _session;
    private readonly BehaviorRegistry _behaviorRegistry;

    public PredicateValueFieldDrawer(IEditSession session, BehaviorRegistry behaviorRegistry)
    {
        _session = session;
        _behaviorRegistry = behaviorRegistry;
    }

    public Type TargetType => typeof(SearchPredicateDto);

    public bool DrawInput(ref object value, EditNode node)
    {
        Type? targetType = GetTargetType(node.JsonPath);
        string? propertyPath = GetSiblingValue<string>(node.JsonPath, "PropertyPath");
        SearchOperator op = GetSiblingValue<SearchOperator>(node.JsonPath, "Operator");

        if (targetType == null || string.IsNullOrEmpty(propertyPath))
        {
            ImGuiApi.BeginDisabled();
            ImGuiApi.TextDisabled("(Select PropertyPath first)");
            ImGuiApi.EndDisabled();
            return false;
        }

        Type? propertyClrType = ResolvePropertyType(targetType, propertyPath);
        if (propertyClrType == null)
        {
            ImGuiApi.TextDisabled("(Invalid Path)");
            return false;
        }

        bool changed = false;
        string widgetId = $"##pred_{node.Id.Value}";

        if (IsNumeric(propertyClrType) || propertyClrType.IsEnum || propertyClrType == typeof(bool))
        {
            if (value is not NumericPredicateDto num)
            {
                num = new NumericPredicateDto { MinValue = 0, MaxValue = 0 };
                value = num;
                changed = true;
            }

            ImGuiApi.SetNextItemWidth(ImGuiApi.GetContentRegionAvail().X);

            if (propertyClrType == typeof(bool))
            {
                int boolIndex = num.MinValue > 0.5 ? 1 : 0;
                if (ImGuiApi.Combo(widgetId, ref boolIndex, "False\0True\0"))
                {
                    num.MinValue = boolIndex;
                    num.MaxValue = boolIndex;
                    changed = true;
                }
            }
            else if (propertyClrType.IsEnum)
            {
                string[] names = Enum.GetNames(propertyClrType);
                Array enumValues = Enum.GetValues(propertyClrType);

                int currentIndex = -1;
                for (int i = 0; i < enumValues.Length; i++)
                {
                    double enumVal = Convert.ToDouble(enumValues.GetValue(i)!);
                    if (Math.Abs(enumVal - num.MinValue) < 0.001)
                    {
                        currentIndex = i;
                        break;
                    }
                }
                if (currentIndex < 0 && enumValues.Length > 0)
                    currentIndex = 0;

                if (ImGuiApi.Combo(widgetId, ref currentIndex, names, names.Length))
                {
                    double enumVal = Convert.ToDouble(enumValues.GetValue(currentIndex)!);
                    num.MinValue = enumVal;
                    num.MaxValue = enumVal;
                    changed = true;
                }
            }
            else
            {
                if (op == SearchOperator.Equals)
                {
                    double v = num.MinValue;
                    if (ImGuiApi.InputDouble(widgetId, ref v))
                    {
                        num.MinValue = v;
                        num.MaxValue = v;
                        changed = true;
                    }
                }
                else if (op == SearchOperator.GreaterThan)
                {
                    double v = num.MinValue;
                    if (ImGuiApi.InputDouble(widgetId, ref v))
                    {
                        num.MinValue = v;
                        changed = true;
                    }
                }
                else if (op == SearchOperator.LessThan)
                {
                    double v = num.MaxValue;
                    if (ImGuiApi.InputDouble(widgetId, ref v))
                    {
                        num.MaxValue = v;
                        changed = true;
                    }
                }
                else if (op == SearchOperator.Changed)
                {
                    ImGuiApi.TextDisabled("(Any Change)");
                }
                else
                {
                    ImGuiApi.TextDisabled("N/A");
                }
            }
        }
        else
        {
            if (value is not StringPredicateDto str)
            {
                str = new StringPredicateDto();
                value = str;
                changed = true;
            }

            if (op == SearchOperator.Changed)
            {
                ImGuiApi.TextDisabled("(Any Change)");
            }
            else if (IsComplexType(propertyClrType))
            {
                ImGuiApi.TextDisabled("(Complex object - pick a leaf field)");
            }
            else
            {
                string s = str.Substring ?? string.Empty;
                ImGuiApi.SetNextItemWidth(ImGuiApi.GetContentRegionAvail().X);
                if (ImGuiApi.InputText(widgetId, ref s, 256))
                {
                    str.Substring = s;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool IsNumeric(Type t)
    {
        return t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong)
            || t == typeof(short) || t == typeof(ushort) || t == typeof(byte) || t == typeof(sbyte)
            || t == typeof(float) || t == typeof(double) || t == typeof(decimal);
    }

    private static bool IsComplexType(Type t)
    {
        return !(t.IsPrimitive
            || t.IsEnum
            || t == typeof(string)
            || t == typeof(decimal)
            || t == typeof(Guid)
            || t == typeof(Fdp.Core.FixedString32)
            || t == typeof(Fdp.Core.FixedString64));
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
            if (child.Name == "ComponentType" || child.Name == "EventType")
                return child.Binding?.GetBoxed() as Type;
            if (child.Name == "BehaviorId")
            {
                if (child.Binding?.GetBoxed() is int hash
                    && hash != 0
                    && _behaviorRegistry.TryGetDefinition(hash, out var def)
                    && def.ParamsDtoType != null)
                {
                    return def.ParamsDtoType;
                }
            }
        }

        return null;
    }

    private T? GetSiblingValue<T>(string jsonPath, string siblingName)
    {
        int lastDot = jsonPath.LastIndexOf('.');
        if (lastDot <= 0)
            return default;

        string parentPath = jsonPath.Substring(0, lastDot);
        EditNode? parentNode = FindNodeByPath(_session.Document.Root, parentPath);
        if (parentNode == null)
            return default;

        foreach (EditNode child in parentNode.Children)
        {
            if (child.Name != siblingName)
                continue;

            object? val = child.Binding?.GetBoxed();
            if (val == null)
                return default;
            if (val is T typed)
                return typed;

            if (typeof(T).IsEnum)
            {
                try { return (T)Enum.Parse(typeof(T), val.ToString()!); }
                catch { return default; }
            }

            try { return (T?)Convert.ChangeType(val, typeof(T)); }
            catch { return default; }
        }

        return default;
    }

    private static Type? ResolvePropertyType(Type rootType, string propertyPath)
    {
        Type currentType = rootType;
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;

        foreach (string rawSegment in propertyPath.Split('.'))
        {
            string segment = rawSegment;
            int bracket = segment.IndexOf('[');
            bool hasIndexer = bracket > 0;
            if (hasIndexer)
                segment = segment.Substring(0, bracket);

            FieldInfo? fi = currentType.GetField(segment, flags);
            if (fi != null)
            {
                currentType = fi.FieldType;
                if (hasIndexer)
                    currentType = UnwrapCollectionElement(currentType);
                continue;
            }

            PropertyInfo? pi = currentType.GetProperty(segment, flags);
            if (pi != null)
            {
                currentType = pi.PropertyType;
                if (hasIndexer)
                    currentType = UnwrapCollectionElement(currentType);
                continue;
            }

            return null;
        }

        return currentType;
    }

    private static Type UnwrapCollectionElement(Type type)
    {
        if (type.IsArray)
            return type.GetElementType() ?? typeof(object);
        if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            return type.GetGenericArguments()[0];
        return type;
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
}
