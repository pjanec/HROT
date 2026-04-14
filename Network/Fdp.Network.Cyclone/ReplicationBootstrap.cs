using System;
using System.Collections.Generic;
using System.Reflection;
using Fdp.Interfaces;
using Fdp.Kernel.Logging;
using Fdp.Interfaces.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
// using FDP.Toolkit.Replication.Translators;
using Fdp.Toolkit.Replication.Utilities;
using CycloneDDS.Runtime;
using Fdp.Network.Cyclone.Translators; // Added for ManagedAutoCycloneTranslator
using Fdp.Network.Cyclone.Providers; // Added for ManagedSerializationProvider

namespace Fdp.Network.Cyclone
{
    public static class ReplicationBootstrap
    {
        public static (List<IDescriptorTranslator> Translators, List<(long Ordinal, ISerializationProvider Provider)> Providers) CreateAutoTranslators(
            DdsParticipant participant,
            Assembly assembly, 
            NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem)
        {
            var translators = new List<IDescriptorTranslator>();
            var providers = new List<(long, ISerializationProvider)>();
            
            foreach (var type in assembly.GetTypes()) {
                // Check attribute
                var attr = type.GetCustomAttribute<FdpDescriptorAttribute>();
                if (attr == null) continue;

                if (type.IsValueType)
                {
                    // UNMANAGED / STRUCT PATH
                    var unsafeLayoutType = typeof(UnsafeLayout<>).MakeGenericType(type);
                    var isValidField = unsafeLayoutType.GetField("IsValid", BindingFlags.Public | BindingFlags.Static);
                    bool isValid = (bool)isValidField!.GetValue(null)!;

                    if (isValid)
                    {
                        var translatorType = typeof(AutoCycloneTranslator<>)
                            .MakeGenericType(type);
                        
                        var translator = (IDescriptorTranslator)Activator.CreateInstance(
                            translatorType,
                            participant,
                            attr.TopicName,
                            attr.Ordinal,
                            entityMap,
                            ghostCreationSystem
                        )!;
                        
                        translators.Add(translator);
                        
                        FdpLog<NetworkEntityMap>.Info(
                            $"[Replication] Auto-registered (Struct): {type.Name} " +
                            $"(Topic: {attr.TopicName}, ID: {attr.Ordinal})"
                        );
                    }
                    else
                    {
                        FdpLog<NetworkEntityMap>.Warn(
                            $"[Replication] SKIPPED auto-registration for Struct {type.Name}. " +
                            $"Type lacks 'long EntityId' field required for auto-translation."
                        );
                    }
                }
                else  // Reference Type (Class)
                {
                    // MANAGED PATH (New)
                    var accessorType = typeof(ManagedAccessor<>).MakeGenericType(type);
                    
                    // Trigger static ctor
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(accessorType.TypeHandle);
                    
                    var isValidField = accessorType.GetField("IsValid", BindingFlags.Public | BindingFlags.Static);
                    bool isValid = (bool)isValidField!.GetValue(null)!;

                    if (isValid)
                    {
                        var translatorType = typeof(ManagedAutoCycloneTranslator<>)
                            .MakeGenericType(type);
                        
                        var translator = (IDescriptorTranslator)Activator.CreateInstance(
                            translatorType,
                            participant,
                            attr.TopicName,
                            attr.Ordinal,
                            entityMap,
                            ghostCreationSystem
                        )!;

                        translators.Add(translator);

                        // Register Serialization Provider for Managed Type
                        var providerType = typeof(ManagedSerializationProvider<>).MakeGenericType(type);
                        var provider = (ISerializationProvider)Activator.CreateInstance(providerType)!;
                        providers.Add((attr.Ordinal, provider));

                        FdpLog<NetworkEntityMap>.Info(
                            $"[Replication] Auto-registered (Managed): {type.Name} " +
                            $"(Topic: {attr.TopicName}, ID: {attr.Ordinal}, Provider: {providerType.Name})"
                        );
                    }
                    else
                    {
                        FdpLog<NetworkEntityMap>.Warn(
                           $"[Replication] SKIPPED auto-registration for Managed {type.Name}. " +
                           $"Type lacks 'EntityId' property/field."
                       );
                    }
                }
            }
            
            return (translators, providers);
        }

        public static List<(long Ordinal, ISerializationProvider Provider)> DiscoverProviders(Assembly assembly)
        {
            var providers = new List<(long, ISerializationProvider)>();
            
             foreach (var type in assembly.GetTypes()) {
                var attr = type.GetCustomAttribute<FdpDescriptorAttribute>();
                if (attr == null) continue;

                if (!type.IsValueType)
                {
                    // MANAGED PATH check
                    var accessorType = typeof(ManagedAccessor<>).MakeGenericType(type);
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(accessorType.TypeHandle);
                    var isValidField = accessorType.GetField("IsValid", BindingFlags.Public | BindingFlags.Static);
                    bool isValid = (bool)isValidField!.GetValue(null)!;

                    if (isValid)
                    {
                        var providerType = typeof(ManagedSerializationProvider<>).MakeGenericType(type);
                        var provider = (ISerializationProvider)Activator.CreateInstance(providerType)!;
                        providers.Add((attr.Ordinal, provider));
                    }
                }
             }
             return providers;
        }
    }
}
