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
public partial class ModerationWindow : Window
{
    private readonly ApiClient _api;
    private readonly bool _isAdmin;
    private PlayerDetails? _player;
    private string _search = "";
    private int _page = 1;
    private bool _playersLoaded;

    public ModerationWindow(string session, AccountInfo me)
    {
        InitializeComponent();
        _api = new ApiClient { StaffSession = session };
        _isAdmin = me.Role == "admin";
        Title = $"Модерация — {me.Login} ({(_isAdmin ? "администратор" : "модератор")})";
        TabInvites.Visibility = _isAdmin ? Visibility.Visible : Visibility.Collapsed;
        TabAccounts.Visibility = _isAdmin ? Visibility.Visible : Visibility.Collapsed;
        TabShop.Visibility = _isAdmin ? Visibility.Visible : Visibility.Collapsed;
        PageShop.Init(_api, Status);
        Loaded += async (_, _) => await LoadOnlineAsync();
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
        MessageBox.Show(this, question, "Подтверждение", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

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

    private async void OnOnlineOpen(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (OnlineList.SelectedItem is not OnlinePlayer player) return;
        NickBox.Text = player.Nick;
        _search = player.Nick;
        _page = 1;
        _playersLoaded = true; // список загрузим сами, вкладка не должна перезагружать его
        TabPlayer.IsChecked = true;
        await Run(async () =>
        {
            await LoadPlayersAsync();
            await OpenPlayerAsync(player.Nick);
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

        AdminProfilePanel.Visibility = _isAdmin ? Visibility.Visible : Visibility.Collapsed;
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
        new AchievementsWindow(_api, _player.Nick) { Owner = this }.Show();
    }

    private void OnStorage(object sender, RoutedEventArgs e)
    {
        if (_player == null) return;
        new StorageWindow(_api, _player.Nick) { Owner = this }.Show();
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
        MessageBox.Show(this, $"Новый пароль для {account.Login}:\n\n{password}\n\nОн уже скопирован. Передайте его игроку — он сменит его в лаунчере.",
            "Временный пароль");
        Status($"Пароль {account.Login} изменён");
    });
}
