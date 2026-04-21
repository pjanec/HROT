using System;
using System.Collections.Generic;

namespace Hrot.Core.Network;

/// <summary>
/// An <see cref="IEntityCreationRequestSource"/> that wraps an ordered list of inner sources
/// and drains all of them in sequence during each <see cref="ProcessRequests"/> call.
///
/// <para><b>Drain order:</b> inner sources are drained in the order they were supplied to
/// the constructor. Each source's own per-tick cap (if any) is respected independently.</para>
///
/// <para><b>Error handling:</b> exceptions thrown by any inner source propagate to the caller;
/// they are not swallowed.</para>
/// </summary>
public sealed class CompositeEntityCreationRequestSource : IEntityCreationRequestSource
{
    private readonly IReadOnlyList<IEntityCreationRequestSource> _innerSources;

    /// <param name="innerSources">
    /// Ordered list of inner sources to drain. Must contain at least one source.
    /// </param>
    /// <exception cref="ArgumentNullException">If <paramref name="innerSources"/> is null.</exception>
    /// <exception cref="ArgumentException">If <paramref name="innerSources"/> is empty.</exception>
    public CompositeEntityCreationRequestSource(IReadOnlyList<IEntityCreationRequestSource> innerSources)
    {
        if (innerSources == null)
            throw new ArgumentNullException(nameof(innerSources));
        if (innerSources.Count == 0)
            throw new ArgumentException("innerSources must contain at least one source.", nameof(innerSources));
        _innerSources = innerSources;
    }

    /// <inheritdoc/>
    public void ProcessRequests(Action<EntityCreationRequest> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        foreach (var source in _innerSources)
        {
            source.ProcessRequests(handler);
        }
    }
}
