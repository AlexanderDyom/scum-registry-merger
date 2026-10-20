using System.Windows;
using System.Windows.Threading;

namespace RegistryMerger.Gui;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        Updater.CleanUp();
        Settings = AppSettings.Load();
        Loc.Instance.Set(Settings.Language);

        if (!Settings.IsConfigured)
        {
            var setup = new SetupWindow(Settings.Clone());
            if (setup.ShowDialog() != true) { Shutdown(); return; }
            Settings = setup.Result;
            Settings.Save();
        }
        ShowMain();
    }

    public static void ShowMain()
    {
        var main = new MainWindow();
        Current.MainWindow = main;
        Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Dialog.Show(MainWindow, Loc.T("ErrorTitle"), e.Exception.Message, Loc.T("Close"));
        e.Handled = true;
    }
}
