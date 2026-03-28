using Xunit;

// Zero-allocation and other timing-sensitive tests must not run alongside parallel
// cases in the same process; full-solution test runs otherwise inflate GC deltas.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
