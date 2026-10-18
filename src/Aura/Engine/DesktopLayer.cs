using System;
using System.Drawing;
using static Aura.Engine.Native;

namespace Aura.Engine;

/// <summary>
/// Finds the layer between the desktop picture and the icons, and puts windows there.
///
/// Classic desktop (Windows 10, Windows 11 before 24H2): sending 0x052C to Progman creates a
/// top-level WorkerW behind the icons; wallpaper windows become its children.
///
/// Raised desktop (Windows 11 24H2 and later): Progman has WS_EX_NOREDIRECTIONBITMAP and the
/// WorkerW is a child of Progman. Microsoft's guidance is to add our own WS_EX_LAYERED child of
/// Progman, z-ordered under SHELLDLL_DefView (the icons) and above that WorkerW.
/// </summary>
internal sealed class DesktopLayer
{
    public IntPtr Progman { get; private set; }
    public IntPtr WorkerW { get; private set; }
    public IntPtr DefView { get; private set; }
    public bool IsRaised { get; private set; }

    public bool IsValid => Progman != IntPtr.Zero && IsWindow(Progman) &&
                           (IsRaised ? DefView != IntPtr.Zero : WorkerW != IntPtr.Zero && IsWindow(WorkerW));

    public bool Initialize()
    {
        Progman = FindWindow("Progman", null);
        if (Progman == IntPtr.Zero)
        {
            Log.Write("Progman not found (Explorer not running?)");
            return false;
        }

        IsRaised = (GetStyle(Progman, GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP) != 0;

        // Ask Progman to split the wallpaper from the icons. Harmless if already done.
        SendMessageTimeout(Progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), SMTO_NORMAL, 1000, out _);
        SendMessageTimeout(Progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);

        WorkerW = IntPtr.Zero;
        DefView = IntPtr.Zero;

        if (IsRaised)
        {
            DefView = FindWindowEx(Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            WorkerW = FindWindowEx(Progman, IntPtr.Zero, "WorkerW", null);
        }

        if (DefView == IntPtr.Zero)
        {
            // Classic layout: the WorkerW we want is the top-level sibling right after the one holding DefView.
            EnumWindows((top, _) =>
            {
                var view = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view != IntPtr.Zero)
                {
                    DefView = view;
                    WorkerW = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                }
                return true;
            }, IntPtr.Zero);

            if (DefView != IntPtr.Zero && GetParent(DefView) == Progman && !IsRaised)
            {
                // Icons still live in Progman and no WorkerW came up: draw straight into Progman.
                if (WorkerW == IntPtr.Zero) WorkerW = Progman;
            }
        }

        Log.Write($"Desktop layer: raised={IsRaised} progman={Progman} workerw={WorkerW} defview={DefView}");
        return IsValid;
    }

    /// <summary>Makes <paramref name="hwnd"/> a wallpaper covering <paramref name="screen"/> (physical pixels).</summary>
    public bool Attach(IntPtr hwnd, Rectangle screen)
    {
        if (!IsValid && !Initialize()) return false;

        // Turn the window into a borderless child that never takes focus.
        var style = GetStyle(hwnd, GWL_STYLE);
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        style |= WS_CHILD;
        SetStyle(hwnd, GWL_STYLE, style);

        var ex = GetStyle(hwnd, GWL_EXSTYLE);
        ex &= ~WS_EX_APPWINDOW;
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;

        IntPtr parent;
        if (IsRaised)
        {
            ex |= WS_EX_LAYERED;
            SetStyle(hwnd, GWL_EXSTYLE, ex);
            SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
            parent = Progman;
        }
        else
        {
            SetStyle(hwnd, GWL_EXSTYLE, ex);
            parent = WorkerW;
        }

        if (SetParent(hwnd, parent) == IntPtr.Zero && GetParent(hwnd) != parent)
        {
            Log.Write($"SetParent failed for {hwnd}");
            return false;
        }

        // Child coordinates are relative to the parent, which spans the whole virtual screen.
        GetWindowRect(parent, out var p);
        var x = screen.X - p.Left;
        var y = screen.Y - p.Top;

        if (IsRaised)
        {
            // Directly under the icons, then make sure Windows' own WorkerW stays at the very bottom.
            SetWindowPos(hwnd, DefView, x, y, screen.Width, screen.Height, SWP_NOACTIVATE | SWP_FRAMECHANGED);
            EnsureWorkerWAtBottom();
        }
        else if (parent == Progman)
        {
            SetWindowPos(hwnd, DefView, x, y, screen.Width, screen.Height, SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        else
        {
            SetWindowPos(hwnd, IntPtr.Zero, x, y, screen.Width, screen.Height,
                SWP_NOACTIVATE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }
        ShowWindow(hwnd, SW_SHOWNA);

        GetWindowRect(hwnd, out var r);
        Log.Write($"Attached {hwnd} to {parent} at {r} for screen {screen}");
        return true;
    }

    public void EnsureWorkerWAtBottom()
    {
        if (!IsRaised || WorkerW == IntPtr.Zero) return;
        var last = GetWindow(GetWindow(Progman, GW_CHILD), GW_HWNDLAST);
        if (last != WorkerW)
            SetWindowPos(WorkerW, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Repaints the normal wallpaper so no frozen video frame stays behind after stopping.</summary>
    public void RefreshWallpaper()
    {
        // On the raised desktop this would recreate the WorkerW, and it is not needed there.
        if (IsRaised) return;
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null, SPIF_UPDATEINIFILE);
    }

    /// <summary>True for windows that belong to the desktop itself.</summary>
    public bool IsDesktopWindow(IntPtr hwnd)
    {
        if (hwnd == Progman || hwnd == WorkerW) return true;
        var cls = ClassName(hwnd);
        return cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
}
