using System;
using StructEdit.Core;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

public sealed class GuidFieldDrawer : IImGuiFieldDrawer
{
    public Type TargetType => typeof(Guid);

    public bool DrawInput(ref object value, EditNodeMetadata meta)
    {
        Guid current = value is Guid guid ? guid : Guid.Empty;
        string text = current.ToString();

        bool changed = ImGuiApi.InputText("##v", ref text, 64);
        if (!changed)
            return false;

        if (!Guid.TryParse(text, out Guid parsed))
            return false;

        value = parsed;
        return true;
    }
}
