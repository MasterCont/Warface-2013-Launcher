using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Отмечаемый пункт списка (участок, вариант миссии) с уведомлением об изменении для привязки.</summary>
public class Pick(string id, string title, string details = "") : INotifyPropertyChanged
{
    private bool _selected;

    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Details { get; } = details;

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            Changed?.Invoke();
        }
    }

    public event Action? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Карта с участками; «все» отмечает или снимает все участки карты.</summary>
public class MapPick : INotifyPropertyChanged
{
    public MapPick(MapInfo map, IEnumerable<string> selected)
    {
        Title = map.Title;
        var chosen = selected.ToHashSet();
        Sublevels = map.Sublevels.Select(s => new Pick(s.Name, s.Name) { Selected = chosen.Contains(s.Name) }).ToList();
        Sublevels.ForEach(s => s.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(All))));
    }

    public string Title { get; }
    public List<Pick> Sublevels { get; }

    public bool? All
    {
        get => Sublevels.All(s => s.Selected) ? true : Sublevels.Any(s => s.Selected) ? null : false;
        set => Sublevels.ForEach(s => s.Selected = value == true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>PvE-миссии (администратор): текущие миссии и настройки генерации.</summary>
public partial class MissionsPanel : UserControl
{
    private ApiClient _api = null!;
    private Action<string, bool> _status = (_, _) => { };
    private bool _loaded;
    private MissionInfo? _mission;

    private MissionSettingsData? _catalog;
    private readonly Dictionary<string, TypeSettings> _edited = new();
    private string? _currentType;
    private List<MapPick> _maps = [];
    private List<Pick> _graphs = [];

    public MissionsPanel()
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

    private bool Confirm(string question) =>
        MessageBox.Show(App.Shell, question, "Подтверждение", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public Task LoadOnceAsync() => _loaded ? Task.CompletedTask : LoadMissionsAsync();

    private void OnSection(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var settings = SettingsTab.IsChecked == true;
        CurrentSection.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsSection.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        if (settings && _catalog == null) _ = Run(LoadSettingsAsync);
    }

    // --- Текущие миссии ---

    private async Task LoadMissionsAsync()
    {
        var data = await _api.GetMissionsAsync();
        _loaded = true;
        var order = new[] { "trainingmission", "easymission", "normalmission", "hardmission" };
        MissionsList.ItemsSource = data.Missions.OrderBy(m => Array.IndexOf(order, m.Type) is var i && i < 0 ? 99 : i).ToList();
        GeneratedText.Text = data.GeneratedAt is { } at
            ? $"Сгенерированы {at.ToLocalTime():dd.MM.yyyy HH:mm} · следующая смена в 04:00 МСК"
            : "Миссии ещё не сгенерированы";
        ShowMission(null);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await Run(LoadMissionsAsync);

    private void OnMissionSelected(object sender, SelectionChangedEventArgs e) => ShowMission(MissionsList.SelectedItem as MissionInfo);

    private void ShowMission(MissionInfo? mission)
    {
        _mission = mission;
        MissionActions.IsEnabled = mission != null;
        if (mission == null)
        {
            MissionTitle.Text = "Миссия";
            MissionText.Text = "Миссии сменяются каждый день в 04:00 по Москве. Здесь можно поправить текущие до следующей смены.";
            TimeBox.Text = "";
            return;
        }
        MissionTitle.Text = mission.Title;
        MissionText.Text = $"Сложность: {MissionNames.Difficulty(mission.Difficulty)}\nУчастки: {string.Join(" → ", mission.Sublevels)}\n"
                           + $"Доп. заданий: {mission.SecondaryObjectives}\nНазвание в игре: @{mission.Name}";
        TimeBox.Text = mission.TimeOfDay;
    }

    private async void OnSetTime(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_mission == null) return;
        _status(await _api.SetMissionTimeAsync(_mission.Uid, TimeBox.Text.Trim()), false);
        await LoadMissionsAsync();
    });

    private async void OnRemoveMission(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_mission == null || !Confirm($"Убрать миссию «{_mission.Title}» до следующей генерации?")) return;
        _status(await _api.RemoveMissionAsync(_mission.Uid), false);
        await LoadMissionsAsync();
    });

    private async void OnRegenerate(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!Confirm("Сгенерировать миссии заново прямо сейчас?\n\nТекущие миссии сменятся у всех игроков, а таблица рекордов прохождений обнулится — как при ежедневной смене в 04:00.")) return;
        await RegenerateAsync();
    });

    private async Task RegenerateAsync()
    {
        _status("Генерируем миссии…", false);
        _status(await _api.RegenerateMissionsAsync(), false);
        await LoadMissionsAsync();
    }

    // --- Настройки генерации ---

    private async Task LoadSettingsAsync()
    {
        _catalog = await _api.GetMissionSettingsAsync();
        _edited.Clear();
        foreach (var type in _catalog.Types)
        {
            var saved = _catalog.Settings?.Types.GetValueOrDefault(type.Name);
            _edited[type.Name] = new TypeSettings(
                saved?.Enabled ?? true,
                saved?.Sublevels ?? type.Defaults.Sublevels,
                saved?.Graphs ?? type.Defaults.Graphs,
                saved?.TimeOfDay ?? type.Defaults.TimeOfDay,
                saved?.SecondaryMin ?? type.Defaults.SecondaryMin,
                saved?.SecondaryMax ?? type.Defaults.SecondaryMax);
        }
        var publish = _catalog.Settings?.Publish ?? new Dictionary<string, bool>();
        PublishEasy.IsChecked = publish.GetValueOrDefault("easy", true);
        PublishNormal.IsChecked = publish.GetValueOrDefault("normal", true);
        PublishHard.IsChecked = publish.GetValueOrDefault("hard", true);

        _currentType = null;
        TypeBox.SelectionChanged -= OnTypeChanged;
        TypeBox.ItemsSource = _catalog.Types.Select(t => t.Chain.Length > 1
            ? string.Join(" → ", t.Chain.Select(MissionNames.Difficulty)) + " (основные)"
            : MissionNames.Type(t.Name)).ToList();
        TypeBox.SelectionChanged += OnTypeChanged;
        TypeBox.SelectedIndex = Math.Max(0, Array.FindIndex(_catalog.Types, t => t.Chain.Length > 1));
        WarningsText.Visibility = Visibility.Collapsed;
        if (_catalog.UpdatedBy != null) _status($"Настройки миссий изменял {_catalog.UpdatedBy}", false);
    }

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_catalog == null || TypeBox.SelectedIndex < 0) return;
        StoreCurrentType();
        var type = _catalog.Types[TypeBox.SelectedIndex];
        _currentType = type.Name;
        var settings = _edited[type.Name];

        EnabledBox.IsChecked = settings.Enabled != false;
        PublishPanel.Visibility = type.Chain.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsTimeBox.Text = settings.TimeOfDay ?? "";
        SecondaryMinBox.Text = settings.SecondaryMin?.ToString() ?? "";
        SecondaryMaxBox.Text = settings.SecondaryMax?.ToString() ?? "";

        _maps = _catalog.Maps.Select(m => new MapPick(m, settings.Sublevels ?? [])).ToList();
        MapsList.ItemsSource = _maps;

        var defaults = (type.Defaults.Graphs ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chosen = (settings.Graphs ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mapTitles = _catalog.Maps.ToDictionary(m => m.Map, m => m.Title);
        _graphs = type.Graphs.Select(g => new Pick(g.Name,
                (defaults.Contains(g.Name) ? "★ " : "") + $"{mapTitles.GetValueOrDefault(g.Map, g.Map)} · {g.Name.Split('_')[0]}",
                $"этапов: {g.Stages}" + (g.Boss != null ? $" · финал: {g.Boss}" : " · без босса"))
            { Selected = chosen.Contains(g.Name) }).ToList();
        GraphsList.ItemsSource = _graphs;
    }

    /// <summary>Запомнить правки текущего типа перед переключением или сохранением.</summary>
    private bool StoreCurrentType()
    {
        if (_currentType == null) return true;
        int? Number(TextBox box, string title)
        {
            var text = box.Text.Trim();
            if (text == "") return null;
            if (int.TryParse(text, out var value) && value is >= 0 and <= 5) return value;
            _status($"{title}: целое число от 0 до 5", true);
            throw new FormatException();
        }
        try
        {
            _edited[_currentType] = new TypeSettings(
                EnabledBox.IsChecked == true,
                _maps.SelectMany(m => m.Sublevels).Where(s => s.Selected).Select(s => s.Id).ToArray(),
                _graphs.Where(g => g.Selected).Select(g => g.Id).ToArray(),
                SettingsTimeBox.Text.Trim() == "" ? null : SettingsTimeBox.Text.Trim(),
                Number(SecondaryMinBox, "Доп. заданий от"),
                Number(SecondaryMaxBox, "Доп. заданий до"));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void OnAllGraphs(object sender, RoutedEventArgs e) => _graphs.ForEach(g => g.Selected = true);

    private void OnDefaultGraphs(object sender, RoutedEventArgs e)
    {
        var type = _catalog?.Types.FirstOrDefault(t => t.Name == _currentType);
        if (type == null) return;
        var defaults = (type.Defaults.Graphs ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _graphs.ForEach(g => g.Selected = defaults.Contains(g.Id));
    }

    private async Task<bool> SaveSettingsAsync()
    {
        if (_catalog == null || !StoreCurrentType()) return false;
        var payload = new
        {
            types = _edited.ToDictionary(t => t.Key, t => (object)new
            {
                enabled = t.Value.Enabled != false,
                sublevels = t.Value.Sublevels,
                graphs = t.Value.Graphs,
                time_of_day = t.Value.TimeOfDay,
                secondary_min = t.Value.SecondaryMin,
                secondary_max = t.Value.SecondaryMax,
            }),
            publish = new { easy = PublishEasy.IsChecked == true, normal = PublishNormal.IsChecked == true, hard = PublishHard.IsChecked == true },
        };
        var warnings = await _api.SaveMissionSettingsAsync(payload);
        WarningsText.Text = warnings.Length == 0 ? "" : "Предупреждения генератора:\n• " + string.Join("\n• ", warnings);
        WarningsText.Visibility = warnings.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _status("Настройки миссий сохранены — применятся при следующей генерации (в 04:00 или по кнопке)", false);
        return true;
    }

    private async void OnSaveSettings(object sender, RoutedEventArgs e) => await Run(async () => await SaveSettingsAsync());

    private async void OnSaveAndRegenerate(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!Confirm("Сохранить настройки и сразу сгенерировать миссии заново?\n\nТаблица рекордов прохождений обнулится.")) return;
        if (!await SaveSettingsAsync()) return;
        await RegenerateAsync();
    });

    private async void OnResetSettings(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!Confirm("Вернуть исходные настройки генерации миссий (как в игре)?")) return;
        _status(await _api.ResetMissionSettingsAsync(), false);
        await LoadSettingsAsync();
    });
}
