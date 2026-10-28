using System.Windows;
using System.Windows.Controls;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Выбор предмета из каталога игры (по 15 на странице): выбранный передаётся в onPicked, страница закрывается.</summary>
public partial class ItemPickerPage : UserControl
{
    private readonly ApiClient _api;
    private int _page = 1;

    private readonly Action<GameItem> _onPicked;

    public ItemPickerPage(ApiClient api, string? group, Action<GameItem> onPicked)
    {
        InitializeComponent();
        _api = api;
        _onPicked = onPicked;
        GroupBox.ItemsSource = ItemNames.Groups.Select(g => g.Title).ToList();
        GroupBox.SelectedIndex = Math.Max(0, Array.FindIndex(ItemNames.Groups, g => g.Id == group));
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            await LoadAsync();
        };
    }

    private async Task LoadAsync()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var group = ItemNames.Groups[Math.Max(0, GroupBox.SelectedIndex)].Id;
            var result = await _api.GetItemsAsync(SearchBox.Text.Trim(), group, _page);
            _page = result.Page;
            ItemsList.ItemsSource = result.Items;
            PageText.Text = result.Total == 0 ? "ничего не найдено" : $"стр. {result.Page} из {result.Pages} · предметов {result.Total}";
            PrevPage.IsEnabled = result.Page > 1;
            NextPage.IsEnabled = result.Page < result.Pages;
        }
        catch (ApiException error)
        {
            ErrorText.Text = error.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        _page = 1;
        await LoadAsync();
    }

    private async void OnSearchKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true; // Enter в поиске — поиск, а не «Выбрать»
        _page = 1;
        await LoadAsync();
    }

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _page = 1;
        await LoadAsync();
    }

    private async void OnPrevPage(object sender, RoutedEventArgs e)
    {
        _page--;
        await LoadAsync();
    }

    private async void OnNextPage(object sender, RoutedEventArgs e)
    {
        _page++;
        await LoadAsync();
    }

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is not GameItem item) return;
        App.Shell.Back();
        _onPicked(item);
    }
}
