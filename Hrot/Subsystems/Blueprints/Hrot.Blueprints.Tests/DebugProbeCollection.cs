using Xunit;

namespace Hrot.Blueprints.Tests;

// xUnit collection that serializes all test classes that mutate the process-wide
// DebugProbe.Sink static. Classes in this collection run sequentially with respect
// to each other; they run in parallel with classes NOT in this collection.
[CollectionDefinition("DebugProbe")]
public sealed class DebugProbeCollection { }
