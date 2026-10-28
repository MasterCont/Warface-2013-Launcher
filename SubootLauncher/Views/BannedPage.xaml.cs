using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Страница вместо главной, пока у аккаунта действует бан: причина, срок, «Проверить снова».</summary>
public partial class BannedPage : UserControl
{
    private readonly string _session;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };
    private AccountInfo _me;

    public BannedPage(string session, AccountInfo me)
    {
        InitializeComponent();
        _session = session;
        _me = me;
        Show(me);
        _clock.Tick += async (_, _) =>
        {
            if (_me.Ban?.Until is { } until && until <= DateTime.UtcNow)
            {
                await CheckAsync(); // срок вышел — сразу проверяем, не дожидаясь общей проверки
                return;
            }
            Show(_me);
        };
        Loaded += (_, _) => _clock.Start();
        Unloaded += (_, _) => _clock.Stop();
    }

    private void Show(AccountInfo me)
    {
        _me = me;
        var ban = me.Ban!;
        LoginText.Text = me.Profile is { } profile ? $"{me.Login} · {profile.Nick}" : me.Login;
        ReasonText.Text = Loc.T("Ban.Reason", ban.Reason);
        UntilText.Text = ban.Until is { } until
            ? Loc.T("Ban.Until", until.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), Left(until - DateTime.UtcNow))
            : Loc.T("Ban.Forever");
        if (ban.Until is { } ended && ended <= DateTime.UtcNow) UntilText.Text = Loc.T("Ban.Ending");
        SinceText.Text = Loc.T("Ban.Since", ban.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
    }

    private static string Left(TimeSpan span)
    {
        if (span.TotalDays >= 1) return Loc.T("Ban.Days", (int)span.TotalDays, span.Hours);
        if (span.TotalHours >= 1) return Loc.T("Ban.Hours", (int)span.TotalHours, span.Minutes);
        return Loc.T("Ban.Minutes", Math.Max(1, (int)Math.Ceiling(span.TotalMinutes)));
    }

    private async void OnCheck(object sender, RoutedEventArgs e) => await CheckAsync(showStill: true);

    private async Task CheckAsync(bool showStill = false)
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
            if (showStill) StatusText.Text = Loc.T("Ban.Still");
        }
        catch (ApiException error)
        {
            StatusText.Text = error.Message;
        }
    }

    private void OnAccounts(object sender, RoutedEventArgs e) => App.Shell.ShowAccounts();
}
