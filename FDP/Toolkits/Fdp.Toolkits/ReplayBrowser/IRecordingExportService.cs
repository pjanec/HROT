namespace Fdp.Toolkit.ReplayBrowser
{
    public interface IRecordingExportService
    {
        /// <summary>
        /// Streams an .fdp recording to <paramref name="outputJsonPath"/> using
        /// <paramref name="options"/>. Allocation-isolated; uses its own ReplayBrowserContext.
        /// </summary>
        void ExportToJson(string inputFdpPath, string outputJsonPath, JsonExportOptions options);
    }
}
