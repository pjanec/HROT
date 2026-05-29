using System.Collections.Generic;

namespace Hrot.Utility.Editor.Model;

// Editor-only layout state: card order, collapsed flags, pinned fixture.
// Persisted via [UtilityLayout] method in the generated .cs file.
public sealed class UtilityLayoutData
{
    // VisualId order for option cards (empty = natural insertion order).
    public List<string> OptionOrder   = new();
    // VisualIds of options that are collapsed.
    public HashSet<string> Collapsed  = new();
    // Name of the currently pinned fixture (empty = first fixture).
    public string PinnedFixture       = string.Empty;
}
