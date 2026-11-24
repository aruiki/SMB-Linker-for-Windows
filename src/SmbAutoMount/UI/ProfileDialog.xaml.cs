using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SmbAutoMount.Core;
using SmbAutoMount.Native;

namespace SmbAutoMount.UI;

public sealed class IconChoice
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public BitmapImage? Image { get; init; }
    public Visibility DefaultVisibility => Image == null ? Visibility.Visible : Visibility.Collapsed;

    public static IconChoice WindowsDefault() => new() { Path = "", Name = Strings.T("pf.iconDefault") };

    public static IconChoice FromFile(string file)
    {
        var key = System.IO.Path.GetFileNameWithoutExtension(file);
        var name = Strings.T("icon." + key);
        return new IconChoice
        {
            Path = file,
            Name = name.StartsWith("icon.") ? key : name,
            Image = new BitmapImage(new Uri(file)) { DecodePixelWidth = 48 },
        };
    }
}

public sealed partial class ProfileDialog : ContentDialog
{
    private readonly MountProfile? _original;
    private bool _passwordTouched;

    public MountProfile? Result { get; private set; }
    public bool PasswordChanged { get; private set; }

    public ProfileDialog(MountProfile? existing)
    {
        InitializeComponent();
        _original = existing;
        Title = Strings.T(existing == null ? "pf.titleAdd" : "pf.titleEdit");

        var p = existing ?? new MountProfile { DriveLetter = SuggestLetter(), Icon = Paths.DefaultDriveIcon };
        ServerBox.Text = p.Server;
        ShareBox.Text = p.Share;
        UserBox.Text = p.UserName;
        LabelBox.Text = p.Label;
        IconBox.Text = p.Icon;
        EnabledToggle.IsOn = p.Enabled;
        FillLetters(p.DriveLetter);
        LoadGallery();
        AppearanceExpander.IsExpanded = existing == null || !string.IsNullOrEmpty(p.Label) || !string.IsNullOrEmpty(p.Icon);

        bool hasStored = existing != null && CredMan.Exists(CredMan.TargetFor(existing.Id));
        PasswordHint.Text = Strings.T(hasStored ? "pf.passwordKeep" : "pf.passwordHint");
        if (hasStored) PasswordInput.PlaceholderText = "••••••••";
    }

    private static string SuggestLetter()
    {
        var used = App.Instance.Config.Profiles.Select(x => x.DriveLetter.ToUpperInvariant()).ToHashSet();
        var taken = DriveInfo.GetDrives().Select(d => d.Name[..1].ToUpperInvariant()).ToHashSet();
        for (char c = 'Z'; c >= 'D'; c--)
        {
            var s = c.ToString();
            if (!used.Contains(s) && !taken.Contains(s)) return s;
        }
        return "Z";
    }

    private void FillLetters(string selected)
    {
        var local = DriveInfo.GetDrives()
            .Where(d => d.DriveType != DriveType.Network)
            .Select(d => d.Name[..1].ToUpperInvariant()).ToHashSet();
        var sel = selected.ToUpperInvariant().TrimEnd(':');
        var items = Enumerable.Range('D', 'Z' - 'D' + 1).Select(i => ((char)i).ToString())
            .Where(l => !local.Contains(l) || l == sel)
            .Select(l => l + ":").ToList();
        LetterBox.ItemsSource = items;
        LetterBox.SelectedItem = items.FirstOrDefault(i => i == sel + ":") ?? items.LastOrDefault();
    }

    /// <summary>Accepts "\\server\share" or "smb://server/share" pasted into the server box.</summary>
    private void ServerBox_LostFocus(object sender, RoutedEventArgs e) => SplitServer();

    private void SplitServer()
    {
        var t = ServerBox.Text.Trim().Replace('/', '\\');
        if (t.StartsWith("smb:", StringComparison.OrdinalIgnoreCase)) t = t[4..];
        if (!t.Contains('\\')) return;
        var parts = t.TrimStart('\\').Split('\\', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1) ServerBox.Text = parts[0];
        if (parts.Length == 2 && string.IsNullOrWhiteSpace(ShareBox.Text)) ShareBox.Text = parts[1].TrimEnd('\\');
    }

    private void Password_Changed(object sender, RoutedEventArgs e) => _passwordTouched = true;

