using System;
using System.Text;

namespace Hrot.Editor.AiShared.Identity;

// Derives a deterministic asset Guid from a tree name string using FNV-1a-32.
public static class AssetIdHasher
{
    public static Guid FromName(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        int hash = AssetIdHash.Fnv1a32(bytes);
        return new Guid(hash, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }
}
