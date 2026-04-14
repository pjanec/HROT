using System;
using CycloneDDS.Runtime;
using FDP.Toolkit.DER;
using FDP.Toolkit.Time.Messages;

namespace Hrot.ExCon.Services;

/// <summary>
/// DDS ingress handler that forwards SwitchTimeModeWireDto samples to ExConLogic.
/// </summary>
public sealed class TimeModeIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<SwitchTimeModeWireDto>  _reader;
    private readonly Action<SwitchTimeModeWireDto>     _onMode;

    public TimeModeIngressHandler(DdsParticipant participant, Action<SwitchTimeModeWireDto> onMode)
    {
        _reader  = new DdsReader<SwitchTimeModeWireDto>(participant);
        _onMode  = onMode ?? throw new ArgumentNullException(nameof(onMode));
    }

    public void Poll()
    {
        using var loan = _reader.Take();
        foreach (var s in loan)
        {
            if (!s.IsValid) continue;
            _onMode(s.Data);
        }
    }

    public void Dispose() => _reader.Dispose();
}