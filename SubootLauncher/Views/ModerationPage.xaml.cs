using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>
/// Панель модерации. Все действия идут в API с сессией модератора/админа — права проверяет сервер,
/// здесь только прячутся вкладки, недоступные роли.
/// </summary>
public partial class ModerationPage : UserControl, IReturnAware
{
    private readonly ApiClient _api;
    private bool _isAdmin;
    private PlayerDetails? _player;
    private string _search = "";
    private int _page = 1;
    private bool _playersLoaded;
    private bool _onlineLoaded;

    /// <summary>Вернулись со склада или достижений — карточка игрока могла устареть.</summary>
    public void OnReturned()
    {
        if (_player != null && TabPlayer.IsChecked == true) _ = Run(RefreshPlayerAsync);
    }

    public ModerationPage(string session, AccountInfo me)
    {
        InitializeComponent();
        _api = new ApiClient { StaffSession = session };
        ApplyRole(me);
        PageShop.Init(_api, Status);
        PageMissions.Init(_api, Status);
        PageRewards.Init(_api, Status);
        PageServer.Init(_api, Status, me.IsOwner);
        PageServer.PendingChanged += count => TabServer.Content = count > 0 ? $"Сервер ⚠ {count}" : "Сервер";
        PageAudit.Init(_api, Status);
        PageComplaints.Init(_api, Status, OpenPlayerFromElsewhere);
        PageComplaints.NewCountChanged += count => TabComplaints.Content = count > 0 ? $"Жалобы ({count})" : "Жалобы";
        // Loaded срабатывает и при возврате со склада или достижений — онлайн загружаем один раз
        Loaded += async (_, _) =>
        {
            if (_onlineLoaded) return;
            _onlineLoaded = true;
            await Run(LoadOnlineAsync);
            await Run(PageComplaints.LoadCountAsync); // счётчик новых жалоб на вкладке
            if (_isAdmin) await Run(PageServer.LoadPendingCountAsync); // запланированные сбросы — на вкладке «Сервер»
        };
    }

    /// <summary>
    /// Разделы под роль: админские вкладки и блоки карточки игрока видны только администратору,
    /// кнопка «Админ» — только главному. Вызывается и при смене роли, пока страница открыта.
    /// </summary>
    public void ApplyRole(AccountInfo me)
    {
        _isAdmin = me.IsAdmin;
        RoleAdminButton.Visibility = me.IsOwner ? Visibility.Visible : Visibility.Collapsed;
        PageServer.SetOwner(me.IsOwner);
        var adminTabs = new[] { TabInvites, TabAccounts, TabShop, TabAudit, TabMissions, TabRewards, TabServer };
        foreach (var tab in adminTabs) tab.Visibility = _isAdmin ? Visibility.Visible : Visibility.Collapsed;
        // Открытая админская вкладка стала недоступна — на «Онлайн»
        if (!_isAdmin && adminTabs.Any(tab => tab.IsChecked == true)) TabOnline.IsChecked = true;
        if (_player != null) ShowPlayer();
    }

    // --- Общее ---

    private void OnTab(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var pages = new (RadioButton Tab, FrameworkElement Page, Func<Task>? Load)[]
        {
            (TabOnline, PageOnline, LoadOnlineAsync),
            (TabPlayer, PagePlayer, () => _playersLoaded ? Task.CompletedTask : LoadPlayersAsync()),
            (TabBans, PageBans, LoadBansAsync),
            (TabBroadcast, PageBroadcast, null),
            (TabInvites, PageInvites, LoadInvitesAsync),
            (TabAccounts, PageAccounts, LoadAccountsAsync),
            (TabShop, PageShop, PageShop.LoadOnceAsync),
            (TabComplaints, PageComplaints, PageComplaints.LoadOnceAsync),
            (TabAudit, PageAudit, PageAudit.LoadOnceAsync),
            (TabMissions, PageMissions, PageMissions.LoadOnceAsync),
            (TabRewards, PageRewards, PageRewards.LoadOnceAsync),
            (TabServer, PageServer, PageServer.LoadOnceAsync),
        };
        foreach (var (tab, page, load) in pages)
        {
            var active = tab.IsChecked == true;
            page.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (active && load != null) _ = Run(load);
        }
        if (TabPlayer.IsChecked == true) NickBox.Focus();
    }

