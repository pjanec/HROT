using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;

namespace Fdp.Toolkit.Squad.Primitives
{
    /// <summary>
    /// Per-member per-element score inputs for the partition primitive.
    /// Up to 4 element kinds (covering, bounding, overwatch, reserve).
    /// </summary>
    public struct MemberPartitionInput
    {
        private float _s0, _s1, _s2, _s3;

        public MemberPartitionInput(float s0, float s1 = 0f, float s2 = 0f, float s3 = 0f)
        {
            _s0 = s0; _s1 = s1; _s2 = s2; _s3 = s3;
        }

        /// <summary>Score for element index <paramref name="i"/> (0..3).</summary>
        public float this[int i] =>
            i == 0 ? _s0 :
            i == 1 ? _s1 :
            i == 2 ? _s2 : _s3;
    }

    /// <summary>
    /// Partitions squad members across N elements with hysteresis to prevent
    /// disruptive mid-maneuver reshuffling (design §4.1).
    /// </summary>
    public static class ElementPartitionPrimitive
    {
        /// <summary>
        /// Assigns each member to the highest-scoring element, subject to a
        /// decisive-gap hysteresis: a member stays in its current element unless
        /// the new winner's score exceeds the current element's score by at least
        /// <paramref name="decisiveGap"/>.
        /// </summary>
        /// <param name="state">Squad cognitive state to read/write.</param>
        /// <param name="inputs">Per-member element scores. Length must equal the squad roster size.</param>
        /// <param name="elementCount">Number of elements in use (2..4).</param>
        /// <param name="decisiveGap">
        ///   Minimum score advantage required to move a member to a new element
        ///   (anti-flip-flop; mirrors PostureSelect hysteresis in Utility §4.5).
        /// </param>
        /// <param name="repartitionsCount">
        ///   Number of members who actually changed element this call.
        /// </param>
        public static void Partition(
            ref SquadCognitiveState state,
            ReadOnlySpan<MemberPartitionInput> inputs,
            int elementCount,
            float decisiveGap,
            out int repartitionsCount)
        {
            repartitionsCount = 0;

            var membersSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(ref state.Elements.MemberElements), 16);

            for (int i = 0; i < inputs.Length; i++)
            {
                // Find the highest-scoring element.
                float bestScore = float.MinValue;
                int   newBest   = 0;
                for (int e = 0; e < elementCount; e++)
                {
                    float s = inputs[i][e];
                    if (s > bestScore) { bestScore = s; newBest = e; }
                }

                byte current = membersSpan[i];
                if (newBest != current)
                {
                    float gap = inputs[i][newBest] - inputs[i][current];
                    if (gap > decisiveGap)
                    {
                        membersSpan[i] = (byte)newBest;
                        repartitionsCount++;
                    }
                }
            }
        }
    }
}
