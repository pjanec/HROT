using Fdp.Presentation.Panels.ReplayBrowser;
using Xunit;

namespace Fdp.Presentation.ReplayBrowser.Federation;

/// <summary>
/// RBF-P4T5: Documentation/text-predicate tests for FederationPanel disclaimer string.
/// </summary>
public sealed class RBF_P4T5_DocumentationTests
{
    [Fact]
    public void RBF_P4T5_FederationPanel_DisclaimerTextContainsStutter()
    {
        Assert.Contains("stutter", FederationPanel.MergedViewDisclaimerText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RBF_P4T5_FederationPanel_DisclaimerTextContainsOffline()
    {
        Assert.Contains("offline", FederationPanel.MergedViewDisclaimerText, StringComparison.OrdinalIgnoreCase);
    }
}
