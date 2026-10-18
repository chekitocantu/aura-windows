using System;
using Microsoft.Win32;

namespace Aura;

/// <summary>"Open Aura when Windows starts", through the per-user Run key (no admin needed).</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Aura";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            var exe = Environment.ProcessPath;
            if (enabled && exe is not null)
                key.SetValue(Name, $"\"{exe}\" --background");
            else
                key.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error("Startup.Apply", ex);
        }
    }
}
