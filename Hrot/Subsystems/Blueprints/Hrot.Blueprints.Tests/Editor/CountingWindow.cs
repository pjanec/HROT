using Hrot.Blueprints.Editor;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class CountingWindow : BlueprintEditorWindowBase
{
    public int DrawCallCount { get; private set; }
    public override string Title { get; }

    public CountingWindow(string title) => Title = title;

    public override void DrawUI() => DrawCallCount++;
}
