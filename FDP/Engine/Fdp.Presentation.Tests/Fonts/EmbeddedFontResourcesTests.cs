using Fdp.Presentation.Fonts;
using Xunit;

namespace Fdp.Presentation.Tests.Fonts;

/// <summary>
/// SC-FONT-1: Verifies that the embedded Roboto TTF is present and valid
/// without requiring a GPU/GL context.
/// </summary>
public class EmbeddedFontResourcesTests
{
    [Fact]
    public void GetRobotoRegularTtfBytes_ReturnsNonEmptyBytes()
    {
        byte[] ttf = EmbeddedFontResources.GetRobotoRegularTtfBytes();

        Assert.NotNull(ttf);
        Assert.True(ttf.Length > 0, "TTF byte array must not be empty.");
    }

    [Fact]
    public void GetRobotoRegularTtfBytes_HasValidTtfSignature()
    {
        byte[] ttf = EmbeddedFontResources.GetRobotoRegularTtfBytes();

        // A valid TTF/OTF file begins with one of these 4-byte signatures:
        //   00 01 00 00  — TrueType font
        //   4F 54 54 4F  — OpenType/CFF ("OTTO")
        //   74 72 75 65  — Mac TrueType ("true")
        //   74 74 63 66  — TrueType Collection ("ttcf")
        Assert.True(ttf.Length >= 4, "File is too short to contain a TTF signature.");

        bool isTrueType   = ttf[0] == 0x00 && ttf[1] == 0x01 && ttf[2] == 0x00 && ttf[3] == 0x00;
        bool isOpenTypeCff = ttf[0] == 0x4F && ttf[1] == 0x54 && ttf[2] == 0x54 && ttf[3] == 0x4F;
        bool isMacTrue    = ttf[0] == 0x74 && ttf[1] == 0x72 && ttf[2] == 0x75 && ttf[3] == 0x65;
        bool isTtcf       = ttf[0] == 0x74 && ttf[1] == 0x74 && ttf[2] == 0x63 && ttf[3] == 0x66;

        Assert.True(
            isTrueType || isOpenTypeCff || isMacTrue || isTtcf,
            $"Unexpected TTF signature: {ttf[0]:X2} {ttf[1]:X2} {ttf[2]:X2} {ttf[3]:X2}. " +
            "Expected 00 01 00 00 (TrueType), 4F 54 54 4F (OTTO), 74 72 75 65 (true), or 74 74 63 66 (ttcf).");
    }
}
