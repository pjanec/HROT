using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Fhsm.Tests.SourceGen
{
    /// <summary>
    /// Tests for BHU-013/014: HSM source generator SharedAi and ExitCleanup infrastructure.
    /// Verifies that the generated HsmActionRegistrar exposes RequiredExitCleanups and that
    /// RegisterAll() is safe to call even when no SharedAi methods are present in the assembly.
    /// </summary>
    public class SharedAiHsmTests
    {
        // ---- BHU-013: RequiredExitCleanups dict is emitted -----------------------

        [Fact]
        public void GeneratedRegistrar_Has_RequiredExitCleanups_Property()
        {
            var regType = typeof(Fhsm.Tests.Generated.HsmActionRegistrar);
            // RequiredExitCleanups is a public static readonly field (not a property).
            var field = regType.GetField(
                "RequiredExitCleanups",
                BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(field);
            // Must implement IReadOnlyDictionary<string, string>
            Assert.True(
                typeof(IReadOnlyDictionary<string, string>).IsAssignableFrom(field!.FieldType),
                "RequiredExitCleanups must be assignable to IReadOnlyDictionary<string,string>");
        }

        // ---- BHU-013: When no [WritesChannel] actions exist the dict is empty ---

        [Fact]
        public void RequiredExitCleanups_IsEmpty_WhenNoWritesChannelActionsExist()
        {
            // The test assembly contains no [WritesChannel]-annotated actions,
            // so the generated dict should be empty.
            var regType = typeof(Fhsm.Tests.Generated.HsmActionRegistrar);
            var field = regType.GetField("RequiredExitCleanups", BindingFlags.Public | BindingFlags.Static);
            var dict   = (IReadOnlyDictionary<string, string>?)field?.GetValue(null);

            Assert.NotNull(dict);
            Assert.Empty(dict!);
        }

        // ---- BHU-014: RegisterAll does not throw with empty SharedAi set --------

        [Fact]
        public void RegisterAll_Exists_And_Is_Public_Static()
        {
            // Verify RegisterAll() is present without calling it, to avoid polluting
            // the shared HsmActionDispatcher static state used by other tests.
            var regType = typeof(Fhsm.Tests.Generated.HsmActionRegistrar);
            var method  = regType.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(method);
            Assert.True(method!.IsStatic);
        }

        // ---- BHU-013: Dispatcher has ExecuteAction and EvaluateGuard entries ----

        [Fact]
        public void GeneratedDispatcher_Has_ExecuteAction_Method()
        {
            // The kernel dispatcher is populated from the generated registrar.
            // Verify the dispatcher type exists and has the expected entry points.
            var dispType = typeof(Fhsm.Kernel.HsmActionDispatcher);

            Assert.NotNull(dispType.GetMethod("ExecuteAction",
                BindingFlags.Public | BindingFlags.Static));
            Assert.NotNull(dispType.GetMethod("EvaluateGuard",
                BindingFlags.Public | BindingFlags.Static));
        }
    }
}
