using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SubootLauncher.Services;
using SubootLauncher.Views;

namespace SubootLauncher.Views;

/// <summary>Ячейка награды за вход: день, кредиты, короны, предмет и состояние (claimed, today, next, future).</summary>
public record RewardCell(int Day, RewardDay Reward, string State, string Mark)
{
    public string DayText => Loc.T("Rewards.Day", Day);
    public string CryText => Reward.Cry > 0 ? Loc.T("Rewards.Cry", Reward.Cry) : "";
    public string CrownText => Loc.T("Rewards.Crown", Reward.Crown);
    public Visibility CrownVisibility => Reward.Crown > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string ItemText => Reward.Item?.Caption ?? "";
    public string Tooltip => string.Join("\n", new[] { CryText, Reward.Crown > 0 ? CrownText : null, Reward.Item?.Caption }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>Строка списка серверов: данные API, цвет точки, подпись и живой пинг (обновляются без пересоздания строки).</summary>
public class ServerRow(ServerInfo server) : System.ComponentModel.INotifyPropertyChanged
{
    private long? _ping;
    private bool _pinged;

    public ServerInfo Server { get; private set; } = server;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void Changed(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    /// <summary>Новые данные с сервера (онлайн, состояние) — строка и выделение в списке остаются.</summary>
    public void Update(ServerInfo server)
    {
        Server = server;
        Changed(nameof(Name), nameof(Details), nameof(StateBrush));
    }

    public void SetPing(long? ms)
    {
        _ping = ms;
        _pinged = true;
        Changed(nameof(PingText), nameof(PingBrush));
    }

    public string PingText => !_pinged ? "…" : _ping is { } ms ? Loc.T("Server.Ping", ms) : Loc.T("Server.PingFail");

    public Brush PingBrush => (Brush)Application.Current.Resources[!_pinged ? "MutedBrush"
        : _ping is not { } ms ? "OfflineBrush"
        : ms < 60 ? "OnlineBrush"
        : ms < 120 ? "DegradedBrush"
        : "OfflineBrush"];

    public string Name => Server.Name;

    public string Details => Server.State switch
    {
        "online" => Loc.T("Server.Online"),
        "degraded" => Loc.T("Server.Degraded"),
        _ => Loc.T("Server.Offline"),
    } + (Server.Online is { } online ? Loc.T("Server.Players", online) : "") + $"\n{Server.Host}";

    public Brush StateBrush => (Brush)Application.Current.Resources[Server.State switch
    {
        "online" => "OnlineBrush",
        "degraded" => "DegradedBrush",
        _ => "OfflineBrush",
    }];
}

/// <summary>Главное окно: персонаж, серверы, папка игры и запуск.</summary>
public partial class HomePage : UserControl
{
    private readonly string _session;
    private AccountInfo _me;

    // Пока Game.exe из папки игры работает (запущен нами или через Start.bat), «Играть» заблокирована
    private readonly DispatcherTimer _gameWatch = new() { Interval = TimeSpan.FromSeconds(2) };

    // Живые данные серверов, пока главная на экране: пинг раз в 5 с, онлайн и состояние — раз в 30 с
    private readonly DispatcherTimer _pingTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _serversTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private List<ServerRow> _servers = [];
    private bool _pinging;
    private bool _starting;

    public HomePage(string session, AccountInfo me)
    {
        InitializeComponent();
        _session = session;
        _me = me;

        // Лаунчер лежит в папке клиента — играем этим клиентом сразу, без выбора папки
        var detected = GameLauncher.Detect();
        if (detected != null && !string.Equals(detected, App.Settings.GamePath, StringComparison.OrdinalIgnoreCase))
        {
            App.Settings.GamePath = detected;
            App.Settings.Save();
        }
        ShowAccount();
        ShowGamePath();
        _gameWatch.Tick += (_, _) => UpdatePlayButton();
        _tween.Tick += OnTween;
        _pingTimer.Tick += async (_, _) => await PingServersAsync();
        _serversTimer.Tick += async (_, _) => await LoadServersAsync(quiet: true);
        // Пока открыта другая страница, главная выгружена — следим за игрой, только когда она видна
        Loaded += async (_, _) =>
        {
            // Вернулись на главную — показать то, что лаунчер узнал, пока она была скрыта
            if (App.Shell.Account is { } account && !ReferenceEquals(account, _me)) UpdateAccount(account);
            _gameWatch.Start();
            UpdatePlayButton();
            await LoadServersAsync();
        };
        Unloaded += (_, _) =>
        {
            _gameWatch.Stop();
            _pingTimer.Stop();
            _serversTimer.Stop();
        };
    }

    private void UpdatePlayButton()
    {
        var running = GameLauncher.IsRunning(App.Settings.GamePath);
        PlayButton.IsEnabled = !running && !_starting;
        PlayButton.Content = Loc.T(running ? "Main.Running" : "Main.Play");
        if (running && StatusText.Text == Loc.T("Main.Starting")) StatusText.Text = "";
    }

    // --- Персонаж: живые данные с анимацией ---

    private ProfileInfo? _shown;          // что сейчас на экране (после анимации)
    private readonly DispatcherTimer _tween = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _tweenStart;
    private (double Exp, double Game, double Cry, double Crown) _from, _to;
    private ProfileInfo? _tweenTarget;
    private const double TweenMs = 900;

    private void ShowAccount()
    {
        App.Shell.SetAccount(_me);
        UpdateAccount(_me, animate: false);
    }

    /// <summary>Новые данные аккаунта: числа «докручиваются», изменения подсвечиваются.</summary>
    public void UpdateAccount(AccountInfo me, bool animate = true)
    {
        _me = me;
        if (me.Profile is not { } profile)
        {
            ProfilePanel.Visibility = Visibility.Collapsed;
            NoProfileText.Visibility = Visibility.Visible;
            _shown = null;
            return;
        }
        ProfilePanel.Visibility = Visibility.Visible;
        NoProfileText.Visibility = Visibility.Collapsed;

        var old = _shown;
        if (!animate || old == null)
        {
            _tween.Stop();
            _shown = profile;
            Render(profile, profile.Experience, profile.Money.Game, profile.Money.Cry, profile.Money.Crown);
            return;
        }

        // Значки изменений
        AddDelta(profile.Experience - old.Experience, "Delta.Exp");
        AddDelta(profile.Money.Game - old.Money.Game, "Delta.Game");
        AddDelta(profile.Money.Cry - old.Money.Cry, "Delta.Cry");
        AddDelta(profile.Money.Crown - old.Money.Crown, "Delta.Crown");
        if (profile.Rank != old.Rank)
        {
            AddBadge(Loc.T("Delta.Rank", profile.Rank), "YellowBrush");
            Pulse();
        }
        if (profile.Nick != old.Nick) AddBadge(Loc.T("Delta.Nick", profile.Nick), "YellowBrush");
        if (profile.Clan != old.Clan) AddBadge(profile.Clan != null ? Loc.T("Delta.Clan", profile.Clan) : Loc.T("Delta.NoClan"), "YellowBrush");

        // Плавный переход чисел от показанных к новым
        _from = (old.Experience, old.Money.Game, old.Money.Cry, old.Money.Crown);
        _to = (profile.Experience, profile.Money.Game, profile.Money.Cry, profile.Money.Crown);
        _tweenTarget = profile;
        _shown = profile;
        if (_from == _to)
        {
            Render(profile, profile.Experience, profile.Money.Game, profile.Money.Cry, profile.Money.Crown);
            return;
        }
        _tweenStart = DateTime.UtcNow;
        _tween.Start();
    }

    private void OnTween(object? sender, EventArgs e)
    {
        if (_tweenTarget == null) return;
        var t = Math.Min(1, (DateTime.UtcNow - _tweenStart).TotalMilliseconds / TweenMs);
        var k = 1 - Math.Pow(1 - t, 3); // плавное замедление в конце
        double Lerp(double a, double b) => a + (b - a) * k;
        Render(_tweenTarget, (long)Lerp(_from.Exp, _to.Exp), (long)Lerp(_from.Game, _to.Game), (long)Lerp(_from.Cry, _to.Cry), (long)Lerp(_from.Crown, _to.Crown));
        if (t >= 1) _tween.Stop();
    }

    private void Render(ProfileInfo profile, long experience, long game, long cry, long crown)
    {
        RankText.Text = profile.Rank.ToString();
        NickText.Text = profile.Nick;
        ClanText.Text = profile.Clan != null ? Loc.T("Main.ClanName", profile.Clan) : "";
        ClanText.Visibility = profile.Clan != null ? Visibility.Visible : Visibility.Collapsed;
        ExperienceText.Text = Loc.T("Main.Experience", profile.Rank, experience);
        PlayTimeText.Text = Loc.T("Main.PlayTime", profile.PlaySeconds / 3600, profile.PlaySeconds % 3600 / 60);
        RenderRewards(profile.LoginBonus);
        MoneyText.Text = Loc.T("Main.Money", game, cry, crown);

        if (profile.RankProgress is { } progress)
        {
            if (progress.Next is { } next && next > progress.Current)
            {
                ProgressScale.ScaleX = Math.Clamp((double)(experience - progress.Current) / (next - progress.Current), 0, 1);
                ProgressText.Text = Loc.T("Main.ToNextRank", profile.Rank + 1, Math.Max(0, experience - progress.Current), next - progress.Current);
            }
            else
            {
                ProgressScale.ScaleX = 1;
                ProgressText.Text = Loc.T("Main.MaxRank");
            }
        }
        else
        {
            ProgressScale.ScaleX = 0;
            ProgressText.Text = "";
        }
    }

    /// <summary>Сетка наград за вход: полученные — бледные, сегодняшняя — зелёная, следующая — жёлтая.</summary>
    private void RenderRewards(LoginBonusInfo? bonus)
    {
        if (bonus is not { Enabled: true } || bonus.Days.Length == 0)
        {
            RewardsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        RewardsPanel.Visibility = Visibility.Visible;
        RewardsList.ItemsSource = bonus.Days.Select((reward, index) =>
        {
            if (bonus.ClaimedToday && index == bonus.Current) return new RewardCell(index + 1, reward, "today", Loc.T("Rewards.Today"));
            if (index < bonus.Streak) return new RewardCell(index + 1, reward, "claimed", "✓");
            if (index == bonus.Next) return new RewardCell(index + 1, reward, "next", Loc.T(bonus.ClaimedToday ? "Rewards.Tomorrow" : "Rewards.NextLogin"));
            return new RewardCell(index + 1, reward, "future", "");
        }).ToList();
        RewardsStreakText.Text = Loc.T("Rewards.Streak", bonus.Streak, bonus.Days.Length);
    }

    private void AddDelta(long delta, string key)
    {
        if (delta == 0) return;
        var text = (delta > 0 ? "+" : "−") + Loc.T(key, Math.Abs(delta).ToString("N0"));
        AddBadge(text, delta > 0 ? "OnlineBrush" : "ErrorBrush");
    }

    // Значок изменения: появляется и через 6 секунд плавно исчезает
    private void AddBadge(string text, string brush)
    {
        var badge = new TextBlock
        {
            Text = text,
            Foreground = (Brush)FindResource(brush),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 12, 0),
            Opacity = 0,
        };
        DeltaPanel.Children.Add(badge);
        var appear = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(800)) { BeginTime = TimeSpan.FromSeconds(6) };
        var story = new System.Windows.Media.Animation.Storyboard();
        System.Windows.Media.Animation.Storyboard.SetTarget(appear, badge);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(appear, new PropertyPath(OpacityProperty));
        System.Windows.Media.Animation.Storyboard.SetTarget(fade, badge);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
        story.Children.Add(appear);
        story.Children.Add(fade);
        story.Completed += (_, _) => DeltaPanel.Children.Remove(badge);
        story.Begin();
    }

    // Новое звание — значок «подпрыгивает»
    private void Pulse()
    {
        var grow = new System.Windows.Media.Animation.DoubleAnimation(1, 1.25, TimeSpan.FromMilliseconds(180)) { AutoReverse = true, RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2) };
        RankScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        RankScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private void ShowGamePath()
    {
        var path = App.Settings.GamePath;
        GamePathText.Text = GameLauncher.IsGameFolder(path) ? path! : Loc.T("Main.NoFolder");
    }

    /// <summary>Список серверов. quiet — фоновое обновление: строки обновляются на месте, ошибка не показывается.</summary>
    private async Task LoadServersAsync(bool quiet = false)
    {
        if (!quiet) ServersError.Visibility = Visibility.Collapsed;
        try
        {
            var servers = await App.Api.GetServersAsync();
            var same = servers.Length == _servers.Count && servers.All(s => _servers.Any(r => r.Server.Id == s.Id));
            if (same)
            {
                foreach (var server in servers) _servers.First(r => r.Server.Id == server.Id).Update(server);
            }
            else
            {
                _servers = servers.Select(s => new ServerRow(s)).ToList();
                ServerList.ItemsSource = _servers;
                ServerList.SelectedItem = _servers.FirstOrDefault(r => r.Server.Id == App.Settings.ServerId)
                                          ?? _servers.FirstOrDefault(r => r.Server.State != "offline")
                                          ?? _servers.FirstOrDefault();
                _ = PingServersAsync();
            }
            ServersError.Visibility = Visibility.Collapsed;
            _pingTimer.Start();
            _serversTimer.Start();
        }
        catch (ApiException error)
        {
            if (quiet) return;
            ServersError.Text = error.Message;
            ServersError.Visibility = Visibility.Visible;
        }
    }

    private async Task PingServersAsync()
    {
        if (_pinging) return;
        _pinging = true;
        try
        {
            var rows = _servers.ToList();
            var results = await Task.WhenAll(rows.Select(r => PingService.MeasureAsync(r.Server.Host, r.Server.Port)));
            for (var i = 0; i < rows.Count; i++) rows[i].SetPing(results[i]);
        }
        finally
        {
            _pinging = false;
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        await LoadServersAsync();
        try
        {
            var me = await App.Api.GetMeAsync(_session);
            App.Shell.SetAccount(me);
            UpdateAccount(me);
        }
        catch (ApiException)
        {
            // Аккаунт обновим в следующий раз
        }
    }

    private void OnServerSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ServerList.SelectedItem is ServerRow row)
        {
            App.Settings.ServerId = row.Server.Id;
            App.Settings.Save();
        }
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Main.FolderDialog") };
        if (dialog.ShowDialog(App.Shell) != true) return;
        if (!GameLauncher.IsGameFolder(dialog.FolderName))
        {
            StatusText.Text = Loc.T("Main.NotGameFolder");
            return;
        }
        App.Settings.GamePath = dialog.FolderName;
        App.Settings.Save();
        StatusText.Text = "";
        ShowGamePath();
        UpdatePlayButton();
    }

    private async void OnPlay(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "";
        var path = App.Settings.GamePath;
        if (!GameLauncher.IsGameFolder(path))
        {
            StatusText.Text = Loc.T("Main.ChooseFolderFirst");
            return;
        }
        if (GameLauncher.IsRunning(path))
        {
            StatusText.Text = Loc.T("Main.AlreadyRunning");
            UpdatePlayButton();
            return;
        }
        var server = (ServerList.SelectedItem as ServerRow)?.Server;
        if (server?.State == "offline")
        {
            StatusText.Text = Loc.T("Main.ServerOffline", server.Name);
            return;
        }

        // Блокируем сразу: пока получаем токен и Game.exe не появился, второй щелчок не запустит вторую копию
        _starting = true;
        UpdatePlayButton();
        try
        {
            var session = await App.Api.LoginBySessionAsync(_session);
            GameLauncher.Start(path!, session, server);
            StatusText.Text = Loc.T("Main.Starting");
            await Task.Delay(3000);
        }
        catch (ApiException error) when (error.Code == 401)
        {
            AccountStore.Remove(_me.Login);
            MessageBox.Show(App.Shell, error.Message, Loc.T("Main.LoginTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            App.ShowLogin();
        }
        catch (ApiException error) when (error.Code == 403)
        {
            // Пока лаунчер был открыт, аккаунт заблокировали — показываем страницу блокировки
            try
            {
                App.ShowMain(_session, await App.Api.GetMeAsync(_session));
            }
            catch (ApiException)
            {
                StatusText.Text = error.Message;
            }
        }
        catch (Exception error)
        {
            StatusText.Text = error is ApiException ? error.Message : Loc.T("Main.StartFailed", error.Message);
        }
        finally
        {
            _starting = false;
            UpdatePlayButton();
        }
    }
}
