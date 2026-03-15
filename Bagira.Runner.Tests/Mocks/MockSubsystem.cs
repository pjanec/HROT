using System.Numerics;

namespace Bagira.Runner.Tests.Mocks
{
    /// <summary>
    /// Test double for <see cref="ISubsystem"/>.
    /// Tracks method call counts and order so tests can assert lifecycle behaviour.
    /// </summary>
    public class MockSubsystem : ISubsystem
    {
        public string Name { get; }

        /// <inheritdoc/>
        public Vector4 TitleBarColor => new Vector4(0.5f, 0.5f, 0.5f, 1f);

        public bool InitializeCalled { get; private set; }
        public bool ShutdownCalled  { get; private set; }
        public int  UpdateCallCount { get; private set; }
        public int  DrawWorldCount  { get; private set; }
        public int  DrawUICount     { get; private set; }

        /// <summary>Sequence number assigned by the shared counter when Shutdown is called.</summary>
        public int ShutdownOrder { get; private set; } = -1;

        /// <summary>Config received during Initialize.</summary>
        public SubsystemConfig? ReceivedConfig { get; private set; }

        private readonly Action<MockSubsystem>? _onShutdown;

        public MockSubsystem(string name = "MockSubsystem",
                             Action<MockSubsystem>? onShutdown = null)
        {
            Name       = name;
            _onShutdown = onShutdown;
        }

        public void Initialize(SubsystemConfig config)
        {
            InitializeCalled = true;
            ReceivedConfig   = config;
        }

        public void Update(float deltaTime) => UpdateCallCount++;
        public void DrawWorld()             => DrawWorldCount++;
        public void DrawUI()                => DrawUICount++;

        public void Shutdown()
        {
            ShutdownCalled = true;
            _onShutdown?.Invoke(this);
        }
    }
}
