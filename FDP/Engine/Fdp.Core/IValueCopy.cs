namespace Fdp.Core
{
    /// <summary>
    /// ⭐ N-9 (CE-4471; CLAUDE.md "NEW COMPONENTS") — a MANAGED component that is filled in place by its one writer (a per-frame scratch singleton
    /// such as the traffic frame — rebuilding it every frame would allocate every frame, R-2084) and is marked
    /// <see cref="DataPolicy.SnapshotViaClone"/>: wherever a table copy would SHARE the reference with another world (a snapshot, the threaded
    /// node's staging copy to the presentation world), the value is copied INTO the destination world's own instance instead — allocated once
    /// (<see cref="CloneValue"/>), then refilled (<see cref="CopyFrom"/>). So no two threads ever hold the same instance.
    /// </summary>
    public interface IValueCopy<T> where T : class
    {
        /// <summary>A new instance holding this value (the first copy into a world).</summary>
        T CloneValue();

        /// <summary>Overwrites this instance with <paramref name="source"/>'s value (no allocation once its buffers have grown).</summary>
        void CopyFrom(T source);
    }
}
