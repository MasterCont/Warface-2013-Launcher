using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Строка состояния службы: точка цвета состояния, имя и состояние.</summary>
public record ServiceRow(string Name, string State)
{
    public Brush Brush => (Brush)Application.Current.Resources[State == "active" ? "OnlineBrush" : State is "activating" or "reloading" ? "DegradedBrush" : "OfflineBrush"];
}

/// <summary>Обслуживание сервера (администратор): состояние служб, пересборка кэшей, резервная копия.</summary>
public partial class ServerPanel : UserControl
{
    private static readonly Dictionary<string, string> CacheTitles = new()
    {
        ["shop"] = "магазин",
        ["missions_pve"] = "PvE-миссии",
        ["clan_list"] = "кланы",
    };

    private ApiClient _api = null!;
    private Action<string, bool> _status = (_, _) => { };
    private bool _loaded;

    // Пока вкладка открыта — список сбросов с обратным отсчётом обновляется сам
    private readonly System.Windows.Threading.DispatcherTimer _resetsTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    /// <summary>Число запланированных сбросов — для отметки на вкладке.</summary>
    public event Action<int>? PendingChanged;

    public ServerPanel()
    {
        InitializeComponent();
        _resetsTimer.Tick += async (_, _) => await Run(LoadResetsAsync);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _loaded) _resetsTimer.Start();
            else _resetsTimer.Stop();
        };
    }

    public void Init(ApiClient api, Action<string, bool> status, bool isOwner)
    {
        _api = api;
        _status = status;
        SetOwner(isOwner);
    }

    /// <summary>«Сбросить все аккаунты» — только главному администратору.</summary>
    public void SetOwner(bool isOwner) => ResetAllButton.Visibility = isOwner ? Visibility.Visible : Visibility.Collapsed;

    public async Task LoadPendingCountAsync()
    {
        var data = await _api.GetResetsAsync();
        PendingChanged?.Invoke(data.Resets.Count(r => r.IsPending || r.Status == "running"));
    }

    private async Task LoadResetsAsync()
    {
        var data = await _api.GetResetsAsync();
        ResetsList.ItemsSource = data.Resets;
        ResetsHint.Text = $"Запланированные сбросы выполняются через {data.DelayMinutes} мин.; до этого любой администратор может их отменить. "
                          + "Перед сбросом сервер делает резервную копию базы.";
        PendingChanged?.Invoke(data.Resets.Count(r => r.IsPending || r.Status == "running"));
    }

    private async void OnCancelReset(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var id = (long)((Button)sender).Tag;
        _status(await _api.CancelResetAsync(id), false);
        await LoadResetsAsync();
    });

    private async void OnResetAll(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        const string phrase = "СБРОСИТЬ ВСЕХ";
        if (MessageBox.Show(App.Shell,
                "Сбросить прогресс ВСЕХ игроков сервера (вайп)?\n\nУ каждого обнулятся опыт, деньги, предметы, достижения, статистика и прогресс. "
                + "Останутся логины, ники, друзья и кланы.",
                "Вайп — шаг 1 из 3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (MessageBox.Show(App.Shell,
                "Это затронет всех игроков, в том числе администраторов. Вернуть прогресс можно будет только из резервной копии базы.\n\nТочно продолжить?",
                "Вайп — шаг 2 из 3", MessageBoxButton.YesNo, MessageBoxImage.Stop, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!TypeConfirmWindow.Ask(App.Shell, "Вайп — шаг 3 из 3",
                "Сброс выполнится через 5 минут, игроки получат предупреждение. До этого его может отменить любой администратор.",
                phrase, "Запланировать вайп")) return;
        var reset = await _api.ScheduleResetAllAsync(phrase);
        _status($"Вайп запланирован (#{reset.Id}) на {reset.ExecuteAt.ToLocalTime():HH:mm:ss}", true);
        await LoadResetsAsync();
    });

    private async Task Run(Func<Task> action)
    {
        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            await action();
        }
        catch (ApiException error)
        {
            _status(error.Message, true);
        }
        finally
        {
            Cursor = null;
        }
    }

    public Task LoadOnceAsync() => _loaded ? Task.CompletedTask : LoadAsync();

    private async Task LoadAsync()
    {
        var status = await _api.GetStatusAsync();
        _loaded = true;
        OnlineText.Text = $"В сети: {status.Online?.ToString() ?? "?"}";
        var rows = status.Components.Select(c => new ServiceRow(c.Name, c.State))
            .Concat(status.Channels.Select(c => new ServiceRow($"канал {c.Name} (ранг {c.MinRank}–{c.MaxRank})", c.State)))
            .Concat((status.Dedicated ?? []).Select(d => new ServiceRow($"dedicated {d.Name}", d.State)));
        ServicesList.ItemsSource = rows.ToList();
        await LoadResetsAsync();
        _resetsTimer.Start();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await Run(LoadAsync);

    private async void OnRebuild(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var tag = (string)((Button)sender).Tag;
        var modules = tag == "all" ? CacheTitles.Keys.ToArray() : [tag];
        if (modules.Contains("missions_pve") && MessageBox.Show(App.Shell,
                "Пересборка PvE-миссий сменит текущие миссии у всех игроков и обнулит таблицу рекордов прохождений. Продолжить?",
                "Подтверждение", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        CacheResult.Text = "Пересобираем: " + string.Join(", ", modules.Select(m => CacheTitles[m])) + "…";
        var result = await _api.RebuildCachesAsync(modules);
        CacheResult.Text = string.Join("\n", result.Select(r => $"{CacheTitles.GetValueOrDefault(r.Key, r.Key)}: {(r.Value == "updated" ? "обновлён" : "ошибка")}"));
        _status("Кэши пересобраны — игровые каналы подхватят их за ~10 секунд", false);
    });

    private async void OnBackup(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        _status("Делаем резервную копию…", false);
        _status(await _api.BackupAsync(), false);
    });
}
