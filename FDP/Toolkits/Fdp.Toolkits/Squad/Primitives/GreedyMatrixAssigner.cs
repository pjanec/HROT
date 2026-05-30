using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Squad.Primitives
{
    /// <summary>
    /// Greedy O(m*n) assignment over a pre-built score matrix.
    /// Shared by <see cref="Fdp.Toolkit.Utility.ThreatMatrixAssignmentSystem"/> and
    /// <see cref="RoleSlotAssignmentPrimitive"/>.
    /// </summary>
    public static unsafe class GreedyMatrixAssigner
    {
        /// <summary>
        /// Greedily assigns each of the <paramref name="memberCount"/> rows to the
        /// highest-scoring column in <paramref name="scoreMatrix"/>, subject to
        /// <paramref name="maxFocusFire"/> concurrent assignments per column.
        /// </summary>
        /// <param name="scoreMatrix">
        ///   Flat row-major matrix of size <c>memberCount * candidateCount</c>.
        ///   <c>scoreMatrix[m * candidateCount + c]</c> is the score of member <c>m</c>
        ///   for candidate <c>c</c>.
        /// </param>
        /// <param name="memberCount">Number of rows (squad members). Max 16.</param>
        /// <param name="candidateCount">Number of columns (candidates). Max 16.</param>
        /// <param name="maxFocusFire">
        ///   Maximum number of members that may be assigned to the same candidate.
        /// </param>
        /// <param name="assignments">
        ///   Output span of length <paramref name="memberCount"/>. Each entry is the
        ///   winning candidate index (0-based), or -1 when no acceptable candidate was
        ///   found for that member.
        /// </param>
        public static void Assign(
            ReadOnlySpan<float> scoreMatrix,
            int memberCount,
            int candidateCount,
            int maxFocusFire,
            Span<int> assignments)
        {
            // Stack-allocated focus-fire counter per candidate (max 16).
            int* focusCount = stackalloc int[candidateCount];
            for (int c = 0; c < candidateCount; c++)
                focusCount[c] = 0;

            for (int m = 0; m < memberCount; m++)
            {
                float best = -1f;
                int bestC  = -1;
                int rowBase = m * candidateCount;
                for (int c = 0; c < candidateCount; c++)
                {
                    if (focusCount[c] >= maxFocusFire) continue;
                    float s = scoreMatrix[rowBase + c];
                    if (s > best) { best = s; bestC = c; }
                }
                assignments[m] = best > 0f ? bestC : -1;
                if (bestC >= 0 && best > 0f)
                    focusCount[bestC]++;
            }
        }
    }
}
