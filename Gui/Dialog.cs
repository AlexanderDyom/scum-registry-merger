using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RegistryMerger.Gui;

/// <summary>Small themed message dialog: title, text, an optional warning line and up to two buttons.</summary>
public static class Dialog
{
    /// <summary>Returns true when the primary button was pressed.</summary>
    public static bool Show(Window? owner, string title, string text, string primary, string? secondary = null, string? warning = null, string icon = "")
    {
        var w = new Window
        {
            Title = title, SizeToContent = SizeToContent.Height, Width = 420, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = owner == null || !owner.IsVisible,
            Style = (Style)Application.Current.Resources["AppWindow"],
        };
        if (owner != null && owner.IsVisible) w.Owner = owner;
        Native.DarkTitleBar(w);
        var res = Application.Current.Resources;
        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(new TextBlock { Text = icon, Style = (Style)res["Icon"], Foreground = (Brush)res["AccentText"], Margin = new Thickness(0, 0, 8, 0), FontSize = 18 });
        head.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)res["Text2"], LineHeight = 19 });
        if (warning != null)
            panel.Children.Add(new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)res["Warn"], FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var result = false;
        if (secondary != null)
        {
            var b2 = new Button { Content = secondary, Style = (Style)res["Btn"], Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            b2.Click += (_, _) => w.Close();
            buttons.Children.Add(b2);
        }
        var b1 = new Button { Content = primary, Style = (Style)res["Primary"], IsDefault = true, IsCancel = secondary == null };
        b1.Click += (_, _) => { result = true; w.Close(); };
        buttons.Children.Add(b1);
        panel.Children.Add(buttons);
        w.Content = panel;
        w.ShowDialog();
        return result;
    }
}
