using System.Windows;
using System.Windows.Controls;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Жалобы игроков (модератор): фильтр по статусу, разбор с заметкой, переход к карточке игрока.</summary>
public partial class ComplaintsPanel : UserControl
{
    private ApiClient _api = null!;
    private Action<string, bool> _status = (_, _) => { };
    private Action<string> _openPlayer = _ => { };
    private int _page = 1;
    private bool _loaded;
    private Complaint? _selected;

    /// <summary>Число новых жалоб изменилось — для счётчика на вкладке.</summary>
    public event Action<int>? NewCountChanged;

    public ComplaintsPanel()
    {
        InitializeComponent();
        StatusBox.ItemsSource = Complaint.Statuses.Select(s => s.Title).ToList();
        StatusBox.SelectedIndex = 0;
    }

    public void Init(ApiClient api, Action<string, bool> status, Action<string> openPlayer)
    {
        _api = api;
        _status = status;
        _openPlayer = openPlayer;
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

    /// <summary>Только счётчик новых — для вкладки, пока панель не открыта.</summary>
    public async Task LoadCountAsync()
    {
        var result = await _api.GetComplaintsAsync("new", null, 1);
        NewCountChanged?.Invoke(result.Counts.GetValueOrDefault("new"));
    }

    private async Task LoadAsync()
    {
        var status = Complaint.Statuses[Math.Max(0, StatusBox.SelectedIndex)].Id;
        var result = await _api.GetComplaintsAsync(status, null, _page);
        _loaded = true;
        _page = result.Page;
        ComplaintsList.ItemsSource = result.Complaints;
        PageText.Text = result.Total == 0 ? "жалоб нет" : $"стр. {result.Page} из {result.Pages} · жалоб {result.Total}";
        PrevPage.IsEnabled = result.Page > 1;
        NextPage.IsEnabled = result.Page < result.Pages;

        // Подписи фильтра с числами: «Новые (3)»
        var index = StatusBox.SelectedIndex;
        StatusBox.SelectionChanged -= OnFilterChanged;
        StatusBox.ItemsSource = Complaint.Statuses
            .Select(s => s.Id == null ? s.Title : $"{s.Title} ({result.Counts.GetValueOrDefault(s.Id)})").ToList();
        StatusBox.SelectedIndex = index;
        StatusBox.SelectionChanged += OnFilterChanged;
        NewCountChanged?.Invoke(result.Counts.GetValueOrDefault("new"));

        ComplaintsList.SelectedItem = _selected == null ? null : result.Complaints.FirstOrDefault(c => c.Id == _selected.Id);
        if (ComplaintsList.SelectedItem == null) ShowComplaint(null);
    }

    private async Task Reload(int page)
    {
        _page = page;
        await Run(LoadAsync);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await Reload(_page);

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded) await Reload(1);
    }

    private async void OnPrevPage(object sender, RoutedEventArgs e) => await Reload(_page - 1);

    private async void OnNextPage(object sender, RoutedEventArgs e) => await Reload(_page + 1);

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ComplaintsList.SelectedItem is Complaint complaint) ShowComplaint(complaint);
    }

    private void ShowComplaint(Complaint? complaint)
    {
        _selected = complaint;
        Actions.IsEnabled = complaint != null;
        if (complaint == null)
        {
            DetailTitle.Text = "Жалоба";
            DetailText.Text = "Выберите жалобу в списке. Игроки отправляют их в игре: меню игрока → «Пожаловаться».";
            CommentText.Text = "";
            HandledText.Text = "";
            NoteBox.Text = "";
            return;
        }
        DetailTitle.Text = $"Жалоба #{complaint.Id} — {complaint.StatusTitle}";
        DetailText.Text = $"{complaint.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}\n"
                          + $"От: {complaint.ReporterNick ?? "?"}\nНа: {complaint.TargetNick}" + (complaint.TargetUid == null ? " (такого игрока нет на сервере)" : "")
                          + $"\nПричина: {complaint.TypeTitle}"
                          + (complaint.Channel != null ? $"\nКанал: {complaint.Channel}" + (complaint.RoomId != null ? $", комната {complaint.RoomId}" : "") : "");
        CommentText.Text = string.IsNullOrWhiteSpace(complaint.Comment) ? "Без комментария." : "«" + complaint.Comment + "»";
        NoteBox.Text = complaint.Note ?? "";
        HandledText.Text = complaint.HandledBy != null
            ? $"Разбирал: {complaint.HandledBy}" + (complaint.HandledAt is { } at ? $", {at.ToLocalTime():dd.MM.yyyy HH:mm}" : "")
            : "";
        OpenTargetButton.IsEnabled = complaint.TargetUid != null;
        OpenReporterButton.IsEnabled = complaint.ReporterNick != null;
    }

    private void OnOpenTarget(object sender, RoutedEventArgs e)
    {
        if (_selected != null) _openPlayer(_selected.TargetNick);
    }

    private void OnOpenReporter(object sender, RoutedEventArgs e)
    {
        if (_selected?.ReporterNick != null) _openPlayer(_selected.ReporterNick);
    }

    private async void OnSetStatus(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_selected == null) return;
        var status = (string)((Button)sender).Tag;
        var note = NoteBox.Text.Trim();
        _status(await _api.UpdateComplaintAsync(_selected.Id, status, note == "" ? null : note), false);
        await LoadAsync();
        if (ComplaintsList.SelectedItem is Complaint updated) ShowComplaint(updated);
    });
}
