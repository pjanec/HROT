using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.Diagnostics.Overlays
{
    // Gating wrapper: tracks elapsed gizmo emit time and gates further emission.
    // Sheds lowest-priority flags first when the frame budget is exceeded.
    internal sealed class OverlayBudgetArbiter
    {
        // Priority order (shedding): Channels < SquadAssignment < Eqs < TargetMemory
        //                          < Perception < UtilityDecision (highest priority, shed last)
        private static readonly AiOverlayFlags[] ShedOrder = new[]
        {
            AiOverlayFlags.Channels,
            AiOverlayFlags.SquadAssignment,
            AiOverlayFlags.Eqs,
            AiOverlayFlags.TargetMemory,
            AiOverlayFlags.Perception,
            AiOverlayFlags.UtilityDecision,
        };

        private readonly float _budgetMs;
        private float _usedMs;
        private AiOverlayFlags _active; // flags still permitted this frame

        public OverlayBudgetArbiter(float budgetMs)
        {
            _budgetMs = budgetMs;
            _active   = (AiOverlayFlags)0xFFFF; // all enabled at frame start
        }

        // Call at the start of each frame to reset state.
        public void BeginFrame()
        {
            _usedMs = 0f;
            _active = (AiOverlayFlags)0xFFFF;
        }

        // Record that 'elapsedMs' milliseconds were spent emitting the given family.
        // Returns false if the family was shed due to budget exhaustion.
        public bool RecordAndCheck(AiOverlayFlags family, float elapsedMs)
        {
            _usedMs += elapsedMs;
            if (_usedMs <= _budgetMs)
                return true;

            // Over budget: shed the lowest-priority active family.
            foreach (var f in ShedOrder)
            {
                if ((_active & f) != 0)
                {
                    _active &= ~f;
                    break;
                }
            }

            return (_active & family) != 0;
        }

        // Returns true if the given overlay family is still permitted this frame.
        public bool IsPermitted(AiOverlayFlags family) => (_active & family) != 0;
    }
}
