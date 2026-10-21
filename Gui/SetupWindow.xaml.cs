using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace RegistryMerger.Gui;

public partial class SetupWindow : Window
{
    private readonly string? _client = GameLocator.FindInstall(InstallKind.Client);
    private readonly string? _server = GameLocator.FindInstall(InstallKind.Server);
    private bool _loading = true;
    private string? _verifiedKey;

    public AppSettings Result { get; }

    public SetupWindow(AppSettings settings)
    {
        Result = settings;
        InitializeComponent();
        Native.DarkTitleBar(this);
        LanguageList.Fill(LangBox, settings.Language);

        var kind = settings.Kind;
        if (settings.GameFolder == null && _client == null && _server != null) kind = InstallKind.Server;
        (kind == InstallKind.Server ? ServerCard : ClientCard).IsChecked = true;
        PathBox.Text = settings.GameFolder ?? (kind == InstallKind.Server ? _server : _client) ?? "";
        KeyBox.Password = settings.AesKey ?? "";
        _loading = false;
        Refresh();
        Loc.Instance.PropertyChanged += (_, _) => Refresh();
    }

    private void Refresh()
    {
        ClientFound.Text = _client != null ? "✓ " + Loc.T("FoundInSteam") : Loc.T("NotFound");
        ClientFound.Foreground = (System.Windows.Media.Brush)FindResource(_client != null ? "Ok" : "Muted");
        ServerFound.Text = _server != null ? "✓ " + Loc.T("FoundInSteam") : Loc.T("NotFound");
        ServerFound.Foreground = (System.Windows.Media.Brush)FindResource(_server != null ? "Ok" : "Muted");
        PathCheck.Show(PathStatus, PathBox.Text);
    }

    private void Kind_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var found = ServerCard.IsChecked == true ? _server : _client;
        if (found != null) PathBox.Text = found;
    }

    private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) PathCheck.Show(PathStatus, PathBox.Text);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("BrowseTitle") };
        if (Directory.Exists(PathBox.Text)) dlg.InitialDirectory = PathBox.Text;
        if (dlg.ShowDialog(this) == true) PathBox.Text = dlg.FolderName;
    }

    private void KeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        KeyError.Visibility = Visibility.Collapsed;
        var verified = _verifiedKey != null && KeyBox.Password.Trim() == _verifiedKey;
        KeyOk.Visibility = verified ? Visibility.Visible : Visibility.Collapsed;
        MakeRefButton.IsEnabled = verified;
    }

    private async void CheckKey_Click(object sender, RoutedEventArgs e)
    {
        var paks = GameLocator.ResolvePaks(PathBox.Text);
        if (paks == null) { PathCheck.Show(PathStatus, PathBox.Text, force: true); return; }
        var key = KeyBox.Password.Trim();
        if (key.Length == 0) return;
        if (!await KeyCheck.ValidateAsync(paks, key, KeyError, CheckKeyButton)) return;
        _verifiedKey = key;
        KeyOk.Text = "\u2713 " + Loc.T("KeyWorks");
        KeyOk.Visibility = Visibility.Visible;
        MakeRefButton.IsEnabled = true;
        ReferenceUi.ShowStatus(RefStatus, paks);
    }

    private async void MakeRef_Click(object sender, RoutedEventArgs e)
    {
        var paks = GameLocator.ResolvePaks(PathBox.Text);
        if (paks == null || _verifiedKey == null) return;
        await ReferenceUi.MakeAsync(paks, _verifiedKey, MakeRefButton, RefStatus);
    }

    private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LangBox.SelectedItem is LangOption o)
        {
            Result.Language = o.Code;
            Loc.Instance.Set(o.Code);
        }
    }

    private void WhereKey_Click(object sender, RoutedEventArgs e) =>
        Dialog.Show(this, Loc.T("WhereKeyTitle"), Loc.T("WhereKeyText"), Loc.T("Close"), icon: "");

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        var paks = GameLocator.ResolvePaks(PathBox.Text);
        if (paks == null) { PathCheck.Show(PathStatus, PathBox.Text, force: true); return; }

        var key = KeyBox.Password.Trim();
        if (key.Length > 0 && key != _verifiedKey)
        {
            if (!await KeyCheck.ValidateAsync(paks, key, KeyError, ContinueButton)) return;
        }

        Result.GameFolder = GameLocator.InstallRoot(paks);
        Result.Kind = ServerCard.IsChecked == true ? InstallKind.Server : InstallKind.Client;
        Result.AesKey = key;
        DialogResult = true;
    }
}

