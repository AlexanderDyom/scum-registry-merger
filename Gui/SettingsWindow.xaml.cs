using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace RegistryMerger.Gui;

public partial class SettingsWindow : Window
{
    private readonly string? _client = GameLocator.FindInstall(InstallKind.Client);
    private readonly string? _server = GameLocator.FindInstall(InstallKind.Server);
    private readonly string _languageBefore;
    private Updater.Release? _release;
    private bool _checked;
    private bool _loading = true;

    public AppSettings Result { get; }
    public bool RegistryDeleted { get; private set; }

    public SettingsWindow(AppSettings settings, Updater.Release? release, bool focusKey = false)
    {
        if (focusKey) Loaded += (_, _) => { KeyBox.Focus(); Keyboard.Focus(KeyBox); };
        Result = settings;
        _release = release;
        _checked = release != null;
        _languageBefore = settings.Language;
        InitializeComponent();
        Native.DarkTitleBar(this);

        (settings.Kind == InstallKind.Server ? ServerChip : ClientChip).IsChecked = true;
        PathBox.Text = settings.GameFolder ?? "";
        KeyBox.Password = settings.AesKey ?? "";
        AutoCheck.IsChecked = settings.CheckUpdates;
        BuildLanguages();
        _loading = false;
        Render();
        Loc.Instance.PropertyChanged += OnLanguage;
        Closed += (_, _) =>
        {
            Loc.Instance.PropertyChanged -= OnLanguage;
            if (DialogResult != true) Loc.Instance.Set(_languageBefore);
        };
    }

    private void OnLanguage(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Render();

    private void BuildLanguages()
    {
        LangPanel.Children.Clear();
        foreach (var o in LanguageList.Options())
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("Chip"), GroupName = "lang", Tag = o.Code, Content = LanguageList.Label(o),
                IsChecked = o.Code == (string.IsNullOrEmpty(Result.Language) ? "auto" : Result.Language),
            };
            rb.Checked += (_, _) =>
            {
                Result.Language = o.Code;
                Loc.Instance.Set(o.Code);
            };
            LangPanel.Children.Add(rb);
        }
    }

    private void Render()
    {
        // "System (Русский)" follows the UI language
        if (LangPanel.Children.Count > 0 && LangPanel.Children[0] is RadioButton { Content: StackPanel { Children: [TextBlock t, ..] } })
            t.Text = Loc.F("LangSystem", Loc.Languages.First(l => l.Code == Loc.SystemLanguage()).Native);
        LangNote.Text = Loc.T("AiNote");

        PathCheck.Show(PathStatus, PathBox.Text);
        var hasKey = KeyBox.Password.Trim().Length > 0;
        KeyHint.Text = hasKey ? Loc.T("KeyStoredHint") : Loc.T("KeyRecommendShort");
        KeyHint.Foreground = (Brush)FindResource(hasKey ? "Text2" : "Warn");

        VersionLine.Text = Loc.F("VersionLine", Updater.CurrentText);
        var newer = _release != null && Updater.IsNewer(_release);
        if (!Updater.Configured)
        {
            UpdateLine.Text = Loc.T("UpdatesNotConfigured");
            UpdateLine.Foreground = (Brush)FindResource("Muted");
        }
        else if (newer)
        {
            UpdateLine.Text = "✓ " + Loc.F("NewVersion", _release!.Version.ToString(3), _release.Published.ToLocalTime().ToString("d", Loc.Instance.Culture));
            UpdateLine.Foreground = (Brush)FindResource("Ok");
        }
        else
        {
            UpdateLine.Text = _checked ? Loc.T("UpToDate") : "";
            UpdateLine.Foreground = (Brush)FindResource("Text2");
        }
        UpdateLine.Visibility = UpdateLine.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotesButton.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton.Visibility = newer && _release!.ExeUrl != null ? Visibility.Visible : Visibility.Collapsed;
        CheckButton.Visibility = Updater.Configured && !newer ? Visibility.Visible : Visibility.Collapsed;
        AutoCheck.Visibility = Updater.Configured ? Visibility.Visible : Visibility.Collapsed;

        var paks = GameLocator.ResolvePaks(PathBox.Text);
        var exists = paks != null && MergeEngine.SharedFiles(paks).Any();
        DeleteText.Text = exists ? Loc.T("DeleteRegistryText") : Loc.T("NoRegistryToDelete");
        DeleteButton.IsEnabled = exists;
    }

    private void Kind_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var found = ServerChip.IsChecked == true ? _server : _client;
        if (found != null) PathBox.Text = found;
    }

    private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) Render();
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
        if (!_loading) Render();
    }

    private async void CheckKey_Click(object sender, RoutedEventArgs e)
    {
        var paks = GameLocator.ResolvePaks(PathBox.Text);
        if (paks == null) { PathCheck.Show(PathStatus, PathBox.Text, force: true); return; }
        if (KeyBox.Password.Trim().Length == 0) return;
        if (await KeyCheck.ValidateAsync(paks, KeyBox.Password, KeyError, CheckKeyButton))
        {
            KeyHint.Text = "✓ " + Loc.T("KeyWorks");
            KeyHint.Foreground = (Brush)FindResource("Ok");
        }
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        try
        {
            _release = await Updater.LatestAsync();
            _checked = true;
        }
        catch (Exception ex)
        {
            Dialog.Show(this, Loc.T("UpdateFailedTitle"), ex.Message, Loc.T("Close"), icon: "");
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
        Render();
    }

    private void Notes_Click(object sender, RoutedEventArgs e)
    {
        if (_release == null) return;
        var text = _release.Notes.Trim().Length > 0 ? _release.Notes.Trim() : Loc.T("NoNotes");
        if (Dialog.Show(this, Loc.F("WhatsNewTitle", _release.Version.ToString(3)), text, Loc.T("OpenOnGitHub"), Loc.T("Close")))
            Process.Start(new ProcessStartInfo(_release.PageUrl) { UseShellExecute = true });
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_release == null) return;
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = Loc.T("Downloading");
        try
        {
            await Updater.InstallAsync(_release);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = Loc.T("UpdateNow");
            Dialog.Show(this, Loc.T("UpdateFailedTitle"), ex.Message, Loc.T("Close"), icon: "");
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var paks = GameLocator.ResolvePaks(PathBox.Text);
        if (paks == null) return;
        if (!Dialog.Show(this, Loc.T("DeleteConfirmTitle"), Loc.F("DeleteConfirmText", "pakchunk999-ModsRegistry"), Loc.T("DeleteRegistry"), Loc.T("Cancel"), icon: ""))
            return;
        if (GameLocator.IsGameRunning())
        {
            Dialog.Show(this, Loc.T("GameRunningTitle"), Loc.T("GameRunningText"), Loc.T("Close"), icon: "");
            return;
        }
        MergeEngine.DeleteShared(paks);
        RegistryDeleted = true;
        Render();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var paks = GameLocator.ResolvePaks(PathBox.Text);
        if (paks == null) { PathCheck.Show(PathStatus, PathBox.Text, force: true); return; }
        var key = KeyBox.Password.Trim();
        if (key.Length > 0 && key != (Result.AesKey ?? "") && !await KeyCheck.ValidateAsync(paks, key, KeyError, SaveButton)) return;
        Result.GameFolder = GameLocator.InstallRoot(paks);
        Result.Kind = ServerChip.IsChecked == true ? InstallKind.Server : InstallKind.Client;
        Result.AesKey = key;
        Result.CheckUpdates = AutoCheck.IsChecked == true;
        DialogResult = true;
    }
}
