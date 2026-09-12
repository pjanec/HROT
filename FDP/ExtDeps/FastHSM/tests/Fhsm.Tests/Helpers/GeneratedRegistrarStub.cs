// Hand-written stand-in for the missing Fhsm.SourceGen output. Every test under SourceGen/
// exercises THIS FILE, not a source generator — the name of that folder is the only thing
// suggesting otherwise. Keeping the stub was a deliberate, recorded decision:
// .dev/_DONE/blueprints-2/reports/BATCH-01-REPORT.md:70.
//
// BP-307 (Batch 78), measured:
//
//   * `Fhsm.SourceGen` DID exist — FastHSM's own BATCH-11 created src/Fhsm.SourceGen/ — and was
//     later removed. Its successor is Fdp.Toolkits.Analyzers/HsmActionGenerator.cs, the only
//     `class HsmActionGenerator` left in the repo (.dev/_DONE/fluent-btree/ONBOARDING.md:88,103
//     maps the two).
//   * Until Batch 78 this project still carried a ProjectReference to the vanished project.
//     MSBuild printed "Skipping project ... because it was not found" and succeeded, so the
//     suite read as generator-backed while measuring a stub. It has been replaced by a comment.
//
// ⚠ THIS STUB NO LONGER MATCHES THE SUCCESSOR, and pointing the tests at it would not be a
// cleanup. Hash() below keys on the SHORT method name; HsmActionKey.ForActionName keys on the
// FULLY QUALIFIED name, and has done since E6(A) (Batch 72) — a change made by the blueprint
// programme, in a suite that was outside its gate set until Batch 76. The stub and
// ActionDispatchTests are self-consistent, so nothing goes red; they simply do not describe
// production dispatch. Reconciling them is FastHSM's call, not a side effect of a csproj tidy.

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
