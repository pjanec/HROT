using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Presentation.Abstractions;

namespace Fdp.Presentation.Panels;

/// <summary>
/// Native Windows file dialogs executed on a transient STA thread.
/// </summary>
public sealed class WinFormsFileDialogService : IFileDialogService
{
    private readonly ConcurrentDictionary<string, string> _openDirectories = new();
    private readonly ConcurrentDictionary<string, string> _saveDirectories = new();
    private readonly string _settingsFilePath;
    private readonly object _stateFileLock = new();

    public WinFormsFileDialogService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string configDir = Path.Combine(appData, "HROT");
        Directory.CreateDirectory(configDir);
        _settingsFilePath = Path.Combine(configDir, "file_dialogs.json");
        LoadState();
    }

    public Task<string?> ShowSaveAsDialogAsync(string callSiteId, string defaultFileName, string extensionFilter)
    {
        string dir = _saveDirectories.GetOrAdd(callSiteId, Environment.CurrentDirectory);
        return ShowDialogAsync(openDialog: false, callSiteId, defaultFileName, extensionFilter, dir, _saveDirectories);
    }

    public Task<string?> ShowOpenFileDialogAsync(string callSiteId, string extensionFilter)
    {
        string dir = _openDirectories.GetOrAdd(callSiteId, Environment.CurrentDirectory);
        return ShowDialogAsync(openDialog: true, callSiteId, string.Empty, extensionFilter, dir, _openDirectories);
    }

    private Task<string?> ShowDialogAsync(
        bool openDialog,
        string callSiteId,
        string defaultFileName,
        string extensionFilter,
        string initialDir,
        ConcurrentDictionary<string, string> stateStore)
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
                string? result = ShowNativeDialog(openDialog, defaultFileName, extensionFilter, initialDir);
                if (!string.IsNullOrEmpty(result))
                {
                    stateStore[callSiteId] = Path.GetDirectoryName(result) ?? initialDir;
                    SaveState();
                }
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

    private string? ShowNativeDialog(bool openDialog, string defaultFileName, string extensionFilter, string initialDir)
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
            string safeInitialDir = Directory.Exists(initialDir) ? initialDir : Environment.CurrentDirectory;
            string title = openDialog ? "Open File" : "Save As";

            filterPtr = Marshal.StringToHGlobalUni(filter);
            titlePtr = Marshal.StringToHGlobalUni(title);
            initialDirPtr = Marshal.StringToHGlobalUni(safeInitialDir);

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

    private void LoadState()
    {
        if (!File.Exists(_settingsFilePath))
            return;

        try
        {
            string json = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
            FileDialogState? state = JsonSerializer.Deserialize<FileDialogState>(json);
            if (state == null)
                return;

            if (state.OpenDirectories != null)
            {
                foreach (KeyValuePair<string, string> kv in state.OpenDirectories)
                    _openDirectories[kv.Key] = kv.Value;
            }

            if (state.SaveDirectories != null)
            {
                foreach (KeyValuePair<string, string> kv in state.SaveDirectories)
                    _saveDirectories[kv.Key] = kv.Value;
            }
        }
        catch
        {
            // Best-effort load. Corrupted or locked state file should not break dialogs.
        }
    }

    private void SaveState()
    {
        try
        {
            var state = new FileDialogState
            {
                OpenDirectories = new Dictionary<string, string>(_openDirectories),
                SaveDirectories = new Dictionary<string, string>(_saveDirectories)
            };

            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            lock (_stateFileLock)
            {
                File.WriteAllText(_settingsFilePath, json, Encoding.UTF8);
            }
        }
        catch
        {
            // Best-effort persistence. Failures must not crash picker flow.
        }
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

    private sealed class FileDialogState
    {
        public Dictionary<string, string>? OpenDirectories { get; set; }
        public Dictionary<string, string>? SaveDirectories { get; set; }
    }
}
