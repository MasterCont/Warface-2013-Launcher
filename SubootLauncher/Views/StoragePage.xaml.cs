using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Склад игрока (администратор): правка и удаление предметов, выдача новых.</summary>
public partial class StoragePage : UserControl
{
    private readonly ApiClient _api;
    private readonly string _nick;
    private StorageItem[] _items = [];
    private bool _loaded;

    public StoragePage(ApiClient api, string nick)
    {
        InitializeComponent();
        _api = api;
        _nick = nick;
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await Run(LoadAsync);
        };
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
        var selectedId = (ItemsList.SelectedItem as StorageItem)?.Id;
        _items = (await _api.GetStorageAsync(_nick))
            .OrderBy(i => i.Kind == "default")
            .ThenBy(i => i.Group)
            .ThenBy(i => i.Name)
            .ToArray();
        ApplyFilter();
        ItemsList.SelectedItem = _items.FirstOrDefault(i => i.Id == selectedId);
    }

    private void ApplyFilter()
    {
        var filter = FilterBox.Text.Trim();
        var shown = filter == "" ? _items : _items.Where(i => i.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        ItemsList.ItemsSource = shown;
        StorageTitle.Text = filter == "" ? $"Склад: {_items.Length}" : $"Склад: {shown.Length} из {_items.Length}";
    }

    private void OnFilter(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsLoaded) ApplyFilter();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await Run(LoadAsync);

    private void OnItemSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ItemsList.SelectedItem is not StorageItem item)
        {
            ItemActions.IsEnabled = false;
            SelectedText.Text = "Выберите предмет в списке.";
            return;
        }
        ItemActions.IsEnabled = item.Kind != "default";
        SelectedText.Text = $"#{item.Id} {item.Name}\n{ItemNames.Group(item.Group)} · {ItemNames.Category(item.Category)}\n{item.State}"
                            + (item.Kind == "default" ? "\nСтандартное снаряжение менять нельзя." : "");
        RepairButton.Visibility = item.Durability != null ? Visibility.Visible : Visibility.Collapsed;
        PermanentButton.Visibility = item.Kind == "rental" ? Visibility.Visible : Visibility.Collapsed;
        RentalPanel.Visibility = item.Kind == "rental" ? Visibility.Visible : Visibility.Collapsed;
        QuantityPanel.Visibility = item.Kind == "consumable" ? Visibility.Visible : Visibility.Collapsed;
        QuantityBox.Text = item.Quantity?.ToString() ?? "";
    }

    private async Task EditSelected(object change, string? question = null)
    {
        if (ItemsList.SelectedItem is not StorageItem item) return;
        if (question != null && !Confirm(question.Replace("{item}", item.Name))) return;
        Status(await _api.EditItemAsync(_nick, item.Id, change));
        await LoadAsync();
    }

    private async void OnRepair(object sender, RoutedEventArgs e) => await Run(() => EditSelected(new { repair = true }));

    private async void OnPermanent(object sender, RoutedEventArgs e) =>
        await Run(() => EditSelected(new { permanent = true }, "Сделать {item} постоянным (навсегда, полная прочность)?"));

    private async void OnSetHours(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!int.TryParse(HoursBox.Text.Trim(), out var hours) || hours < 0)
        {
            Status("Срок — целое число часов (0 — истечёт сразу)", error: true);
            return;
        }
        await EditSelected(new { hours });
    });

    private async void OnSetQuantity(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!int.TryParse(QuantityBox.Text.Trim(), out var quantity) || quantity < 1)
        {
            Status("Количество — целое число от 1", error: true);
            return;
        }
        await EditSelected(new { quantity });
    });

    private async void OnRemove(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (ItemsList.SelectedItem is not StorageItem item || !Confirm($"Удалить {item.Name} со склада {_nick}?")) return;
        Status(await _api.RemoveItemAsync(_nick, item.Id));
        await LoadAsync();
    });

    // --- Выдача ---

    private void OnPickItem(object sender, RoutedEventArgs e)
    {
        App.Shell.Navigate(new ItemPickerPage(_api, null, item => GiveNameBox.Text = item.Name), "Выбор предмета");
    }

    private void OnDaysFocus(object sender, RoutedEventArgs e) => GiveRental.IsChecked = true;

    private void OnCountFocus(object sender, RoutedEventArgs e) => GiveCount.IsChecked = true;

    private async void OnGive(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var name = GiveNameBox.Text.Trim();
        if (name == "")
        {
            Status("Выберите предмет", error: true);
            return;
        }
        object entry;
        string term;
        if (GiveRental.IsChecked == true)
        {
            if (!int.TryParse(GiveDaysBox.Text.Trim(), out var days) || days < 1)
            {
                Status("Срок — целое число дней от 1", error: true);
                return;
            }
            entry = new { name, days };
            term = $"на {days} дн.";
        }
        else if (GiveCount.IsChecked == true)
        {
            if (!int.TryParse(GiveCountBox.Text.Trim(), out var quantity) || quantity < 1)
            {
                Status("Количество — целое число от 1", error: true);
                return;
            }
            entry = new { name, quantity };
            term = $"{quantity} шт.";
        }
        else
        {
            entry = new { name };
            term = "навсегда";
        }
        if (!Confirm($"Выдать {_nick}: {name} {term}?")) return;
        Status(await _api.GiveAsync(_nick, new { items = new[] { entry } }));
    });
}
