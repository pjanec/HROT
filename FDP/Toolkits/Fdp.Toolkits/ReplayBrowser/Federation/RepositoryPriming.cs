using System;
using System.Reflection;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// Shared app-domain and sandbox priming logic extracted from
    /// <c>ReplayBrowserContext</c>. Reflects all loaded assemblies and registers
    /// every <c>[ComponentId]</c>-annotated type on the repository and every
    /// <c>[EventId]</c>-annotated struct on the event bus.
    /// Factored out so multiple contexts (e.g. the federated set owned by
    /// <see cref="FederatedReplayManager"/>) can share the same priming path
    /// without duplicating the reflection logic.
    /// </summary>
    public static class RepositoryPriming
    {
        /// <summary>
        /// Reflects all loaded (non-system) assemblies and registers discovered
        /// component types on <paramref name="repo"/> and (optionally) event types
        /// on <paramref name="bus"/>.
        /// </summary>
        public static void RegisterDiscoveredComponents(EntityRepository repo, FdpEventBus? bus = null)
        {
            MethodInfo? registerMethod = null;
            foreach (var m in typeof(EntityRepository).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "RegisterComponent") continue;
                if (!m.IsGenericMethodDefinition) continue;
                if (m.GetParameters().Length == 1) { registerMethod = m; break; }
            }

            MethodInfo? ensureStreamMethod = bus == null ? null : typeof(FdpEventBus).GetMethod(
                nameof(FdpEventBus.PrepareForNativeEventReplay),
                BindingFlags.Public | BindingFlags.Instance);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                string? fullName = assembly.FullName;
                if (!string.IsNullOrEmpty(fullName) &&
                    (fullName.StartsWith("System", StringComparison.Ordinal) ||
                     fullName.StartsWith("Microsoft", StringComparison.Ordinal)))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = System.Array.FindAll(ex.Types, t => t != null)!;
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type.GetCustomAttributes(typeof(ComponentIdAttribute), false).Length > 0)
                    {
                        try
                        {
                            ComponentTypeRegistry.GetOrRegisterManaged(type);
                            registerMethod?.MakeGenericMethod(type).Invoke(repo, new object?[] { null });
                        }
                        catch
                        {
                        }
                    }

                    if (bus != null && type.IsValueType &&
                        type.GetCustomAttributes(typeof(EventIdAttribute), false).Length > 0)
                    {
                        try
                        {
                            ensureStreamMethod?.MakeGenericMethod(type).Invoke(bus, null);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }
    }
}
