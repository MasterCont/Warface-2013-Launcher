using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Достижения игрока (администратор): назначить, забрать, задать прогресс — по одному, выделенным или все.</summary>
public partial class AchievementsPage : UserControl
{
    private static readonly (bool? Completed, string Title)[] States =
    [
        (null, "Все"),
        (true, "Выполненные"),
        (false, "Невыполненные"),
    ];

    private readonly ApiClient _api;
    private readonly string _nick;
    private PlayerAchievement[] _all = [];

    public AchievementsPage(ApiClient api, string nick)
    {
        InitializeComponent();
        _api = api;
        _nick = nick;
        BannerBox.ItemsSource = AchievementNames.Banners.Select(b => b.Title).ToList();
        BannerBox.SelectedIndex = 0;
        StateBox.ItemsSource = States.Select(s => s.Title).ToList();
        StateBox.SelectedIndex = 0;
        Loaded += async (_, _) => await Run(LoadAsync);
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
            Status(error.Message, error: true);
        }
        finally
        {
            Cursor = null;
        }
    }

    private void Status(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(error ? "ErrorBrush" : "YellowBrush");
    }

    private bool Confirm(string question) =>
        MessageBox.Show(App.Shell, question, "Подтверждение", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    private async Task LoadAsync()
    {
        var selected = AchievementsList.SelectedItems.Cast<PlayerAchievement>().Select(a => a.Id).ToHashSet();
        _all = await _api.GetAchievementsAsync(_nick);
        ApplyFilter();
        foreach (var item in AchievementsList.Items.Cast<PlayerAchievement>().Where(a => selected.Contains(a.Id)))
        {
            AchievementsList.SelectedItems.Add(item);
        }
    }

    private void ApplyFilter()
    {
        var text = FilterBox.Text.Trim();
        var banner = AchievementNames.Banners[Math.Max(0, BannerBox.SelectedIndex)].Id;
        var state = States[Math.Max(0, StateBox.SelectedIndex)].Completed;
        var shown = _all
            .Where(a => banner == null || a.BannerType == banner)
            .Where(a => state == null || a.Completed == state)
            .Where(a => text == "" || a.Id.ToString() == text
                        || a.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || a.Kind.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || AchievementNames.Kind(a.Kind).Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        AchievementsList.ItemsSource = shown;
        CountText.Text = $"Выполнено {_all.Count(a => a.Completed)} из {_all.Length}" + (shown.Length != _all.Length ? $" · показано {shown.Length}" : "");
    }

    private void OnFilterText(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) ApplyFilter();
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) ApplyFilter();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await Run(LoadAsync);

    private PlayerAchievement[] Selected => AchievementsList.SelectedItems.Cast<PlayerAchievement>().ToArray();

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        var selected = Selected;
        SelectionActions.IsEnabled = selected.Length > 0;
        if (selected.Length == 0)
        {
            SelectedText.Text = "Выберите достижения в списке.";
            return;
        }
        if (selected.Length == 1)
        {
            var a = selected[0];
            SelectedText.Text = $"{a.Title}\n{a.Details}";
            ProgressPanel.Visibility = a.Amount > 1 ? Visibility.Visible : Visibility.Collapsed;
            ProgressLabel.Text = $"Прогресс (0 … {a.Amount:N0})";
            ProgressBox.Text = a.Progress.ToString();
            return;
        }
        SelectedText.Text = $"Выбрано: {selected.Length}, из них выполнено {selected.Count(a => a.Completed)}";
        ProgressPanel.Visibility = Visibility.Collapsed;
    }

    private async Task SendAsync(object change)
    {
        Status(await _api.SetAchievementsAsync(_nick, change));
        await LoadAsync();
    }

    private async void OnGive(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var selected = Selected;
        if (selected.Length == 0) return;
        if (selected.Length > 1 && !Confirm($"Назначить {_nick} выбранные достижения ({selected.Length})?")) return;
        await SendAsync(new { achievements = selected.Select(a => new { id = a.Id, completed = true }) });
    });

    private async void OnTake(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var selected = Selected;
        if (selected.Length == 0) return;
        if (!Confirm($"Забрать у {_nick} выбранные достижения ({selected.Length})? Прогресс обнулится.")) return;
        await SendAsync(new { achievements = selected.Select(a => new { id = a.Id, completed = false }) });
    });

    private async void OnSetProgress(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Selected is not [var a]) return;
        if (!long.TryParse(ProgressBox.Text.Trim().Replace(" ", ""), out var progress) || progress < 0 || progress > a.Amount)
        {
            Status($"Прогресс — целое число от 0 до {a.Amount:N0}", error: true);
            return;
        }
        await SendAsync(new { achievements = new[] { new { id = a.Id, progress } } });
    });

    private async void OnGiveAll(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!Confirm($"Назначить {_nick} все достижения ({_all.Length})?")) return;
        await SendAsync(new { all = true });
    });

    private async void OnTakeAll(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!Confirm($"Забрать у {_nick} ВСЕ достижения? Весь прогресс обнулится.")) return;
        await SendAsync(new { all = false });
    });
}
