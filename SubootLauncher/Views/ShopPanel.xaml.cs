using System.Windows;
using System.Windows.Controls;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>
/// Редактор магазина (администратор). Правки хранятся на сервере поверх исходного каталога:
/// «Исходные значения» отменяют их, «Снять с продажи» прячет предложение, не удаляя.
/// </summary>
public partial class ShopPanel : UserControl
{
    private static readonly (string? Id, string Title)[] States =
    [
        (null, "Все"),
        ("edited", "Изменённые"),
        ("hidden", "Снятые"),
        ("added", "Добавленные"),
        ("original", "Исходные"),
    ];

    private ApiClient _api = null!;
    private Action<string, bool> _status = (_, _) => { };
    private int _page = 1;
    private bool _loaded;
    private ShopOffer? _offer;
    private GameItem? _newItem; // новое предложение: выбранный предмет, ещё не сохранено

    public ShopPanel()
    {
        InitializeComponent();
        GroupBox.ItemsSource = ItemNames.Groups.Select(g => g.Title).ToList();
        GroupBox.SelectedIndex = 0;
        StateBox.ItemsSource = States.Select(s => s.Title).ToList();
        StateBox.SelectedIndex = 0;
        StatusBox.ItemsSource = new[] { "NORMAL", "HOT", "NEW" };
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

    private bool Confirm(string question) =>
        MessageBox.Show(Window.GetWindow(this)!, question, "Подтверждение", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    /// <summary>Загрузка при первом открытии вкладки.</summary>
    public Task LoadOnceAsync() => _loaded ? Task.CompletedTask : LoadAsync();

    private async Task LoadAsync()
    {
        var result = await _api.GetShopAsync(SearchBox.Text.Trim(),
            ItemNames.Groups[Math.Max(0, GroupBox.SelectedIndex)].Id,
            States[Math.Max(0, StateBox.SelectedIndex)].Id, _page);
        _loaded = true;
        _page = result.Page;
        OffersList.ItemsSource = result.Offers;
        PageText.Text = result.Total == 0 ? "ничего не найдено" : $"стр. {result.Page} из {result.Pages} · предложений {result.Total}";
        PrevPage.IsEnabled = result.Page > 1;
        NextPage.IsEnabled = result.Page < result.Pages;
        if (_offer != null && _newItem == null) OffersList.SelectedItem = result.Offers.FirstOrDefault(o => o.Id == _offer.Id);
    }

    private async Task Reload(int page)
    {
        _page = page;
        await Run(LoadAsync);
    }

    private async void OnSearch(object sender, RoutedEventArgs e) => await Reload(1);

    private async void OnSearchKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        await Reload(1);
    }

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded) await Reload(1);
    }

    private async void OnPrevPage(object sender, RoutedEventArgs e) => await Reload(_page - 1);

    private async void OnNextPage(object sender, RoutedEventArgs e) => await Reload(_page + 1);

    // --- Редактор ---

    private void OnOfferSelected(object sender, SelectionChangedEventArgs e)
    {
        if (OffersList.SelectedItem is not ShopOffer offer) return;
        _newItem = null;
        _offer = offer;
        ShowOffer(offer);
    }

    private void ShowOffer(ShopOffer offer)
    {
        Editor.IsEnabled = true;
        EditorTitle.Text = offer.Title;
        EditorInfo.Text = $"Предложение #{offer.Id} · {ItemNames.Group(offer.Group)}" + (offer.Classes != "" ? $" · классы {offer.Classes}" : "")
                          + offer.State switch
                          {
                              "edited" => "\nИзменено" + (offer.UpdatedBy != null ? $" ({offer.UpdatedBy})" : ""),
                              "hidden" => "\nСнято с продажи",
                              "added" => "\nДобавлено вручную" + (offer.UpdatedBy != null ? $" ({offer.UpdatedBy})" : ""),
                              _ => "\nИсходное, как в каталоге игры",
                          };
        GameMoneyBox.Text = offer.GameMoney.ToString();
        CryMoneyBox.Text = offer.CryMoney.ToString();
        CrownMoneyBox.Text = offer.CrownMoney.ToString();
        DiscountBox.Text = offer.Discount.ToString();
        RankBox.Text = offer.Rank.ToString();
        TermBox.Text = offer.ExpirationTime;
        QuantityBox.Text = offer.Quantity.ToString();
        DurabilityBox.Text = offer.DurabilityPoints.ToString();
        RepairBox.Text = offer.RepairCost.ToString();
        StatusBox.SelectedItem = offer.OfferStatus;
        RefundableBox.IsChecked = offer.Refundable;
        OriginalText.Text = offer.Original is { } o
            ? $"Исходно: {o.Price} · {o.Term} · прочность {o.DurabilityPoints} · ремонт {o.RepairCost} · {o.OfferStatus}"
            : "";
        SaveButton.Content = "Сохранить";
        HideButton.Visibility = offer.State is "hidden" ? Visibility.Collapsed : Visibility.Visible;
        HideButton.Content = offer.State == "added" ? "Удалить" : "Снять с продажи";
        ShowButton.Visibility = offer.State == "hidden" ? Visibility.Visible : Visibility.Collapsed;
        ResetButton.Visibility = offer.State is "edited" or "hidden" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNewOffer(object sender, RoutedEventArgs e)
    {
        var picker = new ItemPickerWindow(_api, ItemNames.Groups[Math.Max(0, GroupBox.SelectedIndex)].Id) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() != true || picker.Selected is not { } item) return;
        _newItem = item;
        _offer = null;
        OffersList.SelectedItem = null;
        Editor.IsEnabled = true;
        EditorTitle.Text = item.Title;
        EditorInfo.Text = $"Новое предложение · {item.GroupTitle}" + (item.Classes != "" ? $" · классы {item.Classes}" : "");
        var lasting = item.Group is "Weapons" or "Armor";
        GameMoneyBox.Text = "1000";
        CryMoneyBox.Text = "0";
        CrownMoneyBox.Text = "0";
        DiscountBox.Text = "0";
        RankBox.Text = "0";
        TermBox.Text = "";
        QuantityBox.Text = "0";
        DurabilityBox.Text = lasting ? "36000" : "0";
        RepairBox.Text = lasting ? "500" : "0";
        StatusBox.SelectedItem = "NEW";
        RefundableBox.IsChecked = false;
        OriginalText.Text = "";
        SaveButton.Content = "Добавить в магазин";
        HideButton.Visibility = Visibility.Collapsed;
        ShowButton.Visibility = Visibility.Collapsed;
        ResetButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Значения формы; null и сообщение об ошибке, если что-то введено неверно.</summary>
    private Dictionary<string, object>? ReadForm()
    {
        var numbers = new (string Field, string Title, TextBox Box)[]
        {
            ("game_money", "варбаксы", GameMoneyBox),
            ("cry_money", "кредиты", CryMoneyBox),
            ("crown_money", "короны", CrownMoneyBox),
            ("discount", "скидка", DiscountBox),
            ("rank", "звание", RankBox),
            ("quantity", "количество", QuantityBox),
            ("durability_points", "прочность", DurabilityBox),
            ("repair_cost", "ремонт", RepairBox),
        };
        var form = new Dictionary<string, object>();
        foreach (var (field, title, box) in numbers)
        {
            if (!long.TryParse(box.Text.Trim().Replace(" ", ""), out var value) || value < 0)
            {
                _status($"{title}: целое число от 0", true);
                return null;
            }
            form[field] = value;
        }
        var term = TermBox.Text.Trim().ToLowerInvariant();
        if (term != "" && !System.Text.RegularExpressions.Regex.IsMatch(term, @"^[1-9]\d{0,4}[hdm]$"))
        {
            _status("Срок: число и буква — 12h (часы), 7d (дни), 1m (месяцы), или пусто", true);
            return null;
        }
        form["expiration_time"] = term;
        form["offer_status"] = StatusBox.SelectedItem as string ?? "NORMAL";
        form["refundable"] = RefundableBox.IsChecked == true;
        return form;
    }

    private async void OnSave(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (ReadForm() is not { } form) return;
        if (_newItem is { } item)
        {
            form["item_name"] = item.Name;
            if (!Confirm($"Добавить {item.Name} в магазин?")) return;
            _status(await _api.AddOfferAsync(form), false);
            _newItem = null;
            StateBox.SelectedIndex = Array.FindIndex(States, s => s.Id == "added"); // покажет добавленные
            await Reload(1);
            return;
        }
        if (_offer is not { } offer) return;
        // Отправляем только изменённые поля: остальное остаётся как в каталоге
        var current = new Dictionary<string, object>
        {
            ["game_money"] = offer.GameMoney, ["cry_money"] = offer.CryMoney, ["crown_money"] = offer.CrownMoney,
            ["discount"] = offer.Discount, ["rank"] = (long)offer.Rank, ["quantity"] = (long)offer.Quantity,
            ["durability_points"] = (long)offer.DurabilityPoints, ["repair_cost"] = offer.RepairCost,
            ["expiration_time"] = offer.ExpirationTime, ["offer_status"] = offer.OfferStatus, ["refundable"] = offer.Refundable,
        };
        var changes = form.Where(f => !Equals(current[f.Key], f.Value)).ToDictionary(f => f.Key, f => f.Value);
        if (changes.Count == 0)
        {
            _status("Ничего не изменилось", false);
            return;
        }
        _status(await _api.UpdateOfferAsync(offer.Id, changes), false);
        await Run(LoadAsync);
        if (OffersList.SelectedItem is ShopOffer updated) ShowOffer(updated);
    });

    private async void OnHide(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_offer is not { } offer) return;
        var question = offer.State == "added" ? $"Удалить добавленное предложение {offer.ItemName}?" : $"Снять {offer.ItemName} с продажи?";
        if (!Confirm(question)) return;
        _status(await _api.HideOfferAsync(offer.Id), false);
        await AfterChange();
    });

    private async void OnShow(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_offer is not { } offer) return;
        _status(await _api.ShowOfferAsync(offer.Id), false);
        await AfterChange();
    });

    private async void OnReset(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_offer is not { } offer || !Confirm($"Вернуть {offer.ItemName} исходные значения из каталога игры?")) return;
        _status(await _api.ResetOfferAsync(offer.Id), false);
        await AfterChange();
    });

    private async Task AfterChange()
    {
        await LoadAsync();
        if (OffersList.SelectedItem is ShopOffer updated)
        {
            ShowOffer(updated);
        }
        else
        {
            _offer = null;
            Editor.IsEnabled = false;
            EditorTitle.Text = "Предложение";
            EditorInfo.Text = "Выберите предложение в списке или создайте новое.";
        }
    }
}
