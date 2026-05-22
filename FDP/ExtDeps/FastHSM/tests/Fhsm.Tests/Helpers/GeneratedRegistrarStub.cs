// Hand-written stand-in for the missing Fhsm.SourceGen output.
// Replaces the runtime Roslyn source generator (Fhsm.SourceGen.csproj) which is not
// present in this repository. RegisterAll() registers every [HsmAction] / [HsmGuard]
// method in this test assembly so that integration tests work correctly.
//
// When the real source generator is added back, delete this file.

using System;
using System.Collections.Generic;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Generated
{
    public static unsafe class HsmActionRegistrar
    {
        // No [WritesChannel]-annotated methods exist in this assembly.
        public static readonly IReadOnlyDictionary<string, string> RequiredExitCleanups =
            new Dictionary<string, string>();

        private static ushort Hash(string name)
        {
            uint h = 2166136261u;
            foreach (char c in name) { h ^= c; h *= 16777619u; }
            return (ushort)(h & 0xFFFFu);
        }

        public static void RegisterAll()
        {
            // ---- Actions from Fhsm.Tests.Examples.IntegrationTests ----
            HsmActionDispatcher.RegisterAction(Hash("TestEntry"),
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)
                    &Fhsm.Tests.Examples.IntegrationTests.TestEntry);

            HsmActionDispatcher.RegisterAction(Hash("TestExit"),
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)
                    &Fhsm.Tests.Examples.IntegrationTests.TestExit);

            HsmActionDispatcher.RegisterAction(Hash("TestActivity"),
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)
                    &Fhsm.Tests.Examples.IntegrationTests.TestActivity);

            HsmActionDispatcher.RegisterAction(Hash("TestTransition"),
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)
                    &Fhsm.Tests.Examples.IntegrationTests.TestTransition);

            // ---- Actions from Fhsm.Tests.SourceGen.ActionDispatchTests ----
            HsmActionDispatcher.RegisterAction(Hash("TestAction"),
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)
                    &Fhsm.Tests.SourceGen.ActionDispatchTests.TestAction);

            HsmActionDispatcher.RegisterAction(Hash("ImplicitNameAction"),
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)
                    &Fhsm.Tests.SourceGen.ActionDispatchTests.ImplicitNameAction);

            // ---- Guards from Fhsm.Tests.SourceGen.ActionDispatchTests ----
            HsmActionDispatcher.RegisterGuard(Hash("TestGuard"),
                (IntPtr)(delegate* <void*, void*, ushort, bool>)
                    &Fhsm.Tests.SourceGen.ActionDispatchTests.TestGuard);

            // ---- Actions from Fhsm.Tests.Kernel.CommandBufferIntegrationTests ----
            HsmActionDispatcher.RegisterAction(Hash("WriteTestCommand"),
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)
                    &Fhsm.Tests.Kernel.CommandBufferIntegrationTests.WriteTestCommand);

            HsmActionDispatcher.RegisterAction(Hash("WriteAA"),
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)
                    &Fhsm.Tests.Kernel.CommandBufferIntegrationTests.WriteAA);

            HsmActionDispatcher.RegisterAction(Hash("WriteBB"),
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)
                    &Fhsm.Tests.Kernel.CommandBufferIntegrationTests.WriteBB);

            HsmActionDispatcher.RegisterAction(Hash("WriteCC"),
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)
                    &Fhsm.Tests.Kernel.CommandBufferIntegrationTests.WriteCC);

            // ---- Guards from Fhsm.Tests.Integration.HsmSourceGenIntegrationTests ----
            HsmActionDispatcher.RegisterGuard(Hash("IntegrationTestGuard"),
                (IntPtr)(delegate* <void*, void*, ushort, bool>)
                    &Fhsm.Tests.Integration.HsmSourceGenIntegrationTests.IntegrationTestGuard);

            // ---- Guards from Fhsm.Tests.Integration.HsmTerminalStateIntegrationTests ----
            HsmActionDispatcher.RegisterGuard(Hash("ReloadTestGuard"),
                (IntPtr)(delegate* <void*, void*, ushort, bool>)
                    &Fhsm.Tests.Integration.HsmTerminalStateIntegrationTests.ReloadTestGuard);
        }
    }
}