    /// <summary>Выполняет действие API: ошибки и результат показываются в строке состояния.</summary>
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

    // --- Онлайн ---

    private async Task LoadOnlineAsync()
    {
        var online = await _api.GetOnlineAsync();
        OnlineList.ItemsSource = online.Players.OrderBy(p => p.Nick).ToList();
        OnlineTitle.Text = $"В сети: {online.Players.Length}";
        if (online.UnavailableChannels.Length > 0)
        {
            Status("Не ответили каналы: " + string.Join(", ", online.UnavailableChannels), error: true);
        }
    }

    private async void OnRefreshOnline(object sender, RoutedEventArgs e) => await Run(LoadOnlineAsync);

    private void OnOnlineOpen(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (OnlineList.SelectedItem is OnlinePlayer player) OpenPlayerFromElsewhere(player.Nick);
    }

    /// <summary>Открыть карточку игрока из другой вкладки (онлайн, жалобы).</summary>
    private async void OpenPlayerFromElsewhere(string nick)
    {
        NickBox.Text = nick;
        _search = nick;
        _page = 1;
        _playersLoaded = true; // список загрузим сами, вкладка не должна перезагружать его
        TabPlayer.IsChecked = true;
        await Run(async () =>
        {
            await LoadPlayersAsync();
            await OpenPlayerAsync(nick);
        });
    }

    // --- Игрок ---

    private async Task LoadPlayersAsync()
    {
        var result = await _api.GetPlayersAsync(_search, _page);
        _playersLoaded = true;
        _page = result.Page;
        PlayersList.ItemsSource = result.Players;
        PageText.Text = result.Total == 0 ? "никого не найдено" : $"стр. {result.Page} из {result.Pages} · игроков {result.Total}";
        PrevPage.IsEnabled = result.Page > 1;
        NextPage.IsEnabled = result.Page < result.Pages;
        // Если открытый игрок есть на странице — подсвечиваем его
        if (_player != null) PlayersList.SelectedItem = result.Players.FirstOrDefault(p => p.Nick == _player.Nick);
    }

    private async void OnSearchPlayers(object sender, RoutedEventArgs e) => await SearchPlayers();

