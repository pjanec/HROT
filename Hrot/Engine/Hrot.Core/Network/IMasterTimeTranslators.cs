namespace Hrot.Core.Network;

/// <summary>
/// Groups the three master-side time-sync translators behind a single per-frame call surface.
/// </summary>
public interface IMasterTimeTranslators : IDisposable
{
    /// <summary>Read managed write-buffer -> DDS egress (time-mode + lockstep).</summary>
    void ScanAndPublish();
    /// <summary>DDS ingress -> write buffer (time-mode + lockstep).</summary>
    void PollIngress();
    /// <summary>Late NTP ingress poll (Phase 5, after SwapBuffers).</summary>
    void PollNtpIngress();
}
