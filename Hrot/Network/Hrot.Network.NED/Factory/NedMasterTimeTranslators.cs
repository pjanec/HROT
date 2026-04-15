using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Translators;
using Hrot.Core.Network;

namespace Hrot.Network.NED.Factory;

/// <summary>
/// Groups the three master-side time-sync translators behind a single
/// <see cref="IMasterTimeTranslators"/> call surface.
/// Wraps <c>SwitchTimeModeDescriptorTranslator</c>, <see cref="MasterLockstepTranslator"/>,
/// and <c>MasterTimeSyncTranslator</c>.
/// Created and owned by <see cref="NedNetworkFactory.CreateMasterTimeTranslators"/>.
/// </summary>
internal sealed class NedMasterTimeTranslators : IMasterTimeTranslators
{
    private readonly IDescriptorTranslator       _timeModeTranslator;
    private readonly MasterLockstepTranslator    _lockstepTranslator;
    private readonly IDescriptorTranslator       _ntpTranslator;
    private bool _disposed;

    public NedMasterTimeTranslators(DdsParticipant? participant, FdpEventBus bus)
    {
        if (bus == null) throw new ArgumentNullException(nameof(bus));
        _timeModeTranslator = TimeNetworkModule.CreateDescriptorTranslator(participant, bus);
        _lockstepTranslator = TimeNetworkModule.CreateMasterLockstepTranslator(participant, bus);
        _ntpTranslator      = TimeNetworkModule.CreateMasterTimeSyncTranslator(participant);
    }

    /// <inheritdoc/>
    public void ScanAndPublish()
    {
        _timeModeTranslator.ScanAndPublish(null!);
        _lockstepTranslator.ScanAndPublish(null!);
    }

    /// <inheritdoc/>
    public void PollIngress()
    {
        _timeModeTranslator.PollIngress(null!, null!);
        _lockstepTranslator.PollIngress(null!, null!);
    }

    /// <inheritdoc/>
    public void PollNtpIngress()
    {
        _ntpTranslator.PollIngress(null!, null!);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (_timeModeTranslator as IDisposable)?.Dispose();
        // MasterLockstepTranslator does not implement IDisposable; no disposal needed.
        (_ntpTranslator as IDisposable)?.Dispose();
    }
}