/// <summary>The "make a reference of your game version" block of the setup and settings windows.</summary>
internal static class ReferenceUi
{
    /// <summary>"There is / isn't a reference of this game version" next to the button.</summary>
    public static void ShowStatus(TextBlock status, string? paks)
    {
        if (paks == null) { status.Text = ""; return; }
        var exists = ReferenceStore.Find(GameReference.GameFingerprint(paks)) != null;
        status.Text = exists ? "\u2713 " + Loc.T("RefExists") : Loc.T("RefMissing");
        status.Foreground = (System.Windows.Media.Brush)status.FindResource(exists ? "Ok" : "Muted");
    }

    /// <summary>Makes the reference of the installed game version with the (checked) key and saves it where scans look.</summary>
    public static async Task MakeAsync(string paks, string key, Button button, TextBlock status)
    {
        button.IsEnabled = false;
        status.Text = Loc.T("RefMaking");
        status.Foreground = (System.Windows.Media.Brush)status.FindResource("Text2");
        try
        {
            var reference = await Task.Run(() =>
            {
                using var engine = MergeEngine.Open(paks, key);
                var r = engine.MakeReference() ?? throw new InvalidOperationException(Loc.T("KeyWrong"));
                ReferenceStore.Save(r);
                return r;
            });
            status.Text = "\u2713 " + Loc.F("RefDone", reference.Fingerprint, reference.Count.ToString("N0", Loc.Instance.Culture));
            status.Foreground = (System.Windows.Media.Brush)status.FindResource("Ok");
        }
        catch (Exception ex)
        {
            status.Text = Loc.F("RefFailed", ex.Message);
            status.Foreground = (System.Windows.Media.Brush)status.FindResource("Danger");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}

/// <summary>"Paks folder found, N mods in ~mods" / "not a SCUM folder" under a path box.</summary>
internal static class PathCheck
{
    public static void Show(TextBlock target, string path, bool force = false)
    {
        var paks = GameLocator.ResolvePaks(path);
        if (paks != null)
        {
            var mods = Path.Combine(paks, "~mods");
            var n = Directory.Exists(mods)
                ? Directory.EnumerateFiles(mods, "*.pak").Except(MergeEngine.SharedFiles(paks)).Count()
                : 0;
            target.Text = "✓ " + Loc.F("PaksFound", n);
            target.Foreground = (System.Windows.Media.Brush)target.FindResource("Ok");
        }
        else
        {
            target.Text = string.IsNullOrWhiteSpace(path) && !force ? Loc.T("PathEmpty") : Loc.T("PaksNotFound");
            target.Foreground = (System.Windows.Media.Brush)target.FindResource(string.IsNullOrWhiteSpace(path) && !force ? "Muted" : "Danger");
        }
    }
}

/// <summary>Checks the key format, then that it opens the game's paks (a few seconds).</summary>
internal static class KeyCheck
{
    public static async Task<bool> ValidateAsync(string paks, string key, TextBlock error, Button busyButton)
    {
        try { MergeEngine.NormalizeKey(key); }
        catch (FormatException)
        {
            Fail(error, Loc.T("KeyBadFormat"));
            return false;
        }
        var content = busyButton.Content;
        busyButton.IsEnabled = false;
        busyButton.Content = Loc.T("KeyChecking");
        try
        {
            var ok = await Task.Run(() => MergeEngine.TestKey(paks, key));
            if (!ok) Fail(error, Loc.T("KeyWrong"));
            return ok;
        }
        catch (Exception ex)
        {
            Fail(error, ex.Message);
            return false;
        }
        finally
        {
            busyButton.Content = content;
            busyButton.IsEnabled = true;
        }
    }

    private static void Fail(TextBlock error, string text)
    {
        error.Text = text;
        error.Visibility = Visibility.Visible;
    }
}

/// <summary>A language choice: "auto" = the system language. Ai = machine translation, shown with an "AI" mark.</summary>
public sealed record LangOption(string Code, string Text, bool Ai)
{
    public Visibility AiVisibility => Ai ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Language combo and chips: "System (X)" + every language, machine translations marked "AI".</summary>
internal static class LanguageList
{
    public static List<LangOption> Options()
    {
        var sys = Loc.SystemLanguage();
        var list = new List<LangOption> { new("auto", Loc.F("LangSystem", Loc.Languages.First(l => l.Code == sys).Native), Loc.IsAi(sys)) };
        list.AddRange(Loc.Languages.Select(l => new LangOption(l.Code, l.Native, l.Ai)));
        return list;
    }

    public static void Fill(ComboBox box, string selected)
    {
        var options = Options();
        box.ItemsSource = options;
        box.SelectedItem = options.FirstOrDefault(o => o.Code == (string.IsNullOrEmpty(selected) ? "auto" : selected)) ?? options[0];
    }

    /// <summary>Name + amber "AI" mark (for chips).</summary>
    public static UIElement Label(LangOption o)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = o.Text, FontSize = 12 });
        if (o.Ai)
            panel.Children.Add(new TextBlock
            {
                Text = "AI", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 0),
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Warn"), ToolTip = Loc.T("AiTip"),
            });
        return panel;
    }
}
