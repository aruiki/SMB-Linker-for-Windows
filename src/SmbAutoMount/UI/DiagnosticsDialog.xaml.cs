using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SmbAutoMount.Core;
using Windows.ApplicationModel.DataTransfer;

namespace SmbAutoMount.UI;

public sealed class DiagRow
{
    public required string Glyph { get; init; }
    public required Brush Brush { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
}

public sealed partial class DiagnosticsDialog : ContentDialog
{
    private readonly MountProfile _profile;
    private DiagReport? _report;

    public DiagnosticsDialog(MountProfile profile)
    {
        InitializeComponent();
        _profile = profile;
        TargetText.Text = $"{profile.LocalName}  →  {profile.RemotePath}";
        Opened += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        IsPrimaryButtonEnabled = IsSecondaryButtonEnabled = false;
        Busy.Visibility = Visibility.Visible;
        SummaryBar.Severity = InfoBarSeverity.Informational;
        SummaryBar.Message = Strings.T("diag.running");
        try
        {
            _report = await Diagnostics.RunAsync(_profile);
            Items.ItemsSource = _report.Items.Select(ToRow).ToList();
            bool fail = _report.Items.Any(i => i.Level == DiagLevel.Fail);
            bool warn = _report.Items.Any(i => i.Level == DiagLevel.Warn);
            SummaryBar.Severity = fail ? InfoBarSeverity.Error : warn ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            SummaryBar.Message = Strings.T(fail ? "diag.sumFail" : warn ? "diag.sumWarn" : "diag.sumOk");
            IsPrimaryButtonEnabled = fail || warn;
            IsSecondaryButtonEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error("Diagnostics failed", ex);
            SummaryBar.Severity = InfoBarSeverity.Error;
            SummaryBar.Message = ex.Message;
        }
        finally
        {
            Busy.Visibility = Visibility.Collapsed;
        }
    }

    private static DiagRow ToRow(DiagItem i)
    {
        Brush B(string key) => (Brush)Application.Current.Resources[key];
        var (glyph, brush) = i.Level switch
        {
            DiagLevel.Ok => ("", B("SystemFillColorSuccessBrush")),
            DiagLevel.Info => ("", B("TextFillColorSecondaryBrush")),
            DiagLevel.Warn => ("", B("SystemFillColorCautionBrush")),
            _ => ("", B("SystemFillColorCriticalBrush")),
        };
        return new DiagRow { Glyph = glyph, Brush = brush, Title = i.Title, Detail = i.Detail };
    }

    private async void Repair_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // keep the dialog open to show the result
        if (_report == null) return;
        var deferral = args.GetDeferral();
        try
        {
            var done = Diagnostics.Repair(_profile, _report);
            App.Instance.Service.Forget(_profile);
            await App.Instance.Service.ConnectNowAsync(_profile);
            var (state, msg) = App.Instance.Service.GetState(_profile.Id);
            await RunAsync();
            var summary = done.Count == 0 ? Strings.T("diag.repairNothing") : string.Join("\n", done);
            SummaryBar.Message = $"{summary}\n{Strings.StateName(state)}: {msg}";
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Copy_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_report == null) return;
        var data = new DataPackage();
        data.SetText(_report.ToText(_profile));
        Clipboard.SetContent(data);
        SummaryBar.Severity = InfoBarSeverity.Informational;
        SummaryBar.Message = Strings.T("diag.copied");
    }
}