    private async void OnNickKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) await SearchPlayers();
    }

    private async Task SearchPlayers()
    {
        _search = NickBox.Text.Trim();
        _page = 1;
        await Run(LoadPlayersAsync);
    }

    private async void OnPrevPage(object sender, RoutedEventArgs e)
    {
        _page--;
        await Run(LoadPlayersAsync);
    }

    private async void OnNextPage(object sender, RoutedEventArgs e)
    {
        _page++;
        await Run(LoadPlayersAsync);
    }

    private async void OnPlayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PlayersList.SelectedItem is not PlayerRow row || row.Nick == _player?.Nick) return;
        await Run(() => OpenPlayerAsync(row.Nick));
    }

    private async Task OpenPlayerAsync(string nick)
    {
        _player = await _api.GetPlayerAsync(nick);
        ShowPlayer();
    }

    /// <summary>После действия над игроком — обновить карточку и отметки в списке.</summary>
    private async Task RefreshPlayerAsync()
    {
        if (_player == null) return;
        await OpenPlayerAsync(_player.Nick);
        await LoadPlayersAsync();
    }

    private void ShowPlayer()
    {
        if (_player is not { } p)
        {
            PlayerCard.Visibility = Visibility.Collapsed;
            PlayerHint.Visibility = Visibility.Visible;
            return;
        }
        PlayerCard.Visibility = Visibility.Visible;
        PlayerHint.Visibility = Visibility.Collapsed;
        PlayerName.Text = $"{p.Nick}  ·  звание {p.Rank}";
        PlayerInfo.Text = $"uid {p.Uid} · опыт {p.Experience:N0} · PvP {p.PvpRatingPoints:N0}" + (p.Clan != null ? $" · клан {p.Clan}" : "")
                          + $"\nВарбаксы {p.Money.Game:N0} · кредиты {p.Money.Cry:N0} · короны {p.Money.Crown:N0} · предметов {p.Items}";
        PlayerOnline.Text = p.Online is { } online
            ? $"● В сети: {online.Channel}, " + (online.InMatch ? "в матче" : online.RoomId != null ? "в комнате" : "в лобби")
            : "○ Не в сети";
        PlayerOnline.Foreground = (Brush)FindResource(p.Online != null ? "OnlineBrush" : "MutedBrush");
        PlayerBans.ItemsSource = p.Bans.Length > 0 ? p.Bans : null;

        RenameBox.Text = p.Nick;
        AdminProfilePanel.Visibility = _isAdmin ? Visibility.Visible : Visibility.Collapsed;
        var unlocked = p.MissionsUnlocked ?? [];
        var locked = p.MissionsLocked ?? [];
        foreach (var (box, mission) in MissionAccessBoxes()) box.IsChecked = unlocked.Contains(mission);
        AccessHint.Text = locked.Length > 0
            ? "Закрыто администратором (прогрессия не откроет): " + string.Join(", ", locked.Select(MissionNames.Type))
            : "Сложности открываются сами по прохождениям и званию; закрытую здесь прогрессия не откроет.";
        ExpBox.Text = p.Experience.ToString();
        GameMoneyBox.Text = p.Money.Game.ToString();
        CryMoneyBox.Text = p.Money.Cry.ToString();
        CrownMoneyBox.Text = p.Money.Crown.ToString();
        ExpHint.Text = p.Online != null
            ? "Игрок в сети: изменения применятся за ~10 с, он увидит их после перезахода."
            : "Игрок не в сети: изменения сохранятся сразу.";
    }

    private async void OnMessage(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_player == null || PlayerMessageText.Text.Trim() == "") return;
        Status(await _api.MessageAsync(_player.Nick, PlayerMessageText.Text.Trim()));
        PlayerMessageText.Text = "";
    });

    private async void OnRename(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_player == null) return;
        var nick = RenameBox.Text.Trim();
        if (nick == "" || nick == _player.Nick)
        {
            Status("Введите новый ник", error: true);
            return;
        }
        // Онлайн-игрока сервер держит в памяти: ник меняется только после его выхода
        var online = _player.Online != null;
        var question = online
            ? $"{_player.Nick} сейчас в игре. Отключить его и сменить ник на «{nick}»?\nОн сможет сразу зайти снова уже с новым ником."
            : $"Сменить ник {_player.Nick} на «{nick}»?";
        if (!Confirm(question)) return;
        Status(online ? "Отключаем игрока и меняем ник…" : "Меняем ник…");
        Status(await _api.RenameAsync(_player.Nick, nick, kick: online));
        _player = await _api.GetPlayerAsync(nick);
        ShowPlayer();
        await LoadPlayersAsync();
    });

    private async void OnKick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_player == null || !Confirm($"Кикнуть {_player.Nick} с сервера?")) return;
        Status(await _api.KickAsync(_player.Nick));
        await RefreshPlayerAsync();
    });

    private async void OnBan(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_player == null) return;
        var reason = BanReasonBox.Text.Trim();
        if (reason == "")
        {
            Status("Укажите причину бана", error: true);
            return;
        }
        double? hours = null;
        if (BanHoursBox.Text.Trim() != "")
        {
            if (!double.TryParse(BanHoursBox.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                Status("Срок бана — положительное число часов", error: true);
                return;
            }
            hours = parsed;
        }
        var term = hours is { } h ? $"на {h} ч" : "бессрочно";
        if (!Confirm($"Забанить {_player.Nick} {term}?\nПричина: {reason}")) return;
        Status(await _api.BanAsync(_player.Nick, hours, reason));
        BanReasonBox.Text = "";
        await RefreshPlayerAsync();
    });

    private async void OnUnban(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_player == null || !Confirm($"Снять бан с {_player.Nick}?")) return;
        Status(await _api.UnbanAsync(_player.Nick));
        await RefreshPlayerAsync();
    });

    // --- Профиль и склад (админ) ---

    private async void OnSaveProfile(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_player == null) return;
        var boxes = new (string Field, string Title, TextBox Box, long Current)[]
        {
            ("experience", "опыт", ExpBox, _player.Experience),
            ("game_money", "варбаксы", GameMoneyBox, _player.Money.Game),
            ("cry_money", "кредиты", CryMoneyBox, _player.Money.Cry),
            ("crown_money", "короны", CrownMoneyBox, _player.Money.Crown),
        };
        var values = new Dictionary<string, long>();
        var lines = new List<string>();
        foreach (var (field, title, box, current) in boxes)
        {
            if (!long.TryParse(box.Text.Trim().Replace(" ", ""), out var value) || value < 0 || value > int.MaxValue)
            {
                Status($"{title}: целое число от 0 до {int.MaxValue:N0}", error: true);
                return;
            }
            if (value == current) continue;
            values[field] = value;
            lines.Add($"{title}: {current:N0} → {value:N0}");
        }
        if (values.Count == 0)
        {
            Status("Значения не изменились");
            return;
        }
        if (!Confirm($"Изменить профиль {_player.Nick}?\n\n" + string.Join("\n", lines))) return;
        Status(await _api.SetProfileAsync(_player.Nick, values));
        await RefreshPlayerAsync();
    });

    private void OnAchievements(object sender, RoutedEventArgs e)
    {
        if (_player == null) return;
        App.Shell.Navigate(new AchievementsPage(_api, _player.Nick), $"Достижения — {_player.Nick}");
    }

    private (CheckBox Box, string Mission)[] MissionAccessBoxes() =>
    [
        (AccessTraining, "trainingmission"),
        (AccessEasy, "easymission"),
        (AccessNormal, "normalmission"),
        (AccessHard, "hardmission"),
    ];

    private async void OnSaveMissionAccess(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_player == null) return;
        var unlocked = _player.MissionsUnlocked ?? [];
        // Отправляем только изменённые сложности
        var changes = MissionAccessBoxes()
            .Where(x => (x.Box.IsChecked == true) != unlocked.Contains(x.Mission))
            .ToDictionary(x => x.Mission, x => x.Box.IsChecked == true);
        if (changes.Count == 0)
        {
            Status("Доступ не изменился");
            return;
        }
        var text = string.Join("\n", changes.Select(c => $"{MissionNames.Type(c.Key)}: {(c.Value ? "открыть" : "закрыть")}"));
        if (!Confirm($"Изменить доступ {_player.Nick} к PvE?\n\n{text}")) return;
        Status(await _api.SetMissionAccessAsync(_player.Nick, changes));
        await RefreshPlayerAsync();
    });

    // Сброс прогресса: два предупреждения, ввод ника вручную, выполнение через 5 минут с возможностью отмены
    private async void OnResetPlayer(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_player == null) return;
        var nick = _player.Nick;
        if (MessageBox.Show(App.Shell,
                $"Сбросить ВЕСЬ прогресс игрока {nick}?\n\n"
                + "Обнулятся: опыт и звание, деньги (до стартовых), все предметы кроме стандартного снаряжения, достижения, "
                + "статистика, контракты, поставщики, прогресс PvE и обучения, рейтинг, очки клана.\n"
                + "Останутся: логин и пароль, ник, друзья, клан, баны.\n\n"
                + "Перед сбросом сервер сделает резервную копию базы.",
                "Сброс прогресса — шаг 1 из 2", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (!TypeConfirmWindow.Ask(App.Shell, "Сброс прогресса — шаг 2 из 2",
                $"Сброс выполнится через 5 минут. До этого его может отменить любой администратор на вкладке «Сервер». "
                + "Игрок получит сообщение, а в момент сброса будет отключён от игры.",
                nick, "Запланировать сброс")) return;
        var reset = await _api.ScheduleResetAsync(nick, nick);
        Status($"Сброс {nick} запланирован (#{reset.Id}) на {reset.ExecuteAt.ToLocalTime():HH:mm:ss} — отменить можно на вкладке «Сервер»", error: true);
        _ = Run(PageServer.LoadPendingCountAsync);
    });

    private void OnStorage(object sender, RoutedEventArgs e)
    {
        if (_player == null) return;
        App.Shell.Navigate(new StoragePage(_api, _player.Nick), $"Склад — {_player.Nick}");
    }

    // --- Баны ---

    private async Task LoadBansAsync() => BansList.ItemsSource = await _api.GetBansAsync();

    private async void OnRefreshBans(object sender, RoutedEventArgs e) => await Run(LoadBansAsync);

    private async void OnUnbanSelected(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (BansList.SelectedItem is not BanInfo ban || ban.Nick == null || !Confirm($"Снять бан с {ban.Nick}?")) return;
        Status(await _api.UnbanAsync(ban.Nick));
        await LoadBansAsync();
    });

    // --- Рассылка ---

    private async void OnBroadcast(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var text = BroadcastBox.Text.Trim();
        if (text == "" || !Confirm("Отправить сообщение всем игрокам в сети?\n\n" + text)) return;
        Status(await _api.BroadcastAsync(text));
        BroadcastBox.Text = "";
    });

    // --- Ключи приглашения (админ) ---

    private async Task LoadInvitesAsync() => InvitesList.ItemsSource = await _api.GetInvitesAsync();

    private async void OnCreateInvite(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!int.TryParse(InviteUsesBox.Text.Trim(), out var uses) || uses < 1)
        {
            Status("Число регистраций — целое от 1", error: true);
            return;
        }
        double? days = null;
        if (InviteDaysBox.Text.Trim() != "")
        {
            if (!double.TryParse(InviteDaysBox.Text.Trim(), out var parsed) || parsed <= 0)
            {
                Status("Срок — положительное число дней", error: true);
                return;
            }
            days = parsed;
        }
        var invite = await _api.CreateInviteAsync(InviteNoteBox.Text.Trim(), uses, days);
        NewInviteBox.Text = invite.Code;
        NewInvitePanel.Visibility = Visibility.Visible;
        InviteNoteBox.Text = "";
        Status($"Ключ #{invite.Id} создан — передайте его игроку, второй раз код не показывается");
        await LoadInvitesAsync();
    });

    private void OnCopyInvite(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(NewInviteBox.Text);
        Status("Ключ скопирован");
    }

    private async void OnRevokeInvite(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (InvitesList.SelectedItem is not InviteInfo invite || !Confirm($"Отозвать ключ #{invite.Id}?")) return;
        Status(await _api.RevokeInviteAsync(invite.Id));
        await LoadInvitesAsync();
    });

    // --- Аккаунты (админ) ---

    private async Task LoadAccountsAsync() => AccountsList.ItemsSource = await _api.GetAccountsAsync();

    private async void OnRefreshAccounts(object sender, RoutedEventArgs e) => await Run(LoadAccountsAsync);

    private async void OnSetRole(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var role = (string)((Button)sender).Tag;
        if (AccountsList.SelectedItem is not AccountRow account || !Confirm($"Назначить {account.Login} роль «{role}»?")) return;
        Status(await _api.SetRoleAsync(account.Login, role));
        await LoadAccountsAsync();
    });

    private async void OnResetPassword(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (AccountsList.SelectedItem is not AccountRow account || !Confirm($"Выдать {account.Login} новый временный пароль? Старый перестанет работать.")) return;
        // 10 символов без похожих букв (0/O, 1/l)
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var password = new string(Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        await _api.SetPasswordAsync(account.Login, password);
        Clipboard.SetText(password);
        MessageBox.Show(App.Shell, $"Новый пароль для {account.Login}:\n\n{password}\n\nОн уже скопирован. Передайте его игроку — он сменит его в лаунчере.",
            "Временный пароль");
        Status($"Пароль {account.Login} изменён");
    });
}
