using System;
using Fdp.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser;

/// <summary>
/// Stage 4 stub for the replay search panel.
/// Full implementation is deferred to a future batch.
/// </summary>
public sealed class ReplaySearchPanel
{
    private readonly Action<int> _seekIntent;
    private readonly Action<Entity> _selectIntent;

    public ReplaySearchPanel(Action<int> seekIntent, Action<Entity> selectIntent)
    {
        _seekIntent = seekIntent;
        _selectIntent = selectIntent;
    }

    public void DrawContent() { }
}
