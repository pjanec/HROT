using Fdp.Interfaces;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests
{
    /// <summary>
    /// Tests for TKB-010: TkbDescriptorRegistry.
    /// Each test clears the registry before and after to ensure isolation.
    /// </summary>
    [Collection("TkbDeserializerTests")]
    public class TkbDescriptorRegistryTests : System.IDisposable
    {
        public TkbDescriptorRegistryTests()
        {
            TkbDescriptorRegistry.Clear();
        }

        public void Dispose()
        {
            TkbDescriptorRegistry.Clear();
        }

        [Fact]
        public void RegisterParser_ThenTryGetParser_ReturnsTrueAndThunk()
        {
            TkbDescriptorParserThunk noOp = (_, _, _) => { };
            TkbDescriptorRegistry.RegisterParser("Test.Foo", noOp);

            bool found = TkbDescriptorRegistry.TryGetParser("Test.Foo".AsSpan(), out var thunk);

            Assert.True(found);
            Assert.Same(noOp, thunk);
        }

        [Fact]
        public void TryGetParser_UnregisteredName_ReturnsFalse()
        {
            bool found = TkbDescriptorRegistry.TryGetParser("NonExistent".AsSpan(), out _);

            Assert.False(found);
        }

        [Fact]
        public void RegisterParser_CaseInsensitive_FoundWithDifferentCase()
        {
            TkbDescriptorParserThunk noOp = (_, _, _) => { };
            TkbDescriptorRegistry.RegisterParser("gen.vehicleparameters", noOp);

            bool found = TkbDescriptorRegistry.TryGetParser("Gen.VehicleParameters".AsSpan(), out _);

            Assert.True(found);
        }

        [Fact]
        public void RegisterParser_Overwrite_ReturnsLatestThunk()
        {
            TkbDescriptorParserThunk first = (_, _, _) => { };
            TkbDescriptorParserThunk second = (_, _, _) => { };
            TkbDescriptorRegistry.RegisterParser("Test.Bar", first);
            TkbDescriptorRegistry.RegisterParser("Test.Bar", second);

            TkbDescriptorRegistry.TryGetParser("Test.Bar".AsSpan(), out var thunk);

            Assert.Same(second, thunk);
        }
    }
}
