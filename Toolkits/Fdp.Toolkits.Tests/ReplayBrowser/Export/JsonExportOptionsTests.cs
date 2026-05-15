using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Export
{
    public class JsonExportOptionsTests
    {
        [Fact]
        public void Defaults_MatchDesignSpec()
        {
            var opts = new JsonExportOptions();

            Assert.Equal(ExportWindowMode.FullFile, opts.WindowMode);
            Assert.Equal(ExportFormatMode.AbsoluteState, opts.FormatMode);
            Assert.Equal(0, opts.StartFrame);
            Assert.Equal(int.MaxValue, opts.EndFrame);
            Assert.Equal(0f, opts.StartTimeSec);
            Assert.Equal(float.PositiveInfinity, opts.EndTimeSec);
            Assert.False(opts.FilterBySelection);
            Assert.NotNull(opts.TargetEntities);
            Assert.Empty(opts.TargetEntities);
            Assert.False(opts.FilterByEntityIndex);
            Assert.Equal(-1, opts.TargetEntityIndex);
            Assert.True(opts.IncludeEntities);
            Assert.True(opts.IncludeEvents);
            Assert.False(opts.Minified);
            Assert.Equal(0.001, opts.EpsilonTolerance);
        }

        [Fact]
        public void RoundTrip_Json_PreservesAllFields()
        {
            // Note: Entity has two constructors and no [JsonConstructor], so System.Text.Json
            // cannot deserialize it. TargetEntities is kept empty in this round-trip test.
            // The default value (float.PositiveInfinity) for EndTimeSec is also not JSON-serializable,
            // so a finite value is used here.
            var opts = new JsonExportOptions
            {
                WindowMode = ExportWindowMode.ByFrame,
                FormatMode = ExportFormatMode.Changelog,
                StartFrame = 5,
                EndFrame = 100,
                StartTimeSec = 1.5f,
                EndTimeSec = 99.9f,
                FilterBySelection = true,
                TargetEntities = new List<Entity>(),
                FilterByEntityIndex = true,
                TargetEntityIndex = 3,
                IncludeEntities = false,
                IncludeEvents = false,
                Minified = true,
                EpsilonTolerance = 0.005,
            };

            string json = JsonSerializer.Serialize(opts, FdpJsonOptionsRegistry.Indented);
            var restored = JsonSerializer.Deserialize<JsonExportOptions>(json, FdpJsonOptionsRegistry.Indented);

            Assert.NotNull(restored);
            Assert.Equal(opts.WindowMode, restored!.WindowMode);
            Assert.Equal(opts.FormatMode, restored.FormatMode);
            Assert.Equal(opts.StartFrame, restored.StartFrame);
            Assert.Equal(opts.EndFrame, restored.EndFrame);
            Assert.Equal(opts.StartTimeSec, restored.StartTimeSec);
            Assert.Equal(opts.EndTimeSec, restored.EndTimeSec);
            Assert.Equal(opts.FilterBySelection, restored.FilterBySelection);
            Assert.Empty(restored.TargetEntities);
            Assert.Equal(opts.FilterByEntityIndex, restored.FilterByEntityIndex);
            Assert.Equal(opts.TargetEntityIndex, restored.TargetEntityIndex);
            Assert.Equal(opts.IncludeEntities, restored.IncludeEntities);
            Assert.Equal(opts.IncludeEvents, restored.IncludeEvents);
            Assert.Equal(opts.Minified, restored.Minified);
            Assert.Equal(opts.EpsilonTolerance, restored.EpsilonTolerance);
        }
    }
}
