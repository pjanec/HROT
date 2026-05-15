using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Presentation.Abstractions;

namespace Fdp.Presentation.Panels;

/// <summary>
/// Native Windows file dialogs executed on a transient STA thread.
/// </summary>
public sealed class WinFormsFileDialogService : IFileDialogService
{
    private string _currentDirectory = Environment.CurrentDirectory;

    public Task<string?> ShowSaveAsDialogAsync(string defaultFileName, string extensionFilter)
        => ShowDialogAsync(openDialog: false, defaultFileName, extensionFilter);

    public Task<string?> ShowOpenFileDialogAsync(string extensionFilter)
        => ShowDialogAsync(openDialog: true, string.Empty, extensionFilter);

    private Task<string?> ShowDialogAsync(bool openDialog, string defaultFileName, string extensionFilter)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!OperatingSystem.IsWindows())
        {
            tcs.TrySetResult(null);
            return tcs.Task;
        }

        Thread thread = new(() =>
        {
            try
            {
                string? result = ShowNativeDialog(openDialog, defaultFileName, extensionFilter);
                if (!string.IsNullOrEmpty(result))
                    _currentDirectory = Path.GetDirectoryName(result) ?? _currentDirectory;
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    private string? ShowNativeDialog(bool openDialog, string defaultFileName, string extensionFilter)
    {
        const int maxPath = 1024;
        IntPtr filterPtr = IntPtr.Zero;
        IntPtr filePtr = IntPtr.Zero;
        IntPtr titlePtr = IntPtr.Zero;
        IntPtr initialDirPtr = IntPtr.Zero;
        IntPtr defExtPtr = IntPtr.Zero;

        try
        {
            string filter = BuildComdlgFilter(extensionFilter);
            string initialDir = Directory.Exists(_currentDirectory) ? _currentDirectory : Environment.CurrentDirectory;
            string title = openDialog ? "Open File" : "Save As";

            filterPtr = Marshal.StringToHGlobalUni(filter);
            titlePtr = Marshal.StringToHGlobalUni(title);
            initialDirPtr = Marshal.StringToHGlobalUni(initialDir);

            if (!openDialog)
            {
                string defExt = GetDefaultExtension(extensionFilter);
                if (!string.IsNullOrEmpty(defExt))
                    defExtPtr = Marshal.StringToHGlobalUni(defExt);
            }

            filePtr = Marshal.AllocHGlobal(maxPath * 2);
            for (int i = 0; i < maxPath; i++)
                Marshal.WriteInt16(filePtr, i * 2, 0);

            if (!string.IsNullOrEmpty(defaultFileName))
            {
                ReadOnlySpan<char> name = defaultFileName.AsSpan();
                int copyLen = Math.Min(name.Length, maxPath - 1);
                for (int i = 0; i < copyLen; i++)
                    Marshal.WriteInt16(filePtr, i * 2, name[i]);
            }

            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf(typeof(OPENFILENAME)),
                hwndOwner = IntPtr.Zero,
                hInstance = IntPtr.Zero,
                lpstrFilter = filterPtr,
                lpstrCustomFilter = IntPtr.Zero,
                nMaxCustFilter = 0,
                nFilterIndex = 1,
                lpstrFile = filePtr,
                nMaxFile = maxPath,
                lpstrFileTitle = IntPtr.Zero,
                nMaxFileTitle = 0,
                lpstrInitialDir = initialDirPtr,
                lpstrTitle = titlePtr,
                Flags = openDialog
                    ? OFN_PATHMUSTEXIST | OFN_FILEMUSTEXIST | OFN_HIDEREADONLY | OFN_EXPLORER
                    : OFN_PATHMUSTEXIST | OFN_OVERWRITEPROMPT | OFN_HIDEREADONLY | OFN_EXPLORER,
                nFileOffset = 0,
                nFileExtension = 0,
                lpstrDefExt = defExtPtr,
                lCustData = IntPtr.Zero,
                lpfnHook = IntPtr.Zero,
                lpTemplateName = IntPtr.Zero,
                pvReserved = IntPtr.Zero,
                dwReserved = 0,
                FlagsEx = 0
            };

            bool ok = openDialog ? GetOpenFileName(ref ofn) : GetSaveFileName(ref ofn);
            if (!ok)
                return null;

            string path = Marshal.PtrToStringUni(filePtr) ?? string.Empty;
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        finally
        {
            if (filterPtr != IntPtr.Zero) Marshal.FreeHGlobal(filterPtr);
            if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
            if (titlePtr != IntPtr.Zero) Marshal.FreeHGlobal(titlePtr);
            if (initialDirPtr != IntPtr.Zero) Marshal.FreeHGlobal(initialDirPtr);
            if (defExtPtr != IntPtr.Zero) Marshal.FreeHGlobal(defExtPtr);
        }
    }

    private static string BuildComdlgFilter(string extensionFilter)
    {
        if (string.IsNullOrWhiteSpace(extensionFilter))
            return "All files (*.*)\0*.*\0\0";

        return $"Files ({extensionFilter})\0{extensionFilter}\0All files (*.*)\0*.*\0\0";
    }

    private static string GetDefaultExtension(string extensionFilter)
    {
        if (string.IsNullOrWhiteSpace(extensionFilter))
            return string.Empty;

        string trimmed = extensionFilter.Trim();
        if (trimmed.StartsWith("*."))
            return trimmed.Substring(2);
        if (trimmed.StartsWith("."))
            return trimmed.Substring(1);
        return trimmed;
    }

    private const int OFN_HIDEREADONLY = 0x00000004;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_EXPLORER = 0x00080000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileName(ref OPENFILENAME ofn);
}
