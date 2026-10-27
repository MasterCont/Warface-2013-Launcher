using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SubootLauncher.Services;
using SubootLauncher.Views;

namespace SubootLauncher;

/// <summary>Строка списка серверов: данные API + цвет точки и подпись.</summary>
public record ServerRow(ServerInfo Server)
{
    public string Name => Server.Name;

    public string Details => Server.State switch
    {
        "online" => "работает",
        "degraded" => "работает частично",
        _ => "недоступен",
    } + (Server.Online is { } online ? $" · игроков: {online}" : "") + $"\n{Server.Host}";

    public Brush StateBrush => (Brush)Application.Current.Resources[Server.State switch
    {
        "online" => "OnlineBrush",
        "degraded" => "DegradedBrush",
        _ => "OfflineBrush",
    }];
}

/// <summary>Главное окно: персонаж, серверы, папка игры и запуск.</summary>
public partial class MainWindow : Window
{
    private readonly string _session;
    private AccountInfo _me;

    public bool LoggedOut { get; private set; }

    public MainWindow(string session, AccountInfo me)
    {
        InitializeComponent();
        _session = session;
        _me = me;

        App.Settings.GamePath ??= GameLauncher.Detect();
        ShowAccount();
        ShowGamePath();
        Loaded += async (_, _) => await LoadServersAsync();
    }

    private void ShowAccount()
    {
        AccountLogin.Text = _me.Login;
        AccountRole.Text = _me.Role switch { "admin" => "администратор", "moderator" => "модератор", _ => $"uid {_me.Uid}" };
        StaffButton.Visibility = _me.IsStaff ? Visibility.Visible : Visibility.Collapsed;

        if (_me.Profile is { } profile)
        {
            ProfilePanel.Visibility = Visibility.Visible;
            NoProfileText.Visibility = Visibility.Collapsed;
            RankText.Text = profile.Rank.ToString();
            NickText.Text = profile.Nick;
            ExperienceText.Text = $"Звание {profile.Rank} · опыт {profile.Experience:N0}" + (profile.Clan != null ? $" · клан {profile.Clan}" : "");
            MoneyText.Text = $"Варбаксы {profile.Money.Game:N0}    Кредиты {profile.Money.Cry:N0}    Короны {profile.Money.Crown:N0}";
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
        GamePathText.Text = GameLauncher.IsGameFolder(path) ? path! : "не выбрана — укажите папку с Bin32Release\\Game.exe";
    }

    private async Task LoadServersAsync()
    {
        ServersError.Visibility = Visibility.Collapsed;
        try
        {
            var rows = (await App.Api.GetServersAsync()).Select(s => new ServerRow(s)).ToList();
            ServerList.ItemsSource = rows;
            ServerList.SelectedItem = rows.FirstOrDefault(r => r.Server.Id == App.Settings.ServerId)
                                      ?? rows.FirstOrDefault(r => r.Server.State != "offline")
                                      ?? rows.FirstOrDefault();
        }
        catch (ApiException error)
        {
            ServersError.Text = error.Message;
            ServersError.Visibility = Visibility.Visible;
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
        var dialog = new OpenFolderDialog { Title = "Папка игры (в ней Bin32Release, Game, Engine)" };
        if (dialog.ShowDialog(this) != true) return;
        if (!GameLauncher.IsGameFolder(dialog.FolderName))
        {
            StatusText.Text = "В этой папке нет Bin32Release\\Game.exe";
            return;
        }
        App.Settings.GamePath = dialog.FolderName;
        App.Settings.Save();
        StatusText.Text = "";
        ShowGamePath();
    }

    private async void OnPlay(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "";
        var path = App.Settings.GamePath;
        if (!GameLauncher.IsGameFolder(path))
        {
            StatusText.Text = "Сначала выберите папку игры";
            return;
        }
        var server = (ServerList.SelectedItem as ServerRow)?.Server;
        if (server?.State == "offline")
        {
            StatusText.Text = $"Сервер {server.Name} сейчас недоступен";
            return;
        }

        PlayButton.IsEnabled = false;
        try
        {
            var session = await App.Api.LoginBySessionAsync(_session);
            GameLauncher.Start(path!, session, server);
            StatusText.Text = "Игра запускается…";
            await Task.Delay(5000);
            StatusText.Text = "";
        }
        catch (ApiException error) when (error.Code == 401)
        {
            Settings.ForgetSession();
            MessageBox.Show(this, error.Message, "Вход", MessageBoxButton.OK, MessageBoxImage.Information);
            OnLogout(sender, e);
        }
        catch (Exception error)
        {
            StatusText.Text = error is ApiException ? error.Message : "Не удалось запустить игру: " + error.Message;
        }
        finally
        {
            PlayButton.IsEnabled = true;
        }
    }

    /// <summary>Переключение оформления: сохраняем выбор и перезапускаем лаунчер (сессия сохранена — вход не нужен).</summary>
    private void OnTheme(object sender, RoutedEventArgs e)
    {
        var next = App.Settings.Theme == "game" ? "classic" : "game";
        var answer = MessageBox.Show(this, $"Переключить оформление на «{App.Themes[next].Name}»? Лаунчер перезапустится.",
            "Оформление", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        App.Settings.Theme = next;
        App.Settings.Save();
        System.Diagnostics.Process.Start(Environment.ProcessPath!);
        Application.Current.Shutdown();
    }

    private void OnChangePassword(object sender, RoutedEventArgs e)
    {
        new ChangePasswordWindow(_me.Login) { Owner = this }.ShowDialog();
    }

    private void OnStaff(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, "Панель модерации появится в следующей версии лаунчера.", "Модерация");
    }

    private async void OnLogout(object sender, RoutedEventArgs e)
    {
        try { await App.Api.LogoutAsync(_session); } catch (ApiException) { /* сессия могла уже истечь */ }
        Settings.ForgetSession();
        LoggedOut = true;
        App.ShowLogin();
        Close();
    }
}
