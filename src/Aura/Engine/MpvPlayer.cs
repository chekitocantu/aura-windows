using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core;

namespace Aura.Engine;

/// <summary>One mpv process playing one video on one monitor, controlled through its JSON IPC pipe.</summary>
internal sealed class MpvPlayer : IDisposable
{
    private static int _counter;

    private readonly Process _process;
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly object _writeGate = new();

    public string Device { get; }
    public string File { get; }
    public Rectangle Bounds { get; }
    public IntPtr Window { get; private set; }
    public bool HasAudio { get; }
    public bool IsPaused { get; private set; }

    public bool IsAlive
    {
        get
        {
            try { return !_process.HasExited; }
            catch { return false; }
        }
    }

    private MpvPlayer(Process process, string pipeName, string device, string file, Rectangle bounds, bool audio)
    {
        _process = process;
        _pipeName = pipeName;
        Device = device;
        File = file;
        Bounds = bounds;
        HasAudio = audio;
    }

    public static async Task<MpvPlayer> StartAsync(string file, string device, Rectangle bounds, bool audio,
        AuraSettings settings, DesktopLayer layer, bool startPaused, CancellationToken ct)
    {
        if (!System.IO.File.Exists(AppPaths.MpvExe))
            throw new FileNotFoundException(Strings.T("err.mpv"), AppPaths.MpvExe);

        var index = Interlocked.Increment(ref _counter);
        var pipeName = $"aura-mpv-{Environment.ProcessId}-{index}";
        var args = BuildArgs(file, pipeName, audio, settings, layer.IsRaised, startPaused, index);

        var psi = new ProcessStartInfo(AppPaths.MpvExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(AppPaths.MpvExe)!,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Log.Write($"Starting mpv #{index} on {device} {bounds}: {file}");
        var process = Process.Start(psi) ?? throw new InvalidOperationException("mpv did not start");
        ChildProcessJob.Add(process);

        var player = new MpvPlayer(process, pipeName, device, file, bounds, audio) { IsPaused = startPaused };
        try
        {
            player.Window = await WaitForWindowAsync(process, ct);
            if (!layer.Attach(player.Window, bounds))
                throw new InvalidOperationException("could not attach to the desktop");
            await player.ConnectAsync(ct);
            return player;
        }
        catch
        {
            player.Dispose();
            throw;
        }
    }

    internal static List<string> BuildArgs(string file, string pipeName, bool audio, AuraSettings s,
        bool raisedDesktop, bool startPaused, int index)
    {
        var a = new List<string>
        {
            "--no-config",
            "--terminal=no",
            "--loop-file=inf",
            "--force-window=yes",
            "--no-border",
            "--window-minimized=yes",
            "--geometry=-9999:0",
            "--auto-window-resize=no",
            "--keepaspect-window=no",
            "--title=AuraWallpaper",
            "--no-osc",
            "--osd-level=0",
            "--no-osd-bar",
            "--input-default-bindings=no",
            "--input-vo-keyboard=no",
            "--input-cursor=no",
            "--cursor-autohide=no",
            "--no-window-dragging",
            "--stop-screensaver=no",
            "--media-controls=no",
            "--taskbar-progress=no",
            "--sub-auto=no",
            "--audio-file-auto=no",
            "--profile=fast",
            "--hwdec=" + (s.HardwareDecoding ? "auto-safe" : "no"),
            "--gpu-shader-cache-dir=" + Path.Combine(AppPaths.Cache, "mpv-shaders"),
            "--input-ipc-server=\\\\.\\pipe\\" + pipeName,
            "--log-file=" + Path.Combine(Log.Folder, $"mpv-{index % 4}.log"),
            "--msg-level=all=warn",
        };

        // Microsoft's guidance for the raised desktop: present with blt, not flip, into a layered window.
        if (raisedDesktop) a.Add("--d3d11-flip=no");

        switch (s.Fit)
        {
            case "fill": a.Add("--panscan=1.0"); break;
            case "stretch": a.Add("--keepaspect=no"); break;
        }

        if (audio)
        {
            a.Add("--mute=" + (s.Mute ? "yes" : "no"));
            a.Add("--volume=" + s.Volume);
        }
        else
        {
            a.Add("--no-audio");
        }

        if (startPaused) a.Add("--pause");
        a.Add("--");
        a.Add(file);
        return a;
    }

    private static async Task<IntPtr> WaitForWindowAsync(Process process, CancellationToken ct)
    {
        var pid = (uint)process.Id;
        for (var i = 0; i < 150; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new InvalidOperationException($"mpv closed right away (code {process.ExitCode}). Is the file a valid video?");

            var found = IntPtr.Zero;
            Native.EnumWindows((h, _) =>
            {
                Native.GetWindowThreadProcessId(h, out var owner);
                if (owner == pid && Native.ClassName(h) == "mpv") { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) return found;
            await Task.Delay(100, ct);
        }
        throw new TimeoutException("mpv did not open its window");
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(200, ct);
                _pipe = pipe;
                _writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                _ = Task.Run(DrainAsync);
                return;
            }
            catch (TimeoutException) { }
            catch (IOException) { await Task.Delay(100, ct); }
        }
        Log.Write($"mpv IPC pipe {_pipeName} never connected; pause will not work for this player");
    }

    // mpv answers every command; read the answers so the pipe never fills up.
    private async Task DrainAsync()
    {
        try
        {
            using var reader = new StreamReader(_pipe!, Encoding.UTF8);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.Contains("\"error\":") && !line.Contains("\"error\":\"success\""))
                    Log.Write($"mpv {Device}: {line}");
            }
        }
        catch { /* pipe closed */ }
    }

    public void Command(params object[] args)
    {
        var json = JsonSerializer.Serialize(new { command = args });
        lock (_writeGate)
        {
            try { _writer?.WriteLine(json); }
            catch (Exception ex) { Log.Write($"mpv {Device} write failed: {ex.Message}"); }
        }
    }

    public void SetPaused(bool paused)
    {
        if (IsPaused == paused) return;
        IsPaused = paused;
        Command("set_property", "pause", paused);
    }

    public void SetMute(bool mute) { if (HasAudio) Command("set_property", "mute", mute); }
    public void SetVolume(int volume) { if (HasAudio) Command("set_property", "volume", volume); }

    public void Dispose()
    {
        try
        {
            if (IsAlive)
            {
                Command("quit");
                if (!_process.WaitForExit(800)) _process.Kill();
            }
        }
        catch { }
        try { _writer?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _process.Dispose();
    }
}
