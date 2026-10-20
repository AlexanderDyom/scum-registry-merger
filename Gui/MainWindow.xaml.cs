using System.IO;
using System.Windows;
using System.Windows.Media;

namespace RegistryMerger.Gui;

/// <summary>One line of the mod list.</summary>
public sealed class ModRow
{
    public string Glyph { get; init; } = "";
    public Brush StateBrush { get; init; } = Brushes.Transparent;
    public Brush RowBrush { get; init; } = Brushes.Transparent;
    public string Name { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Badge { get; init; } = "";
    public Visibility BadgeVisibility => Badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string Count { get; init; } = "";
    public Brush CountBrush { get; init; } = Brushes.Transparent;
    public string Status { get; init; } = "";
    public Brush StatusBrush { get; init; } = Brushes.Transparent;
    public string? Tip { get; init; }
}

public partial class MainWindow : Window
{
    private enum Mode { Scanning, Building, Ready, Error }

    private Mode _mode;
    private ScanResult? _scan;
    private string? _error;
    private string _progress = "";
    private bool _firstScan = true;
    private Updater.Release? _update;

    private AppSettings S => App.Settings;
    private string Paks => GameLocator.ResolvePaks(S.GameFolder) ?? S.GameFolder ?? "";

    public MainWindow()
    {
        InitializeComponent();
        Native.DarkTitleBar(this);
        Loc.Instance.PropertyChanged += (_, _) => Render();
        KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F5 && _mode is Mode.Ready or Mode.Error) await ScanAsync();
        };
        Loaded += async (_, _) =>
        {
            await ScanAsync();
            await CheckUpdateAsync();
        };
    }

    private Brush B(string key) => (Brush)FindResource(key);

    // ------------------------------------------------------------------ actions

    private async Task ScanAsync()
    {
        _mode = Mode.Scanning;
        _progress = "";
        Render();
        var paks = Paks;
        var key = S.AesKey;
        var progress = new Progress<string>(p => { _progress = p; RenderBanner(); });
        try
        {
            _scan = await Task.Run(() =>
            {
                using var engine = MergeEngine.Open(paks, key);
                return engine.Scan(log: progress);
            });
            _mode = Mode.Ready;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _mode = Mode.Error;
        }
        Render();

        if (_firstScan && _scan != null)
        {
            _firstScan = false;
            await OfferBuildAsync(_scan);
        }
    }

    /// <summary>At startup: a mod was added, updated or removed since the last build (or the game updated).</summary>
    private async Task OfferBuildAsync(ScanResult scan)
    {
        if (!scan.NeedsBuild) return;
        var changed = scan.Mods.Where(m => m.State == ModState.Missing).ToList();
        string title, text;
        if (changed.Count == 1 && changed[0].IsNew && scan.RemovedMods.Count == 0 && !scan.GameUpdated)
        {
            title = Loc.T("NewModTitle");
            text = Loc.F("NewModText", changed[0].Name, Loc.P("ItemsCount", changed[0].NewRecords), MergeEngine.OutputName);
        }
        else
        {
            title = Loc.T("ChangedTitle");
            text = string.Join("\n", Reasons(scan).Prepend(Loc.F("ChangedText", MergeEngine.OutputName)));
        }
        if (Dialog.Show(this, title, text, Loc.T("BuildRegistry"), Loc.T("Later"), Loc.T(S.Kind == InstallKind.Server ? "CloseServerFirst" : "CloseGameFirst"), ""))
            await BuildAsync();
    }

    private async Task BuildAsync()
    {
        if (GameLocator.IsGameRunning())
        {
            Dialog.Show(this, Loc.T("GameRunningTitle"), Loc.T("GameRunningText"), Loc.T("Close"), icon: "");
            return;
        }
        _mode = Mode.Building;
        _progress = "";
        Render();
        var paks = Paks;
        var key = S.AesKey;
        var progress = new Progress<string>(p => { _progress = p; RenderBanner(); });
        try
        {
            await Task.Run(() =>
            {
                var engine = MergeEngine.Open(paks, key);
                try { engine.Build(log: progress); }
                finally { engine.Dispose(); }
            });
        }
        catch (Exception ex)
        {
            _mode = Mode.Ready;
            Render();
            Dialog.Show(this, Loc.T("BuildFailedTitle"), ex.Message, Loc.T("Close"), icon: "");
            return;
        }
        await ScanAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_mode is Mode.Scanning or Mode.Building) return;
        await ScanAsync();
    }

    private async void BannerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == Mode.Error) await ScanAsync();
        else if (_mode == Mode.Ready) await BuildAsync();
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_mode is Mode.Scanning or Mode.Building) return;
        var w = new SettingsWindow(S.Clone(), _update) { Owner = this };
        if (w.ShowDialog() != true)
        {
            if (w.RegistryDeleted) await ScanAsync();
            return;
        }
        var before = (S.GameFolder, S.AesKeyProtected, S.Kind);
        App.Settings.Language = w.Result.Language;
        App.Settings.Kind = w.Result.Kind;
        App.Settings.GameFolder = w.Result.GameFolder;
        App.Settings.AesKeyProtected = w.Result.AesKeyProtected;
        App.Settings.CheckUpdates = w.Result.CheckUpdates;
        App.Settings.Save();
        Loc.Instance.Set(S.Language);
        if (w.RegistryDeleted || before != (S.GameFolder, S.AesKeyProtected, S.Kind)) await ScanAsync();
        else Render();
    }

    private async Task CheckUpdateAsync()
    {
        if (!S.CheckUpdates || !Updater.Configured) return;
        try
        {
            var r = await Updater.LatestAsync();
            if (r != null && Updater.IsNewer(r)) { _update = r; Render(); }
        }
        catch (Exception)
        {
            // offline: no update hint
        }
    }

    // ------------------------------------------------------------------ rendering

    private void Render()
    {
        // install line
        KindIcon.Text = S.Kind == InstallKind.Server ? "" : "";
        KindText.Text = Loc.T(S.Kind == InstallKind.Server ? "Server" : "Client");
        PathText.Text = S.GameFolder ?? "";
        PathText.ToolTip = Paks;
        var hasKey = !string.IsNullOrEmpty(S.AesKey);
        var keyWorks = _scan?.GameRegistryReadable == true;
        (KeyBadge.Background, KeyBadgeText.Foreground, KeyBadgeIcon.Foreground, KeyBadgeText.Text, KeyBadge.ToolTip) =
            !hasKey ? (B("WarnBg"), B("Warn"), B("Warn"), Loc.T("BadgeNoKey"), Loc.T("BadgeNoKeyTip"))
            : _scan == null || keyWorks ? (B("OkBg"), B("Ok"), B("Ok"), Loc.T("BadgeKey"), Loc.T("BadgeKeyTip"))
            : (B("DangerBg"), B("Danger"), B("Danger"), Loc.T("BadgeKeyBad"), Loc.T("BadgeKeyBadTip"));

        RenderBanner();
        RenderList();

        // footer
        FooterText.Text = _scan is { SharedExists: true }
            ? Loc.F("FooterShared", MergeEngine.OutputName, _scan.SharedBuiltAt?.ToString("g", Loc.Instance.Culture) ?? "?", _scan.SharedRecords.ToString("N0", Loc.Instance.Culture))
            : Loc.F("FooterNoShared", MergeEngine.OutputName);
        VersionText.Text = "v" + Updater.CurrentText;
        UpdateLink.Visibility = _update != null ? Visibility.Visible : Visibility.Collapsed;
        if (_update != null) UpdateLink.Content = Loc.F("UpdateAvailable", _update.Version.ToString(3));
        RefreshButton.IsEnabled = _mode is Mode.Ready or Mode.Error;
    }

    private void SetBanner(string bg, string fg, string glyph, string title, string text, string? button, bool primary = true)
    {
        Banner.Background = B(bg);
        BannerIcon.Text = glyph;
        BannerIcon.Foreground = B(fg);
        BannerTitle.Text = title;
        BannerTitle.Foreground = B(fg == "Text2" ? "Text" : fg);
        BannerText.Text = text;
        BannerText.Foreground = B(fg == "Text2" ? "Text2" : fg);
        BannerText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BannerButton.Visibility = button != null ? Visibility.Visible : Visibility.Collapsed;
        BannerButton.Content = button;
        BannerButton.Style = (Style)FindResource(primary ? "Primary" : "Btn");
    }

    private void RenderBanner()
    {
        switch (_mode)
        {
            case Mode.Scanning:
                SetBanner("Surface", "Text2", "", Loc.T("Scanning"), _progress, null);
                return;
            case Mode.Building:
                SetBanner("AccentBg", "AccentText", "", Loc.T("Building"), _progress, null);
                return;
            case Mode.Error:
                SetBanner("DangerBg", "Danger", "", Loc.T("ScanFailed"), _error ?? "", Loc.T("TryAgain"));
                return;
        }
        var s = _scan!;
        if (s.Mods.Count == 0)
            SetBanner("Surface", "Text2", "", Loc.T("NoModsTitle"), Loc.F("NoModsText", Path.Combine(Paks, "~mods")), null);
        else if (s.NeedsBuild)
        {
            var title = s.MissingCount > 0 ? Loc.P("BannerMissing", s.MissingCount)
                : s.RemovedMods.Count > 0 ? Loc.T("BannerRemovedTitle")
                : s.GameUpdated ? Loc.T("BannerGameUpdatedTitle")
                : Loc.T("BannerNoKeyBuildTitle");
            SetBanner("DangerBg", "Danger", "", title, string.Join("\n", Reasons(s)), Loc.T("BuildRegistry"));
        }
        else if (!s.AnyRegistryMods)
            SetBanner("Surface", "Text2", "", Loc.T("NothingNeededTitle"), Loc.T("NothingNeededText"), null);
        else
            SetBanner("OkBg", "Ok", "", Loc.T("AllGoodTitle"), Loc.T("AllGoodText"), Loc.T("Rebuild"), primary: false);
    }

    /// <summary>Why a rebuild is needed, one line each.</summary>
    private static IEnumerable<string> Reasons(ScanResult s)
    {
        if (s.MissingCount > 0) yield return Loc.T("ReasonMissing");
        if (s.RemovedMods.Count > 0) yield return Loc.F("ReasonRemoved", string.Join(", ", s.RemovedMods));
        if (s.GameUpdated) yield return Loc.T("ReasonGameUpdated");
        if (s.BuiltWithoutKey) yield return Loc.T("ReasonNoKeyBuild");
    }

    private void RenderList()
    {
        var mods = _scan?.Mods ?? [];
        EmptyState.Visibility = _mode == Mode.Ready && mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Loc.F("EmptyText", Path.Combine(Paks, "~mods"));
        var approx = _scan is { GameRegistryReadable: false };
        var culture = Loc.Instance.Culture;
        ModList.ItemsSource = mods.Select(m => new ModRow
        {
            Glyph = m.State == ModState.Missing ? "" : "",
            StateBrush = B(m.State == ModState.Missing ? "Danger" : "Ok"),
            RowBrush = m.State == ModState.Missing ? B("DangerBg") : Brushes.Transparent,
            Name = m.Name,
            FileName = m.FileName,
            Badge = m.State != ModState.Missing ? "" : m.IsUpdated ? Loc.T("BadgeUpdated") : m.IsNew ? Loc.T("BadgeNew") : "",
            Count = m.NewRecords > 0 ? (approx ? "≈ " : "") + m.NewRecords.ToString("N0", culture) : "—",
            CountBrush = B(m.NewRecords > 0 ? "Text" : "Muted"),
            Status = Loc.T(m.State switch { ModState.Missing => "StateMissing", ModState.Included => "StateIncluded", _ => m.HasRegistry ? "StateNoNew" : "StateNoRegistry" }),
            StatusBrush = B(m.State switch { ModState.Missing => "Danger", ModState.Included => "Ok", _ => "Text2" }),
            Tip = m.Sample.Count > 0
                ? Loc.F("TipItems", string.Join(", ", m.Sample) + (m.NewRecords > m.Sample.Count ? ", …" : "")) + (approx ? "\n" + Loc.T("TipApprox") : "")
                : m.HasRegistry ? null : Loc.T("TipNoRegistry"),
        }).ToList();
    }
}
