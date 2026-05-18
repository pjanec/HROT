using System.Collections.Generic;
using Fdp.Toolkit.ReplayBrowser.Diff;

namespace Fdp.Toolkit.ReplayBrowser
{
    public sealed record ChangelogEntryDto(
        int FrameIndex,
        long WallClockTicks,
        double RelativeWallTimeSec,
        double SimTimeSec,
        string EntityHandle,
        IReadOnlyList<DiffNode> Mutations);
}
