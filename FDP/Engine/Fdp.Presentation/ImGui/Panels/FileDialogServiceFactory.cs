using System;
using Fdp.Presentation.Abstractions;

namespace Fdp.Presentation.Panels;

/// <summary>
/// Selects the platform-appropriate <see cref="IFileDialogService"/> implementation.
/// </summary>
public static class FileDialogServiceFactory
{
    /// <summary>
    /// Creates the file dialog service for the current OS: the native comdlg32-backed
    /// <see cref="WinFormsFileDialogService"/> on Windows, or the OS-agnostic
    /// <see cref="ImGuiFileDialogService"/> everywhere else.
    /// </summary>
    public static IFileDialogService Create()
        => OperatingSystem.IsWindows()
            ? new WinFormsFileDialogService()
            : new ImGuiFileDialogService();
}
