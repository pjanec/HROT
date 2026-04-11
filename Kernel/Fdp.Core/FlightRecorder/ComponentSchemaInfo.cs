namespace Fdp.Kernel.FlightRecorder
{
    /// <summary>
    /// Captures the structural layout of a single ECS component type at record time.
    /// Stored in the <c>.meta.json</c> schema manifest so that playback can verify
    /// that no struct layouts have changed between the recording and the current binary.
    ///
    /// <para>
    /// The <see cref="LayoutHash"/> is an FNV-1a 64-bit hash over all instance field
    /// names, their declaring types, and their memory offsets (computed by
    /// <see cref="ComponentLayoutHasher"/>).  A hash mismatch or a size change
    /// detected by <see cref="SchemaValidator"/> during playback startup indicates
    /// silent memory corruption risk, causing an <see cref="System.InvalidOperationException"/>
    /// to be thrown before any binary frames are read.
    /// </para>
    /// </summary>
    public class ComponentSchemaInfo
    {
        /// <summary>
        /// Fully qualified type name of the component struct (e.g. <c>Fdp.Kernel.SimTransform</c>).
        /// Used for human-readable diagnostics when a mismatch is reported.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// <see cref="System.Runtime.InteropServices.Marshal.SizeOf"/> of the component struct
        /// at record time, in bytes.
        /// </summary>
        public int Size { get; set; }

        /// <summary>
        /// FNV-1a 64-bit hash over all instance field names, type names, and memory offsets
        /// as computed by <see cref="ComponentLayoutHasher.ComputeHash"/>.
        /// Deterministic across runs as long as the struct layout has not changed.
        /// </summary>
        public ulong LayoutHash { get; set; }

        /// <summary>
        /// <c>true</c> if this component was registered as a managed (Tier 2 class) component;
        /// <c>false</c> for unmanaged (Tier 1 struct) components.
        /// </summary>
        public bool IsManaged { get; set; }
    }
}
