using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser
{
    public enum ExportWindowMode { FullFile, ByFrame, ByTime }
    public enum ExportFormatMode { AbsoluteState, Changelog }

    public sealed class JsonExportOptions
    {
        public ExportWindowMode WindowMode = ExportWindowMode.FullFile;
        public ExportFormatMode FormatMode = ExportFormatMode.AbsoluteState;

        public int StartFrame = 0;
        public int EndFrame = int.MaxValue;
        public float StartTimeSec = 0f;
        public float EndTimeSec = float.PositiveInfinity;

        public bool FilterBySelection = false;
        public List<Entity> TargetEntities = new();
        public bool FilterByEntityIndex = false;
        public int TargetEntityIndex = -1;

        public bool IncludeEntities = true;
        public bool IncludeEvents = true;
        public bool Minified = false;
        public double EpsilonTolerance = 0.001;
    }
}