    private MountProfile? Build()
    {
        SplitServer();
        string? error = null;
        if (string.IsNullOrWhiteSpace(ServerBox.Text)) error = Strings.T("pf.errServer");
        else if (string.IsNullOrWhiteSpace(ShareBox.Text)) error = Strings.T("pf.errShare");
        else if (LetterBox.SelectedItem == null) error = Strings.T("pf.errLetter");
        else
        {
            var letter = ((string)LetterBox.SelectedItem).TrimEnd(':');
            var clash = App.Instance.Config.Profiles.FirstOrDefault(x =>
                x.Id != _original?.Id && x.DriveLetter.Equals(letter, StringComparison.OrdinalIgnoreCase));
            if (clash != null) error = Strings.F("pf.errLetterUsed", letter + ":", clash.RemotePath);
        }
        if (error != null)
        {
            ShowResult(InfoBarSeverity.Error, error);
            return null;
        }

        var p = _original?.Clone() ?? new MountProfile();
        p.Server = ServerBox.Text.Trim();
        p.Share = ShareBox.Text.Trim().Trim('\\', '/').Replace('/', '\\');
        p.DriveLetter = ((string)LetterBox.SelectedItem!).TrimEnd(':');
        p.UserName = UserBox.Text.Trim();
        p.Label = LabelBox.Text.Trim();
        p.Icon = IconBox.Text.Trim();
        p.Enabled = EnabledToggle.IsOn;
        return p;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var p = Build();
        if (p == null) return;
        TestButton.IsEnabled = false;
        TestRing.IsActive = true;
        ResultBar.IsOpen = false;
        string? pw = _passwordTouched ? PasswordInput.Password : null;
        try
        {
            if (!await DriveMapper.IsReachableAsync(p.Server, 3000))
            {
                ShowResult(InfoBarSeverity.Error, Strings.T("diag.portFail"));
                return;
            }
            var r = await Task.Run(() => DriveMapper.TestLogin(p, pw));
            if (r.State == MountState.Connected) ShowResult(InfoBarSeverity.Success, Strings.T("pf.testOk"));
            else ShowResult(InfoBarSeverity.Error, $"{r.Message} (code {r.Code})");
        }
        finally
        {
            TestButton.IsEnabled = true;
            TestRing.IsActive = false;
        }
    }

    private void ShowResult(InfoBarSeverity severity, string text)
    {
        ResultBar.Severity = severity;
        ResultBar.Message = text;
        ResultBar.IsOpen = true;
    }

    private void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var p = Build();
        if (p == null)
        {
            args.Cancel = true;
            return;
        }

        var target = CredMan.TargetFor(p.Id);
        try
        {
            if (string.IsNullOrWhiteSpace(p.UserName))
            {
                // No user name = connect as the Windows account; a stored password would be meaningless.
                CredMan.Delete(target);
            }
            else if (_passwordTouched && PasswordInput.Password.Length > 0)
            {
                CredMan.Save(target, p.QualifiedUserName ?? "", PasswordInput.Password);
                PasswordChanged = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Saving credential failed", ex);
            ShowResult(InfoBarSeverity.Error, ex.Message);
            args.Cancel = true;
            return;
        }

        PasswordInput.Password = "";
        Result = p;
    }

    // ---------- icon ----------

    private bool _syncingIcon;

    private void LoadGallery()
    {
        var list = new System.Collections.Generic.List<IconChoice> { IconChoice.WindowsDefault() };
        try
        {
            // Order: plain colours first, then themed badges, then anything else.
            string[] order = { "green", "blue", "orange", "purple", "red", "teal", "cloud", "media", "backup", "home", "photo", "lock" };
            var files = Directory.Exists(Paths.DriveIconDir) ? Directory.GetFiles(Paths.DriveIconDir, "*.ico") : Array.Empty<string>();
            foreach (var f in files.OrderBy(f =>
                     {
                         var n = Path.GetFileNameWithoutExtension(f).Replace("drive-", "");
                         var i = Array.IndexOf(order, n);
                         return i < 0 ? 100 : i;
                     }))
                list.Add(IconChoice.FromFile(f));
        }
        catch (Exception ex)
        {
            Log.Error("Loading built-in icons failed", ex);
        }
        IconGallery.ItemsSource = list;
        SyncGalleryToText();
    }

    private void SyncGalleryToText()
    {
        if (IconGallery.ItemsSource is not System.Collections.Generic.List<IconChoice> list) return;
        _syncingIcon = true;
        var t = IconBox.Text.Trim();
        IconGallery.SelectedItem = list.FirstOrDefault(c => string.Equals(c.Path, t, StringComparison.OrdinalIgnoreCase));
        _syncingIcon = false;
    }

    private void IconGallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingIcon || IconGallery.SelectedItem is not IconChoice c) return;
        IconBox.Text = c.Path;
    }

    private async void IconBrowse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".ico");
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".dll");
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        IconBox.Text = file.Path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? file.Path : file.Path + ",0";
    }

    private void IconBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            var path = Environment.ExpandEnvironmentVariables(IconBox.Text.Trim().Trim('"'));
            IconPreview.Source = path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
                ? new BitmapImage(new Uri(path)) { DecodePixelWidth = 24 }
                : null;
        }
        catch
        {
            IconPreview.Source = null;
        }
        SyncGalleryToText();
    }
}
