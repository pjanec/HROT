using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.UI.Common.Audio
{
    /// <summary>
    /// ⭐ <c>CE-1305</c> TE-11 (docs/DESIGN_Director_Timeline_Editor.md §0 TE-11, §11v A1) — sound at any playback rate, ONE rule for the live mixer
    /// (<see cref="SoundMixer.RateGain"/>) and the film's offline track (<see cref="SoundTrack.RenderWav"/>): a sound is NEVER resampled — it starts at the
    /// film time of its event and plays at its natural pitch and length; up to <see cref="OnlyBlastsAbove"/>× everything sounds; faster than that only
    /// explosions, <see cref="Ducked"/>; paused (a rate of 0, a held frame, a title card) is silent. Rejected (TE-11): resampling the mix (a chipmunk or a
    /// drone) · silence at every rate but 1×.
    /// </summary>
    public static class SoundAtRate
    {
        /// <summary>Above this many sim seconds per film second only explosions sound.</summary>
        public const double OnlyBlastsAbove = 2d;

        /// <summary>An explosion's gain above <see cref="OnlyBlastsAbove"/>× (−6 dB).</summary>
        public const float Ducked = 0.5f;

        /// <summary>The gain factor of a sound of <paramref name="category"/> (null: unknown — not an explosion) at <paramref name="rate"/> sim seconds per
        /// film second: 0 when paused (≤ 0, NaN), 1 up to 2×, above it <see cref="Ducked"/> for an explosion and 0 for anything else.</summary>
        public static float Gain(SoundCategory? category, double rate)
            => !(rate > 1e-9) ? 0f
               : rate <= OnlyBlastsAbove + 1e-9 ? 1f
               : category == SoundCategory.Explosions ? Ducked : 0f;
    }
}
