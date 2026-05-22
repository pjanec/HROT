using System.Security.Cryptography;
using System.Text;

namespace NodeEditor.Primitives;

/// <summary>
/// Centralized ID generation. Provides random and deterministic-from-string
/// variants. Use deterministic for content-based identity (e.g. "the
/// breakpoint at this node") and random for new mutable entities.
/// </summary>
public static class IdGenerator
{
    /// <summary>Generates a new random NodeId.</summary>
    public static NodeId NewNodeId() => NodeId.NewId();

    /// <summary>Generates a new random PinId.</summary>
    public static PinId NewPinId() => PinId.NewId();

    /// <summary>Generates a new random LinkId.</summary>
    public static LinkId NewLinkId() => LinkId.NewId();

    /// <summary>Generates a new random GraphId.</summary>
    public static GraphId NewGraphId() => GraphId.NewId();

    /// <summary>Generates a new random CommentId.</summary>
    public static CommentId NewCommentId() => CommentId.NewId();

    /// <summary>
    /// Generates a deterministic Guid from an input string by SHA-256
    /// hashing. Same string ⇒ same Guid. Useful when an ID must be
    /// reconstructable from stable content.
    /// </summary>
    public static Guid Deterministic(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(input), hash);
        // Take first 16 bytes; set version + variant per RFC 4122 v5 conventions.
        Span<byte> guidBytes = stackalloc byte[16];
        hash[..16].CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // variant
        return new Guid(guidBytes);
    }

    /// <summary>Deterministic NodeId from input string.</summary>
    public static NodeId DeterministicNodeId(string input) => new(Deterministic(input));

    /// <summary>Deterministic PinId from input string.</summary>
    public static PinId DeterministicPinId(string input) => new(Deterministic(input));

    /// <summary>Deterministic LinkId from input string.</summary>
    public static LinkId DeterministicLinkId(string input) => new(Deterministic(input));
}
