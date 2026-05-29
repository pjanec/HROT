namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Aggregates per-consideration curve outputs into a single option score.
    /// Supports two modes: product-with-compensation (Dave Mark §4.3) and normalised weighted sum (§4.4).
    /// </summary>
    public static class Aggregator
    {
        /// <summary>
        /// Aggregate curve outputs and weights into a single score using the specified mode.
        /// </summary>
        /// <param name="curveOutputs">Normalised curve output values in [0,1]. Length == n.</param>
        /// <param name="weights">Consideration weights. Parallel to <paramref name="curveOutputs"/>.</param>
        /// <param name="mode">Scoring mode.</param>
        /// <returns>Final score in [0,1].</returns>
        public static float Aggregate(ReadOnlySpan<float> curveOutputs, ReadOnlySpan<float> weights, ScoringMode mode)
        {
            if (curveOutputs.IsEmpty) return 0f;

            return mode == ScoringMode.WeightedSum
                ? AggregateSum(curveOutputs, weights)
                : AggregateProduct(curveOutputs, weights);
        }

        private static float AggregateProduct(ReadOnlySpan<float> curveOutputs, ReadOnlySpan<float> weights)
        {
            int n = curveOutputs.Length;
            float rawProduct = 1f;
            for (int i = 0; i < n; i++)
            {
                // Weight is the exponent (§5.4): curve^weight
                float w = i < weights.Length ? weights[i] : 1f;
                rawProduct *= MathF.Pow(curveOutputs[i], w);
            }

            // Dave Mark's compensation factor (§4.3):
            //   modificationFactor = 1 - (1 / n)
            //   makeUpValue        = (1 - rawProduct) * modificationFactor
            //   finalScore         = rawProduct + makeUpValue * rawProduct
            float modificationFactor = 1f - (1f / n);
            float makeUpValue        = (1f - rawProduct) * modificationFactor;
            return rawProduct + makeUpValue * rawProduct;
        }

        private static float AggregateSum(ReadOnlySpan<float> curveOutputs, ReadOnlySpan<float> weights)
        {
            float numerator = 0f, denominator = 0f;
            for (int i = 0; i < curveOutputs.Length; i++)
            {
                float w = i < weights.Length ? weights[i] : 1f;
                numerator   += w * curveOutputs[i];
                denominator += w;
            }
            return denominator > 0f ? numerator / denominator : 0f;
        }
    }
}
