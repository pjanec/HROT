using Xunit;
using Bagira.Runner.Tests.Mocks;

namespace Bagira.Runner.Tests
{
    /// <summary>
    /// Tests for the <see cref="ISubsystem"/> interface contract
    /// and the <see cref="MockSubsystem"/> test double.
    /// </summary>
    public class ISubsystemTests
    {
        [Fact]
        public void MockSubsystem_ImplementsISubsystem()
        {
            ISubsystem subsystem = new MockSubsystem("Test");
            Assert.NotNull(subsystem);
            Assert.Equal("Test", subsystem.Name);
        }

        [Fact]
        public void MockSubsystem_TracksCalls_Correctly()
        {
            var mock = new MockSubsystem("Track");

            mock.Initialize(new SubsystemConfig { SubsystemName = "Track", Headless = true });
            mock.Update(0.016f);
            mock.Update(0.016f);
            mock.DrawWorld();
            mock.DrawUI();
            mock.Shutdown();

            Assert.True(mock.InitializeCalled);
            Assert.Equal(2, mock.UpdateCallCount);
            Assert.Equal(1, mock.DrawWorldCount);
            Assert.Equal(1, mock.DrawUICount);
            Assert.True(mock.ShutdownCalled);
        }
    }
}
