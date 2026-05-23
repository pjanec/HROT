namespace Hrot.Editor.AiShared.Emit;

public sealed class EmitterOptions
{
    public static readonly EmitterOptions Default = new();

    public string NewLine { get; init; } = Environment.NewLine;
    public string Indent { get; init; } = "    ";
}
