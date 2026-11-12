namespace Fdp.Core
{
    /// <summary>⭐ CE-4456 N-1 — a table that can say how much memory it holds (<see cref="MemoryCensus"/>).</summary>
    internal interface IMemorySized
    {
        /// <summary>Committed NATIVE bytes (component chunks).</summary>
        long NativeBytes { get; }
        /// <summary>MANAGED slot bytes (the reference arrays of a managed table; the objects themselves are in the GC heap).</summary>
        long ManagedSlotBytes { get; }
    }
}
