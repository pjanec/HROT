using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost
{
    /// <summary>
    /// Implemented by <see cref="ComponentSystem"/> adapters that wrap an
    /// <see cref="IEcsModuleSystem"/>. Allows callers to recover the original system
    /// instance from a <see cref="SystemGroup.GetSystems"/> result.
    /// </summary>
    public interface IEcsModuleSystemWrapper
    {
        /// <summary>The original <see cref="IEcsModuleSystem"/> wrapped by this adapter.</summary>
        IEcsModuleSystem WrappedSystem { get; }
    }

    /// <summary>
    /// Extension methods for <see cref="SystemGroup"/> that allow adding
    /// <see cref="IEcsModuleSystem"/> instances alongside legacy
    /// <see cref="ComponentSystem"/> instances.
    /// </summary>
    public static class SystemGroupExtensions
    {
        // Wraps an IEcsModuleSystem so it can be managed by SystemGroup.
        private sealed class EcsModuleSystemAdapter : ComponentSystem, IEcsModuleSystemWrapper
        {
            private readonly IEcsModuleSystem _inner;
            public IEcsModuleSystem WrappedSystem => _inner;
            internal EcsModuleSystemAdapter(IEcsModuleSystem inner) => _inner = inner;
            protected override void OnUpdate() => _inner.Execute(World, DeltaTime);
        }

        /// <summary>
        /// Adds an <see cref="IEcsModuleSystem"/> to the group by wrapping it in an adapter.
        /// The adapter forwards each tick to <see cref="IEcsModuleSystem.Execute"/> using
        /// the group's <c>DeltaTime</c> (read from the <see cref="GlobalTime"/> singleton).
        /// </summary>
        public static void AddSystem(this SystemGroup group, IEcsModuleSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            group.AddSystem(new EcsModuleSystemAdapter(system));
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="system"/> is of type
        /// <typeparamref name="T"/>, or if it is an <see cref="IEcsModuleSystemWrapper"/>
        /// whose <see cref="IEcsModuleSystemWrapper.WrappedSystem"/> is of type
        /// <typeparamref name="T"/>. Use this in test assertions instead of
        /// <c>s is T</c> when checking systems retrieved from a <see cref="SystemGroup"/>.
        /// </summary>
        public static bool IsOrWraps<T>(this ComponentSystem system) where T : class
            => system is T || (system is IEcsModuleSystemWrapper w && w.WrappedSystem is T);
    }
}
