using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Журнал аудита API (администратор): по 50 записей, «Ещё» подгружает более ранние.</summary>
public partial class AuditPanel : UserControl
{
    private ApiClient _api = null!;
    private Action<string, bool> _status = (_, _) => { };
    private readonly ObservableCollection<AuditEntry> _entries = [];
    private bool _loaded;

    public AuditPanel()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _entries;
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

    public Task LoadOnceAsync() => _loaded ? Task.CompletedTask : LoadAsync(append: false);

    private async Task LoadAsync(bool append)
    {
        var before = append && _entries.Count > 0 ? _entries[^1].Id : (long?)null;
        var page = await _api.GetAuditAsync(before, SearchBox.Text.Trim(), ErrorsBox.IsChecked == true);
        _loaded = true;
        if (!append) _entries.Clear();
        foreach (var entry in page) _entries.Add(entry);
        MoreButton.IsEnabled = page.Length == 50;
        CountText.Text = _entries.Count == 0 ? "записей нет" : $"показано {_entries.Count}" + (page.Length == 50 ? "" : " · это все");
    }

    private async void OnSearch(object sender, RoutedEventArgs e) => await Run(() => LoadAsync(append: false));

    private async void OnSearchKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        await Run(() => LoadAsync(append: false));
    }

    private async void OnMore(object sender, RoutedEventArgs e) => await Run(() => LoadAsync(append: true));

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (EntriesList.SelectedItem is not AuditEntry entry) return;
        DetailTitle.Text = $"Запись #{entry.Id}";
        DetailText.Text = $"{entry.At.ToLocalTime():dd.MM.yyyy HH:mm:ss}\n"
                          + $"Кто: {entry.KeyName ?? "без ключа"} (роль {entry.Role})\n"
                          + $"IP: {entry.Ip ?? "—"}\n"
                          + $"Запрос: {entry.Method} {Uri.UnescapeDataString(entry.Path)}\n"
                          + $"Ответ: {entry.Code}" + (string.IsNullOrEmpty(entry.Reason) ? "" : $" — {entry.Reason}");
        BodyText.Text = entry.BodyText;
        BodyText.Visibility = Visibility.Visible;
    }
}
