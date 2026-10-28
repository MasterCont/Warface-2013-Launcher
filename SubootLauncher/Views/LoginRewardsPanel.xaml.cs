using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Редактируемый день серии наград за вход.</summary>
public class RewardDayEdit : INotifyPropertyChanged
{
    private string? _itemName;
    private string? _itemTitle;
    private int _kindIndex;

    public RewardDayEdit(int day, RewardDay reward)
    {
        Day = day;
        Cry = reward.Cry.ToString();
        Crown = reward.Crown.ToString();
        _itemName = reward.Item?.Name;
        _itemTitle = reward.Item?.Title;
        _kindIndex = reward.Item?.Days != null ? 0 : reward.Item?.Quantity != null ? 1 : 2;
        Amount = (reward.Item?.Days ?? reward.Item?.Quantity)?.ToString() ?? "";
    }

    public int Day { get; }
    public string DayText => $"День {Day}";
    public string Cry { get; set; }
    public string Crown { get; set; }
    public string Amount { get; set; }

    public string? ItemName => _itemName;
    public bool HasItem => _itemName != null;
    public string ItemText => _itemName == null ? "— без предмета —" : _itemTitle ?? _itemName;

    /// <summary>0 — на дней, 1 — штук, 2 — навсегда.</summary>
    public int KindIndex
    {
        get => _kindIndex;
        set
        {
            _kindIndex = value;
            Changed(nameof(KindIndex), nameof(AmountEnabled));
        }
    }

    public bool AmountEnabled => HasItem && _kindIndex != 2;

    public void SetItem(string? name, string? title)
    {
        _itemName = name;
        _itemTitle = title;
        Changed(nameof(ItemName), nameof(HasItem), nameof(ItemText), nameof(AmountEnabled));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Награды за ежедневный вход (администратор): включение и 14 дней.</summary>
public partial class LoginRewardsPanel : UserControl
{
    private ApiClient _api = null!;
    private Action<string, bool> _status = (_, _) => { };
    private bool _loaded;
    private List<RewardDayEdit> _days = [];

    public LoginRewardsPanel()
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
        var config = await _api.GetLoginRewardsAsync();
        _loaded = true;
        EnabledBox.IsChecked = config.Enabled;
        _days = config.Days.Select((day, index) => new RewardDayEdit(index + 1, day)).ToList();
        DaysList.ItemsSource = _days;
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await Run(LoadAsync);

    private void OnPickItem(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not RewardDayEdit day) return;
        App.Shell.Navigate(new ItemPickerPage(_api, null, item => day.SetItem(item.Name, item.Caption)), $"Награда за день {day.Day}: выбор предмета");
    }

    private void OnClearItem(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is RewardDayEdit day) day.SetItem(null, null);
    }

    private async void OnSave(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var days = new List<object>();
        foreach (var day in _days)
        {
            if (!long.TryParse(day.Cry.Trim(), out var cry) || cry < 0 || !long.TryParse(day.Crown.Trim(), out var crown) || crown < 0)
            {
                _status($"День {day.Day}: кредиты и короны — целые числа от 0", true);
                return;
            }
            object? item = null;
            if (day.ItemName != null)
            {
                if (day.KindIndex == 2)
                {
                    item = new { name = day.ItemName };
                }
                else
                {
                    if (!int.TryParse(day.Amount.Trim(), out var amount) || amount < 1)
                    {
                        _status($"День {day.Day}: укажите число {(day.KindIndex == 0 ? "дней" : "штук")}", true);
                        return;
                    }
                    item = day.KindIndex == 0 ? new { name = day.ItemName, days = amount } : new { name = day.ItemName, quantity = amount };
                }
            }
            days.Add(new { cry, crown, item });
        }
        var enabled = EnabledBox.IsChecked == true;
        if (MessageBox.Show(App.Shell, enabled ? "Сохранить награды? Игроки начнут получать их в течение минуты." : "Сохранить? Награды за вход будут ВЫКЛЮЧЕНЫ.",
                "Награды за вход", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await _api.SaveLoginRewardsAsync(new { enabled, days });
        _status("Награды за вход сохранены — masterserver применит их в течение минуты", false);
        await LoadAsync();
    });
}
