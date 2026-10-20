using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Markup;

namespace RegistryMerger.Gui;

/// <summary>UI strings of the current language (embedded Strings/&lt;code&gt;.json, English as fallback).</summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>
    /// Every embedded Strings/&lt;code&gt;.json: its native name ("_name") and whether it is a machine translation nobody has
    /// reviewed yet ("_ai"). A new language is just a new file. Order: English, Russian, Ukrainian, then by name.
    /// </summary>
    public static readonly (string Code, string Native, bool Ai)[] Languages = Discover();

    private static (string Code, string Native, bool Ai)[] Discover()
    {
        var first = new[] { "en", "ru", "uk" };
        var list = new List<(string, string, bool)>();
        foreach (var res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
        {
            var m = System.Text.RegularExpressions.Regex.Match(res, @"\.Strings\.([a-z]{2,3}(-[A-Za-z]{2,4})?)\.json$");
            if (!m.Success) continue;
            var code = m.Groups[1].Value;
            var d = Load(code);
            list.Add((code, d.TryGetValue("_name", out var n) ? n : code, d.TryGetValue("_ai", out var ai) && ai == "true"));
        }
        return list.OrderBy(l => Array.IndexOf(first, l.Item1) is var i && i >= 0 ? i : first.Length)
            .ThenBy(l => l.Item2, StringComparer.CurrentCulture).ToArray();
    }

    public static bool IsAi(string code) => Languages.Any(l => l.Code == code && l.Ai);

    public static Loc Instance { get; } = new();

    private Dictionary<string, string> _strings = new();
    private readonly Dictionary<string, string> _english = Load("en");

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Code { get; private set; } = "en";
    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    public string this[string key] => _strings.TryGetValue(key, out var s) ? s : _english.TryGetValue(key, out var e) ? e : key;

    public static string T(string key) => Instance[key];
    public static string F(string key, params object[] args) => string.Format(Instance.Culture, Instance[key], args);

    /// <summary>Plural form of a "one|few|many" (or "one|other") string, formatted with n.</summary>
    public static string P(string key, int n)
    {
        var forms = Instance[key].Split('|');
        var n10 = n % 10;
        var n100 = n % 100;
        var i = Instance.Code switch
        {
            "ru" or "uk" or "hr" => n10 == 1 && n100 != 11 ? 0 : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? 1 : 2,
            "pl" => n == 1 ? 0 : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? 1 : 2,
            "cs" => n == 1 ? 0 : n is >= 2 and <= 4 ? 1 : 2,
            _ => n == 1 ? 0 : 1,
        };
        return string.Format(Instance.Culture, forms[Math.Min(i, forms.Length - 1)], n.ToString("N0", Instance.Culture));
    }

    /// <summary>The system UI language if it is one of ours, else English.</summary>
    public static string SystemLanguage()
    {
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        two = two switch { "ua" => "uk", "bs" or "sr" => "hr", "sk" => "cs", _ => two };
        return Languages.Any(l => l.Code == two) ? two : "en";
    }

    /// <summary>"auto" (or empty) = system language.</summary>
    public void Set(string? code)
    {
        Code = string.IsNullOrEmpty(code) || code == "auto" ? SystemLanguage() : code;
        _strings = Code == "en" ? _english : Load(Code);
        Culture = CultureInfo.GetCultureInfo(Code);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static Dictionary<string, string> Load(string code)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($".Strings.{code}.json", StringComparison.OrdinalIgnoreCase));
        if (name == null) return new();
        using var s = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
    }
}

/// <summary>XAML: Text="{l:T Key}" — a live binding, so the text follows a language change.</summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

/// <summary>Settings in %APPDATA%\ScumRegistryMerger\settings.json; the AES key is encrypted for the current Windows user.</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "auto";
    public InstallKind Kind { get; set; } = InstallKind.Client;
    public string? GameFolder { get; set; }
    public string? AesKeyProtected { get; set; }
    public bool CheckUpdates { get; set; } = true;

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScumRegistryMerger");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    public bool IsConfigured => GameLocator.ResolvePaks(GameFolder) != null;

    public string? AesKey
    {
        get
        {
            if (string.IsNullOrEmpty(AesKeyProtected)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(AesKeyProtected), null, DataProtectionScope.CurrentUser)); }
            catch (Exception) { return null; }
        }
        set => AesKeyProtected = string.IsNullOrWhiteSpace(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception)
        {
            // unreadable settings: start over with the setup window
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

internal static class Native
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar (Windows 10 2004+ / 11).</summary>
    public static void DarkTitleBar(Window w)
    {
        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            var on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        };
    }
}
