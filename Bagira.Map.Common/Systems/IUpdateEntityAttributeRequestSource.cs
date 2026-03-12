using System;
using Bagira.BDC.SSTM;

namespace Bagira.Map.Common.Systems;

/// <summary>
/// Abstraction over the DDS reader that delivers <see cref="UpdateEntityAttributeRequest"/>
/// messages. Allows the system to be unit-tested without a live DDS participant.
/// </summary>
public interface IUpdateEntityAttributeRequestSource
{
    /// <summary>
    /// Processes all currently available requests, invoking <paramref name="processor"/>
    /// synchronously for each valid sample.
    /// </summary>
    void ProcessRequests(Action<UpdateEntityAttributeRequest> processor);
}
