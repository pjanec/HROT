using System;
using System.IO;
using System.Security.Cryptography;

namespace Hrot.Network.NED.Transfer;

/// <summary>⭐ <c>P6</c> part 2 — one offered file, as it rides <c>FileOffer.ItemsJson</c>.</summary>
public sealed record OfferedFile(string RelativePath, long Length, long LastWriteUtcTicks, string Sha256);

/// <summary>⭐ <c>P6</c> part 2 — the protocol's constants and helpers, one definition for the sender and the receiver.</summary>
public static class TransferWire
{
    /// <summary>📐 measured: 0 <c>Write</c> timeouts at 64 KB; 16 KB halves the throughput (§4-E2).</summary>
    public const int ChunkBytes = 64 * 1024;

    /// <summary>Chunks the sender keeps in flight beyond the slowest target (probe: 32).</summary>
    public const int Window = 32;

    /// <summary>A receiver acknowledges its gap-free progress every this many chunks.</summary>
    public const int AckEvery = 8;

    /// <summary>⚠ User J: not for GBs — a larger file is refused with a code and goes to the share.</summary>
    public const long MaxFileBytes = 512L * 1024 * 1024;

    public static string PartitionFor(long transferId) => $"hrot.xfer.{transferId:x}";

    public static int SeqCountFor(long length) => length == 0 ? 1 : (int)((length + ChunkBytes - 1) / ChunkBytes);

    public static string Sha256Of(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f));
    }

    /// <summary>⭐ The ONE freshness rule (<c>DESIGN_Asset_Management.md</c> §1 ②): same length and same last-write time.</summary>
    public static bool AlreadyHeld(string destination, long length, long lastWriteUtcTicks)
    {
        var f = new FileInfo(destination);
        return f.Exists && f.Length == length && f.LastWriteTimeUtc.Ticks == lastWriteUtcTicks;
    }
}
