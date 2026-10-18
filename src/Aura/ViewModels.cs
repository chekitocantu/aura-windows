using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Aura.Core;
using Aura.Core.Library;
using Aura.Core.Online;
using Aura.Engine;

namespace Aura;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class LibraryCard : Observable
{
    private ImageSource? _thumb;
    private bool _isActive;

    public LibraryCard(LibraryItem item)
    {
        Item = item;
        var size = item.Size >= 1 << 30 ? $"{item.Size / (double)(1 << 30):0.0} GB" : $"{item.Size / (double)(1 << 20):0.#} MB";
        Subtitle = $"{item.Extension} · {size}";
        Kind = item.IsVideo ? Strings.T("lib.videos").ToUpperInvariant() : Strings.T("lib.images").ToUpperInvariant();
    }

    public LibraryItem Item { get; }
    public string Title => Item.Title;
    public string Subtitle { get; }
    public string Kind { get; }
    public string ActiveText => Strings.T("lib.active");
    public string HoverText => Strings.T("lib.apply");

    public ImageSource? Thumb { get => _thumb; set => Set(ref _thumb, value); }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

public sealed class OnlineCard : Observable
{
    private ImageSource? _thumb;
    private bool _isDownloading;
    private double _progress;
    private string? _localPath;

    public OnlineCard(OnlineItem item, string? localPath)
    {
        Item = item;
        _localPath = localPath;
        Subtitle = string.IsNullOrEmpty(item.Resolution) ? item.Author : $"{item.Resolution} · {item.Author}";
    }

    public OnlineItem Item { get; }
    public string Title => Item.Title;
    public string Subtitle { get; }
    public string Kind => Item.IsVideo ? Strings.T("lib.videos").ToUpperInvariant() : Strings.T("lib.images").ToUpperInvariant();
    public string DownloadText => Strings.T("exp.download");
    public string DownloadApplyText => Strings.T("exp.downloadapply");
    public string DoneText => Strings.T("exp.downloaded");
    public string ApplyText => Strings.T("lib.apply");

    public ImageSource? Thumb { get => _thumb; set => Set(ref _thumb, value); }

    public bool IsDownloading
    {
        get => _isDownloading;
        set { if (Set(ref _isDownloading, value)) Raise(nameof(IsIdle)); }
    }

    public double Progress { get => _progress; set => Set(ref _progress, value); }

    public string? LocalPath
    {
        get => _localPath;
        set
        {
            if (Set(ref _localPath, value)) { Raise(nameof(IsDone)); Raise(nameof(IsIdle)); }
        }
    }

    public bool IsDone => _localPath is not null;
    public bool IsIdle => !_isDownloading && _localPath is null;
}

public sealed class ScreenCard : Observable
{
    private ImageSource? _thumb;

    public ScreenCard(ScreenInfo screen, string? file)
    {
        Screen = screen;
        File = file;
        Name = Strings.F("scr.name", screen.Number) + (screen.IsPrimary ? " · " + Strings.T("scr.primary") : "");
        Detail = $"{screen.Bounds.Width} × {screen.Bounds.Height}";
        FileName = file is null ? Strings.T("scr.none") : System.IO.Path.GetFileNameWithoutExtension(file);
    }

    public ScreenInfo Screen { get; }
    public string? File { get; }
    public string Name { get; }
    public string Number => Screen.Number.ToString(CultureInfo.InvariantCulture);
    public string Detail { get; }
    public string FileName { get; }
    public bool HasFile => File is not null;
    public string ChangeText => Strings.T("scr.change");
    public string ClearText => Strings.T("scr.clear");

    // Position in the small monitor map.
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    public ImageSource? Thumb { get => _thumb; set => Set(ref _thumb, value); }
}

/// <summary>Visible when the bound value is false / null.</summary>
public sealed class InverseVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is true || (value is not null && value is not bool) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Visible when the bound value is true / non-null.</summary>
public sealed class ToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is true || (value is not null && value is not bool) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
