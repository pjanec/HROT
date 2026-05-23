using Hrot.Editor.AiShared.Selection;

namespace Hrot.Hsm.Editor.Inspector;

// Sub-selection record for when an event row in the events table is selected.
public sealed record HsmEventSelection(ushort EventId) : IAssetSubSelection;

// Sub-selection record for when a global transition chip is selected.
public sealed record HsmGlobalTransitionSelection(Guid VisualId) : IAssetSubSelection;
