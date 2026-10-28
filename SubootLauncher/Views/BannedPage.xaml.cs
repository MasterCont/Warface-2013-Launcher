using System.Windows;
using System.Windows.Controls;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Страница вместо главной, пока у аккаунта действует бан: причина, срок, «Проверить снова».</summary>
public partial class BannedPage : UserControl
{
    private readonly string _session;

    public BannedPage(string session, AccountInfo me)
    {
        InitializeComponent();
        _session = session;
        Show(me);
    }

    private void Show(AccountInfo me)
    {
        var ban = me.Ban!;
        LoginText.Text = me.Profile is { } profile ? $"{me.Login} · {profile.Nick}" : me.Login;
        ReasonText.Text = Loc.T("Ban.Reason", ban.Reason);
        UntilText.Text = ban.Until is { } until
            ? Loc.T("Ban.Until", until.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), Left(until - DateTime.UtcNow))
            : Loc.T("Ban.Forever");
        SinceText.Text = Loc.T("Ban.Since", ban.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
    }

    private static string Left(TimeSpan span)
    {
        if (span.TotalDays >= 1) return Loc.T("Ban.Days", (int)span.TotalDays, span.Hours);
        if (span.TotalHours >= 1) return Loc.T("Ban.Hours", (int)span.TotalHours, span.Minutes);
        return Loc.T("Ban.Minutes", Math.Max(1, (int)Math.Ceiling(span.TotalMinutes)));
    }

    private async void OnCheck(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "";
        try
        {
            var me = await App.Api.GetMeAsync(_session);
            if (me.Ban == null)
            {
                App.ShowMain(_session, me); // бан закончился или снят — на главную
                return;
            }
            Show(me);
            StatusText.Text = Loc.T("Ban.Still");
        }
        catch (ApiException error)
        {
            StatusText.Text = error.Message;
        }
    }

    private void OnAccounts(object sender, RoutedEventArgs e) => App.Shell.ShowAccounts();
}
