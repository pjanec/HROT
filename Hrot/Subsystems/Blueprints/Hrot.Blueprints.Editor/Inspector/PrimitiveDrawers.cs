namespace Hrot.Blueprints.Editor.Inspector;

/// <summary>ImGui-based float input drawer. Requires editor runtime to render.</summary>
public sealed class FloatDrawer : IStructEditDrawer<float>
{
    public bool Draw(string label, ref float value, DrawContext ctx)
    {
        if (ctx.IsReadOnly) return false;
        // ImGui.InputFloat(label, ref value) would go here.
        return false;  // No modification without ImGui runtime.
    }
}

public sealed class IntDrawer : IStructEditDrawer<int>
{
    public bool Draw(string label, ref int value, DrawContext ctx)
    {
        if (ctx.IsReadOnly) return false;
        return false;
    }
}

public sealed class BoolDrawer : IStructEditDrawer<bool>
{
    public bool Draw(string label, ref bool value, DrawContext ctx)
    {
        if (ctx.IsReadOnly) return false;
        return false;
    }
}

public sealed class StringDrawer : IStructEditDrawer<string>
{
    public bool Draw(string label, ref string value, DrawContext ctx)
    {
        if (ctx.IsReadOnly) return false;
        return false;
    }
}
