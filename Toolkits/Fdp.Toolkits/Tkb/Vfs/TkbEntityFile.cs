using System.IO;

namespace Fdp.Toolkit.Tkb.Vfs
{
    /// <summary>
    /// Represents a single TKB entity file yielded by <see cref="ITkbStorageStrategy"/>.
    /// The <see cref="JsonStream"/> must be consumed or disposed before advancing the enumerator
    /// so that memory is bounded to one file at a time.
    /// </summary>
    /// <param name="CategoryPath">
    /// Forward-slash relative directory path from the storage root, with no leading or trailing
    /// slash. Empty string for entities at the root. Example: "Platform/Vehicle/Military/MBT".
    /// </param>
    /// <param name="FileName">File name without extension. Example: "Merkava Mk4".</param>
    /// <param name="JsonStream">Open stream positioned at the start of the JSON content.</param>
    public readonly record struct TkbEntityFile(
        string CategoryPath,
        string FileName,
        Stream JsonStream);
}
