using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Aura.Engine;

/// <summary>Sets still images as the normal Windows wallpaper, one per monitor (IDesktopWallpaper).</summary>
internal static class StaticWallpaper
{
    private enum Position { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
        uint GetMonitorDevicePathCount();
        Native.RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(Position position);
        Position GetPosition();
    }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }

    public static bool Set(string image, Rectangle screen, string fit)
    {
        try
        {
            var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
            try
            {
                string? target = null;
                var count = dw.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    var id = dw.GetMonitorDevicePathAt(i);
                    Native.RECT r;
                    try { r = dw.GetMonitorRECT(id); }
                    catch (COMException) { continue; } // monitor detached
                    if (r.Left == screen.Left && r.Top == screen.Top && r.Width == screen.Width && r.Height == screen.Height)
                    {
                        target = id;
                        break;
                    }
                }
                if (target is null)
                {
                    Log.Write($"No IDesktopWallpaper monitor matched {screen}; setting on all monitors");
                }

                dw.SetPosition(fit switch { "fit" => Position.Fit, "stretch" => Position.Stretch, _ => Position.Fill });
                dw.SetWallpaper(target, image);
                Log.Write($"Static wallpaper {image} on {target ?? "all"}");
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(dw);
            }
        }
        catch (Exception ex)
        {
            Log.Error("StaticWallpaper.Set", ex);
            // Old fallback: same picture everywhere.
            return Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, image,
                Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
        }
    }
}
