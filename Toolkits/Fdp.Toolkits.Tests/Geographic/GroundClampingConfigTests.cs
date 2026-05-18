using Fdp.Modules.Geographic.Components;
using Xunit;

namespace Fdp.Modules.Geographic.Tests
{
    /// <summary>
    /// Truth-table tests for <see cref="GroundClampingConfig.IsClampingActive"/>.
    /// Covers the three business rules:
    /// <list type="number">
    ///   <item>ForceOn → always active.</item>
    ///   <item>Default + grounded blueprint → active.</item>
    ///   <item>ForceOff → never active.</item>
    ///   <item>Default + non-grounded blueprint → not active.</item>
    /// </list>
    /// </summary>
    public class GroundClampingConfigTests
    {
        [Theory]
        [InlineData(EClampingMode.ForceOn,  1, true)]
        [InlineData(EClampingMode.ForceOn,  0, true)]  // ForceOn overrides blueprint
        [InlineData(EClampingMode.Auto,  1, true)]  // blueprint grounded
        [InlineData(EClampingMode.Auto,  0, false)] // blueprint non-grounded
        [InlineData(EClampingMode.ForceOff, 1, false)] // ForceOff overrides blueprint
        [InlineData(EClampingMode.ForceOff, 0, false)]
        public void IsClampingActive_MatchesTruthTable(
            EClampingMode mode,
            byte baseRequiresClamping,
            bool expectedActive)
        {
            var config = new GroundClampingConfig
            {
                Mode = mode,
                BaseRequiresClamping = baseRequiresClamping,
            };

            Assert.Equal(expectedActive, config.IsClampingActive);
        }
    }
}
