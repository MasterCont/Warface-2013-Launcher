using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SubootLauncher.Services;
using SubootLauncher.Views;

namespace SubootLauncher.Views;

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
        _pingTimer.Tick += async (_, _) => await PingServersAsync();
        _serversTimer.Tick += async (_, _) => await LoadServersAsync(quiet: true);
        // Пока открыта другая страница, главная выгружена — следим за игрой, только когда она видна
        Loaded += async (_, _) =>
        {
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

    private void ShowAccount()
    {
        App.Shell.SetAccount(_me);
        if (_me.Profile is { } profile)
        {
            ProfilePanel.Visibility = Visibility.Visible;
            NoProfileText.Visibility = Visibility.Collapsed;
            RankText.Text = profile.Rank.ToString();
            NickText.Text = profile.Nick;
            ExperienceText.Text = Loc.T("Main.Experience", profile.Rank, profile.Experience) + (profile.Clan != null ? Loc.T("Main.Clan", profile.Clan) : "");
            MoneyText.Text = Loc.T("Main.Money", profile.Money.Game, profile.Money.Cry, profile.Money.Crown);
        }
        else
        {
            ProfilePanel.Visibility = Visibility.Collapsed;
            NoProfileText.Visibility = Visibility.Visible;
        }
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
            _me = await App.Api.GetMeAsync(_session);
            ShowAccount();
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
