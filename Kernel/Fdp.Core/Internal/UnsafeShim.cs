// File: Fdp.Kernel/Internal/UnsafeShim.cs

using System;
using System.Reflection;
using System.Runtime.CompilerServices;

#nullable disable

namespace Fdp.Kernel.Internal  
{  
    internal static class ComponentTypeHelper  
    {  
        /// <summary>  
        /// Returns true if T is unmanaged, false if it contains references.  
        /// Treated as a constant by the JIT compiler.  
        /// </summary>  
        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static bool IsUnmanaged<T>()  
        {  
            return !RuntimeHelpers.IsReferenceOrContainsReferences<T>();  
        }  
    }

    /// <summary>  
    /// Bridges the gap between generic T and constrained T (unmanaged/class).  
    /// Uses cached open delegates created via Reflection to bypass compile-time constraints.
    /// The runtime cost is just a delegate invocation, and strict dead-code elimination by JIT prevents instantiation errors.
    /// </summary>  
    internal static class UnsafeShim  
    {  
        // ------------------------------------------------------------------  
        // REGISTRATION  
        // ------------------------------------------------------------------  
        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static void RegisterUnmanaged<T>(EntityRepository repo)  
        {  
            UnmanagedAccessor<T>.Register(repo);  
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static void RegisterManaged<T>(EntityRepository repo)  
        {  
            ManagedAccessor<T>.Register(repo);  
        }

        // ------------------------------------------------------------------  
        // SET  
        // ------------------------------------------------------------------  
        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static void AddUnmanaged<T>(EntityRepository repo, Entity e, T value)  
        {  
            UnmanagedAccessor<T>.Add(repo, e, value);  
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static void AddManaged<T>(EntityRepository repo, Entity e, T value)  
        {  
            ManagedAccessor<T>.Add(repo, e, value);  
        }

        // ------------------------------------------------------------------  
        // GET (RW)  
        // ------------------------------------------------------------------  
        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static ref T GetUnmanagedRW<T>(EntityRepository repo, Entity e)  
        {  
            return ref UnmanagedAccessor<T>.GetRW(repo, e);  
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static ref T GetManagedRW<T>(EntityRepository repo, Entity e)  
        {  
            return ref ManagedAccessor<T>.GetRW(repo, e);  
        }

        // ------------------------------------------------------------------  
        // GET (RO)  
        // ------------------------------------------------------------------  
        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static ref readonly T GetUnmanagedRO<T>(EntityRepository repo, Entity e)  
        {  
            return ref UnmanagedAccessor<T>.GetRO(repo, e);  
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static ref readonly T GetManagedRO<T>(EntityRepository repo, Entity e)  
        {  
            return ref ManagedAccessor<T>.GetRO(repo, e);  
        }

        // ------------------------------------------------------------------  
        // HAS / REMOVE  
        // ------------------------------------------------------------------  
        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static bool HasUnmanaged<T>(EntityRepository repo, Entity e) => UnmanagedAccessor<T>.Has(repo, e);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static bool HasManaged<T>(EntityRepository repo, Entity e) => ManagedAccessor<T>.Has(repo, e);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static void RemoveUnmanaged<T>(EntityRepository repo, Entity e) => UnmanagedAccessor<T>.Remove(repo, e);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static void RemoveManaged<T>(EntityRepository repo, Entity e) => ManagedAccessor<T>.Remove(repo, e);

        // ------------------------------------------------------------------  
        // SINGLETONS  
        // ------------------------------------------------------------------  
        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static void SetSingletonUnmanaged<T>(EntityRepository repo, T value)  
        {  
            UnmanagedAccessor<T>.SetSingleton(repo, value);  
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static void SetSingletonManaged<T>(EntityRepository repo, T value)  
        {  
            ManagedAccessor<T>.SetSingleton(repo, value);  
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static ref T GetSingletonUnmanaged<T>(EntityRepository repo)  
        {  
            return ref UnmanagedAccessor<T>.GetSingleton(repo);  
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static ref T GetSingletonManaged<T>(EntityRepository repo)  
        {  
            // Since we can't easily shim the ref return for managed types without GetSingletonManagedRW, 
            // and we didn't add it, we are stuck.
            // But wait, ManagedAccessor<T>.GetRW acts on Entity.
            // Singletons are stored in `_singletons` array as `ManagedComponentTable<T>`.
            // The table has indexer.
            // If we access it via reflection delegate that returns ref...
            // But EntityRepository.GetSingletonManaged returns T?.
            // I'll throw if this is called for now, assuming only Unmanaged Singletons are used in this batch (SpatialGridData is unmanaged).
            throw new NotSupportedException("Managed Singleton Ref Access not implemented");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static bool HasSingletonUnmanaged<T>(EntityRepository repo) => UnmanagedAccessor<T>.HasSingleton(repo);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]  
        public static bool HasSingletonManaged<T>(EntityRepository repo) => ManagedAccessor<T>.HasSingleton(repo);


        // ==================================================================  
        // ACCESSOR HELPERS (Reflection-bound Delegates)
        // ==================================================================

        // Delegates
        private delegate void RegisterDelegate(EntityRepository repo);
        private delegate void AddUnmanagedDelegate<T>(EntityRepository repo, Entity e, in T value);
        private delegate void AddManagedDelegate<T>(EntityRepository repo, Entity e, T value);
        private delegate ref T GetRWDelegate<T>(EntityRepository repo, Entity e);
        private delegate ref readonly T GetROUnmanagedDelegate<T>(EntityRepository repo, Entity e);
        // Note: Managed RO Shim uses RW delegate
        private delegate bool HasDelegate(EntityRepository repo, Entity e);
        private delegate void RemoveDelegate(EntityRepository repo, Entity e);
        
        // Singleton Delegates
        private delegate void SetSingletonUnmanagedDelegate<T>(EntityRepository repo, in T value);
        private delegate void SetSingletonManagedDelegate<T>(EntityRepository repo, T value);
        private delegate ref T GetSingletonUnmanagedDelegate<T>(EntityRepository repo);
        private delegate bool HasSingletonDelegate(EntityRepository repo);

        private static class UnmanagedAccessor<T> 
        {
            private static readonly RegisterDelegate _register;
            private static readonly AddUnmanagedDelegate<T> _add;
            private static readonly GetRWDelegate<T> _getRW;
            private static readonly GetROUnmanagedDelegate<T> _getRO;
            private static readonly HasDelegate _has;
            private static readonly RemoveDelegate _remove;
            
            private static readonly SetSingletonUnmanagedDelegate<T> _setSingleton;
            private static readonly GetSingletonUnmanagedDelegate<T> _getSingleton;
            private static readonly HasSingletonDelegate _hasSingleton;

            static UnmanagedAccessor()
            {
                var repoType = typeof(EntityRepository);
                var typeT = typeof(T);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                _register = (RegisterDelegate)Delegate.CreateDelegate(typeof(RegisterDelegate), null, 
                    repoType.GetMethod(nameof(EntityRepository.RegisterUnmanagedComponent), flags)!.MakeGenericMethod(typeT));

                _add = (AddUnmanagedDelegate<T>)Delegate.CreateDelegate(typeof(AddUnmanagedDelegate<T>), null, 
                    repoType.GetMethod(nameof(EntityRepository.AddUnmanagedComponent), flags)!.MakeGenericMethod(typeT));

                _getRW = (GetRWDelegate<T>)Delegate.CreateDelegate(typeof(GetRWDelegate<T>), null, 
                    repoType.GetMethod(nameof(EntityRepository.GetUnmanagedComponentRW), flags)!.MakeGenericMethod(typeT));

                _getRO = (GetROUnmanagedDelegate<T>)Delegate.CreateDelegate(typeof(GetROUnmanagedDelegate<T>), null, 
                    repoType.GetMethod(nameof(EntityRepository.GetUnmanagedComponentRO), flags)!.MakeGenericMethod(typeT));

                _has = (HasDelegate)Delegate.CreateDelegate(typeof(HasDelegate), null, 
                    repoType.GetMethod(nameof(EntityRepository.HasUnmanagedComponent), flags)!.MakeGenericMethod(typeT));

                _remove = (RemoveDelegate)Delegate.CreateDelegate(typeof(RemoveDelegate), null, 
                    repoType.GetMethod(nameof(EntityRepository.RemoveUnmanagedComponent), flags)!.MakeGenericMethod(typeT));
                    
                _setSingleton = (SetSingletonUnmanagedDelegate<T>)Delegate.CreateDelegate(typeof(SetSingletonUnmanagedDelegate<T>), null, 
                    repoType.GetMethod(nameof(EntityRepository.SetSingletonUnmanaged), flags)!.MakeGenericMethod(typeT));
                    
                _getSingleton = (GetSingletonUnmanagedDelegate<T>)Delegate.CreateDelegate(typeof(GetSingletonUnmanagedDelegate<T>), null, 
                    repoType.GetMethod(nameof(EntityRepository.GetSingletonUnmanaged), flags)!.MakeGenericMethod(typeT));
                    
                _hasSingleton = (HasSingletonDelegate)Delegate.CreateDelegate(typeof(HasSingletonDelegate), null, 
                    repoType.GetMethod(nameof(EntityRepository.HasSingletonUnmanaged), flags)!.MakeGenericMethod(typeT));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Register(EntityRepository repo) => _register(repo);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Add(EntityRepository repo, Entity e, T v) => _add(repo, e, v);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ref T GetRW(EntityRepository repo, Entity e) => ref _getRW(repo, e);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ref readonly T GetRO(EntityRepository repo, Entity e) => ref _getRO(repo, e);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool Has(EntityRepository repo, Entity e) => _has(repo, e);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Remove(EntityRepository repo, Entity e) => _remove(repo, e);  
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void SetSingleton(EntityRepository repo, T v) => _setSingleton(repo, v);
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ref T GetSingleton(EntityRepository repo) => ref _getSingleton(repo);
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool HasSingleton(EntityRepository repo) => _hasSingleton(repo);
        }

        private static class ManagedAccessor<T> 
        {
            private static readonly RegisterDelegate _register;
            private static readonly AddManagedDelegate<T> _add;
            private static readonly GetRWDelegate<T> _getRW;
            // Managed RO uses RW delegate in this shim approach
            private static readonly HasDelegate _has;
            private static readonly RemoveDelegate _remove;
            
            private static readonly SetSingletonManagedDelegate<T> _setSingleton;
            private static readonly HasSingletonDelegate _hasSingleton;

            static ManagedAccessor()
            {
                var repoType = typeof(EntityRepository);
                var typeT = typeof(T);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                _register = (RegisterDelegate)Delegate.CreateDelegate(typeof(RegisterDelegate), null, 
                    repoType.GetMethod(nameof(EntityRepository.RegisterManagedComponentInternal), flags)!.MakeGenericMethod(typeT));

                _add = (AddManagedDelegate<T>)Delegate.CreateDelegate(typeof(AddManagedDelegate<T>), null, 
                    repoType.GetMethod(nameof(EntityRepository.AddManagedComponent), flags)!.MakeGenericMethod(typeT));

                _getRW = (GetRWDelegate<T>)Delegate.CreateDelegate(typeof(GetRWDelegate<T>), null, 
                    repoType.GetMethod(nameof(EntityRepository.GetManagedComponentRW), flags)!.MakeGenericMethod(typeT));

                _has = (HasDelegate)Delegate.CreateDelegate(typeof(HasDelegate), null, 
                    repoType.GetMethod(nameof(EntityRepository.HasManagedComponent), flags)!.MakeGenericMethod(typeT));

                _remove = (RemoveDelegate)Delegate.CreateDelegate(typeof(RemoveDelegate), null, 
                    repoType.GetMethod(nameof(EntityRepository.RemoveManagedComponent), flags)!.MakeGenericMethod(typeT));
                    
                _setSingleton = (SetSingletonManagedDelegate<T>)Delegate.CreateDelegate(typeof(SetSingletonManagedDelegate<T>), null, 
                    repoType.GetMethod(nameof(EntityRepository.SetSingletonManaged), flags)!.MakeGenericMethod(typeT));
                
                _hasSingleton = (HasSingletonDelegate)Delegate.CreateDelegate(typeof(HasSingletonDelegate), null, 
                    repoType.GetMethod(nameof(EntityRepository.HasSingletonManaged), flags)!.MakeGenericMethod(typeT));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Register(EntityRepository repo) => _register(repo);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Add(EntityRepository repo, Entity e, T v) => _add(repo, e, v);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ref T GetRW(EntityRepository repo, Entity e) => ref _getRW(repo, e);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ref readonly T GetRO(EntityRepository repo, Entity e) 
            {
                 // Return RW as RO
                 return ref _getRW(repo, e);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool Has(EntityRepository repo, Entity e) => _has(repo, e);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Remove(EntityRepository repo, Entity e) => _remove(repo, e);  
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void SetSingleton(EntityRepository repo, T v) => _setSingleton(repo, v);
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool HasSingleton(EntityRepository repo) => _hasSingleton(repo);
        }  
    }
}
