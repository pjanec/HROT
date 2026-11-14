using System;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Hrot.SimHost.Serializers;

/// <summary>S8 (B) - a scenario FILE names another entity by its GUID only; the load's scope resolves it into <c>.Ref</c> (the same path as every authored reference).</summary>
internal static class FileLinks
{
    public static EntityLink FromGuidText(string? text)
        => Guid.TryParse(text, out var g) && g != Guid.Empty ? new EntityLink(default, RefSpec.ByExact(g)) : EntityLink.None;
}
