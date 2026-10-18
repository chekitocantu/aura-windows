using System;
using System.Drawing;
using System.Windows.Forms;
using Aura.Core;

namespace Aura;

/// <summary>The icon next to the clock: open, pause, remove, exit.</summary>
internal sealed class Tray : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pause;
    private readonly ToolStripMenuItem _stop;

    public Tray(Action open, Action exit)
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false };
        menu.Items.Add(new ToolStripMenuItem(Strings.T("tray.open"), null, (_, _) => open()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        _pause = new ToolStripMenuItem(Strings.T("tray.pause"), null, (_, _) => TogglePause());
        _stop = new ToolStripMenuItem(Strings.T("tray.stop"), null, async (_, _) => await AppState.Current.Engine.ClearAsync(null));
        menu.Items.Add(_pause);
        menu.Items.Add(_stop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.T("tray.exit"), null, (_, _) => exit()));
        foreach (ToolStripItem item in menu.Items)
        {
            item.ForeColor = Color.FromArgb(241, 242, 248);
            item.Padding = new Padding(6, 4, 6, 4);
        }

        _icon = new NotifyIcon
        {
            Text = "Aura",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) open(); };
        Refresh();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/aura.ico"));
            if (res != null) return new Icon(res.Stream, SystemInformation.SmallIconSize);
        }
        catch (Exception ex) { Log.Error("tray icon", ex); }
        return SystemIcons.Application;
    }

    private static void TogglePause()
    {
        var engine = AppState.Current.Engine;
        engine.SetUserPaused(!engine.UserPaused);
    }

    public void Refresh()
    {
        var engine = AppState.Current.Engine;
        _pause.Text = engine.UserPaused ? Strings.T("tray.resume") : Strings.T("tray.pause");
        _pause.Enabled = engine.AnyVideo;
        _stop.Enabled = engine.AnyVideo;
    }

    public void ShowHint(string title, string body) => _icon.ShowBalloonTip(4000, title, body, ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = true; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Color.FromArgb(241, 242, 248) : Color.FromArgb(94, 97, 120);
            base.OnRenderItemText(e);
        }
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(23, 24, 36);
        private static readonly Color Hover = Color.FromArgb(38, 40, 56);
        public override Color ToolStripDropDownBackground => Bg;
        public override Color MenuBorder => Color.FromArgb(38, 40, 56);
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color SeparatorDark => Color.FromArgb(38, 40, 56);
        public override Color SeparatorLight => Bg;
    }
}
