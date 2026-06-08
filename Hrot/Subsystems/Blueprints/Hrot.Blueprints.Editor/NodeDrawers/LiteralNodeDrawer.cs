using System.Globalization;
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Node-drawer for <see cref="LiteralNode"/>.
/// Provides an Inspector (Details Panel) editor for the literal value, typed per
/// <see cref="LiteralNode.TypeId"/> so the Stage 7 emitter receives correctly-formatted
/// <see cref="LiteralNode.ValueJson"/> (C# literal syntax).
/// </summary>
public sealed class LiteralNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;

    public LiteralNodeDrawer(IEditService editService)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    public bool Handles(Node node) => node is LiteralNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new LiteralNodeSession((LiteralNode)node, parentAsset, _editService);
}

internal sealed class LiteralNodeSession : INodeEditSession
{
    private readonly LiteralNode     _node;
    private readonly BlueprintAsset  _parent;
    private readonly IEditService    _editService;

    public bool IsDirty { get; private set; }

    public LiteralNodeSession(
        LiteralNode node,
        BlueprintAsset parentAsset,
        IEditService editService)
    {
        _node        = node;
        _parent      = parentAsset;
        _editService = editService;
    }

    public void Draw()
    {
        ImGui.TextDisabled($"Literal ({ShortTypeName(_node.TypeId)})");
        ImGui.Separator();

        string rawValue = _node.ValueJson ?? string.Empty;
        bool changed = false;

        if (_node.TypeId == BlueprintTypeSystem.Int32)
        {
            int val = int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i : 0;
            if (ImGui.InputInt("Value", ref val))
            {
                _node.ValueJson = val.ToString(CultureInfo.InvariantCulture);
                changed = true;
            }
        }
        else if (_node.TypeId == BlueprintTypeSystem.Single)
        {
            // Strip any existing 'f' suffix before parsing
            var clean = rawValue.TrimEnd('f', 'F');
            float val = float.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? f : 0f;
            if (ImGui.InputFloat("Value", ref val))
            {
                // Float literals MUST have 'f' suffix for Roslyn (avoids CS0664)
                _node.ValueJson = val.ToString(CultureInfo.InvariantCulture) + "f";
                changed = true;
            }
        }
        else if (_node.TypeId == BlueprintTypeSystem.Float64)
        {
            double val = double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d : 0.0;
            if (ImGui.InputDouble("Value", ref val))
            {
                _node.ValueJson = val.ToString(CultureInfo.InvariantCulture);
                changed = true;
            }
        }
        else if (_node.TypeId == BlueprintTypeSystem.Bool)
        {
            bool val = bool.TryParse(rawValue, out var b) && b;
            if (ImGui.Checkbox("Value", ref val))
            {
                _node.ValueJson = val ? "true" : "false";
                changed = true;
            }
        }
        else if (_node.TypeId == BlueprintTypeSystem.String)
        {
            // Strip surrounding quotes for editing
            var clean = rawValue.Trim('\"');
            var buf   = clean;
            if (ImGui.InputText("Value", ref buf, 1024))
            {
                // String literals must be wrapped in quotes for the generated C# source
                _node.ValueJson = $"\"{buf}\"";
                changed = true;
            }
        }
        else if (_node.TypeId == BlueprintTypeSystem.Byte)
        {
            byte val = byte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)
                ? b : (byte)0;
            int asInt = val;
            if (ImGui.InputInt("Value", ref asInt))
            {
                asInt = Math.Clamp(asInt, byte.MinValue, byte.MaxValue);
                _node.ValueJson = ((byte)asInt).ToString(CultureInfo.InvariantCulture);
                changed = true;
            }
        }
        else
        {
            // Fallback for unknown types: raw C# literal text entry
            if (ImGui.InputText("C# Literal", ref rawValue, 256))
            {
                _node.ValueJson = rawValue;
                changed = true;
            }
        }

        if (changed)
        {
            IsDirty = true;
            _editService.MarkDirty(_parent);
        }
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }

    private static string ShortTypeName(string typeId)
    {
        if (string.IsNullOrEmpty(typeId)) return "?";
        var dot = typeId.LastIndexOf('.');
        return dot >= 0 ? typeId[(dot + 1)..] : typeId;
    }
}
