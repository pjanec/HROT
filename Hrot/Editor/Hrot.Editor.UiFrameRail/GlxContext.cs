using System;
using System.Runtime.InteropServices;

namespace Hrot.Editor.UiFrameRail;

/// <summary>
/// ⭐ <c>CE-1039</c> — the GLX context of the harness's ONE window, handed from thread to thread. A GL context is current on
/// one thread at a time, and xUnit runs consecutive rails on different pool threads: the session that ends RELEASES it
/// (<see cref="Release"/>), the next one BINDS it on its own thread (<see cref="Bind"/>). GLFW creates the context through
/// GLX on X11 (it loads <c>libGL.so.1</c>, the library these calls resolve to), so the binding is the one GLFW made.
/// </summary>
internal static class GlxContext
{
    private const string Lib = "libGL.so.1";

    [DllImport(Lib)] private static extern IntPtr glXGetCurrentContext();
    [DllImport(Lib)] private static extern IntPtr glXGetCurrentDisplay();
    [DllImport(Lib)] private static extern IntPtr glXGetCurrentDrawable();
    [DllImport(Lib)] private static extern int glXMakeCurrent(IntPtr display, IntPtr drawable, IntPtr context);

    /// <summary>The display, drawable and context current on a thread.</summary>
    public readonly record struct Binding(IntPtr Display, IntPtr Drawable, IntPtr Context);

    /// <summary>The binding current on THIS thread, or null when there is none (or GLX is not available).</summary>
    public static Binding? Current()
    {
        try
        {
            var ctx = glXGetCurrentContext();
            if (ctx == IntPtr.Zero) return null;
            var dpy = glXGetCurrentDisplay();
            var drawable = glXGetCurrentDrawable();
            return dpy == IntPtr.Zero || drawable == IntPtr.Zero ? null : new Binding(dpy, drawable, ctx);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Makes <paramref name="b"/> current on THIS thread; false when GLX refused.</summary>
    public static bool Bind(Binding b) => glXMakeCurrent(b.Display, b.Drawable, b.Context) != 0;

    /// <summary>Releases whatever context is current on THIS thread, so another thread may bind it.</summary>
    public static void Release(Binding b) => glXMakeCurrent(b.Display, IntPtr.Zero, IntPtr.Zero);
}
