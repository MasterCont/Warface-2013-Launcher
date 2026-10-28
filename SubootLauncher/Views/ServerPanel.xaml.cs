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

    public ServerPanel()
    {
        InitializeComponent();
    }

    public void Init(ApiClient api, Action<string, bool> status)
    {
        _api = api;
        _status = status;
    }

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
