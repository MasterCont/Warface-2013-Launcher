using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SubootLauncher.Services;
using SubootLauncher.Views;

namespace SubootLauncher;

/// <summary>Страница, которой нужно знать о возврате на неё (например, обновить данные).</summary>
public interface IReturnAware
{
    void OnReturned();
}

/// <summary>
/// Единственное окно лаунчера. Страницы открываются внутри него стеком: «‹ Назад», Esc или
/// боковая кнопка мыши возвращают на предыдущую. Вход и главная — корневые страницы.
/// </summary>
public partial class ShellWindow : Window
{
    private readonly Stack<(UserControl Page, string Title)> _back = new();
    private (UserControl Page, string Title) _current;
    private string? _session;
    private AccountInfo? _me;

    // Слежение за аккаунтом, пока лаунчер открыт: бан, снятие бана, смена роли, завершённая сессия
    private readonly DispatcherTimer _accountWatch = new() { Interval = TimeSpan.FromSeconds(30) };
    private DateTime _lastCheck = DateTime.MinValue;
    private bool _checking;

    public ShellWindow()
    {
        InitializeComponent();
        _accountWatch.Tick += async (_, _) => await CheckAccountAsync();
        // Вернулись в окно лаунчера — проверяем сразу (но не чаще раза в 10 секунд)
        Activated += async (_, _) =>
        {
            if (DateTime.UtcNow - _lastCheck > TimeSpan.FromSeconds(10)) await CheckAccountAsync();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _back.Count > 0 && !IsTextInputFocused())
            {
                Back();
                e.Handled = true;
            }
        };
        MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1 && _back.Count > 0) Back();
        };
    }

    // Esc в открытом выпадающем списке закрывает список, а не страницу
    private static bool IsTextInputFocused() => Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true };

    public bool IsLoggedIn => _session != null;

    // --- Корневые страницы ---

    public void ShowLogin()
    {
        _accountWatch.Stop();
        _session = null;
        _me = null;
        UpdateHeader();
        SetRoot(new LoginPage());
    }

    /// <summary>Главная — или страница блокировки, если у аккаунта действует бан.</summary>
    public void ShowHome(string session, AccountInfo me)
    {
        _session = session;
        _me = me;
        _lastCheck = DateTime.UtcNow;
        _accountWatch.Start();
        UpdateHeader();
        SetRoot(me.Ban != null ? new BannedPage(session, me) : new HomePage(session, me));
    }

    /// <summary>
    /// Проверка аккаунта на сервере. Появился бан — страница блокировки (с любой страницы, в том числе модерации);
    /// бан закончился — главная; сменилась роль — шапка; сессия завершена — выход.
    /// </summary>
    private async Task CheckAccountAsync()
    {
        if (_session == null || _me == null || _checking) return;
        _checking = true;
        _lastCheck = DateTime.UtcNow;
        var session = _session;
        try
        {
            var me = await App.Api.GetMeAsync(session);
            if (session != _session) return; // пока ждали ответа, переключились на другую учётную запись
            var banChanged = (me.Ban == null) != (_me.Ban == null) || me.Ban?.Id != _me.Ban?.Id;
            if (banChanged)
            {
                Log.Write(me.Ban != null ? $"Аккаунт {me.Login} заблокирован (бан #{me.Ban.Id})" : $"Бан аккаунта {me.Login} снят");
                ShowHome(session, me);
                return;
            }
            if (me.Role != _me.Role) Log.Write($"Роль {me.Login}: {_me.Role} -> {me.Role}");
            _me = me;
            UpdateHeader();
        }
        catch (ApiException error) when (error.Code == 401)
        {
            if (session != _session) return;
            Log.Write($"Сессия {_me.Login} завершена на сервере");
            AccountStore.Remove(_me.Login);
            MessageBox.Show(this, error.Message, Loc.T("Main.LoginTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            AfterLogout();
        }
        catch (ApiException)
        {
            // Сервер временно недоступен — проверим в следующий раз
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>Переключатель учётных записей: поверх текущей страницы или корнем, если вход не выполнен.</summary>
    public void ShowAccounts()
    {
        if (_current.Page is AccountsPage) return;
        if (_me == null)
        {
            UpdateHeader();
            SetRoot(new AccountsPage(null));
            return;
        }
        Navigate(new AccountsPage(_me.Login), Loc.T("Accounts.Title"));
    }

    /// <summary>После выхода: к другим сохранённым учётным записям или ко входу.</summary>
    public void AfterLogout()
    {
        _accountWatch.Stop();
        _session = null;
        _me = null;
        if (AccountStore.All.Count > 0) ShowAccounts();
        else ShowLogin();
    }

    /// <summary>Главная обновила данные аккаунта (роль могла измениться).</summary>
    public void SetAccount(AccountInfo me)
    {
        _me = me;
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        var loggedIn = _me != null;
        AccountPanel.Visibility = PasswordButton.Visibility = LogoutButton.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        // Заблокированному — без модерации: права всё равно проверяет сервер, а страница блокировки важнее
        StaffButton.Visibility = _me?.IsStaff == true && _me.Ban == null ? Visibility.Visible : Visibility.Collapsed;
        // Вошли — переключение в списке под ником; не вошли, но есть сохранённые — отдельная кнопка «Аккаунты»
        AccountsButton.Visibility = !loggedIn && AccountStore.All.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_me == null) return;
        AccountLogin.Text = _me.Login;
        AccountRole.Text = _me.Role switch
        {
            "owner" => Loc.T("Main.RoleOwner"),
            "admin" => Loc.T("Main.RoleAdmin"),
            "moderator" => Loc.T("Main.RoleModerator"),
            _ => Loc.T("Main.Uid", _me.Uid),
        };
    }

    // --- Навигация ---

    private void SetRoot(UserControl page)
    {
        _back.Clear();
        Show((page, ""));
    }

    /// <summary>Открыть страницу поверх текущей. Если такая страница уже открыта — просто вернуться к ней.</summary>
    public void Navigate(UserControl page, string title)
    {
        if (_current.Page?.GetType() == page.GetType() && page is SettingsPage or PasswordPage) return;
        _back.Push(_current);
        Show((page, title));
    }

    public void Back()
    {
        if (_back.Count == 0) return;
        Show(_back.Pop());
        (_current.Page as IReturnAware)?.OnReturned();
    }

    private void Show((UserControl Page, string Title) entry)
    {
        _current = entry;
        PageHost.Content = entry.Page;
        PageTitle.Text = entry.Title;
        BackBar.Visibility = _back.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Кнопки шапки ---

    private void OnBack(object sender, RoutedEventArgs e) => Back();

    // Логотип — на главную (или ко входу): закрывает все вложенные страницы
    private void OnHome(object sender, RoutedEventArgs e)
    {
        while (_back.Count > 0) Back();
    }

    private void OnAccounts(object sender, RoutedEventArgs e) => ShowAccounts();

    // --- Список аккаунтов под ником ---

    private void OnAccountMenu(object sender, RoutedEventArgs e)
    {
        AccountMenuList.ItemsSource = AccountStore.All.Select(a => new
        {
            a.Login,
            Mark = string.Equals(a.Login, _me?.Login, StringComparison.OrdinalIgnoreCase) ? "✓" : "",
        }).ToList();
    }

    private async void OnMenuSwitch(object sender, RoutedEventArgs e)
    {
        AccountToggle.IsChecked = false;
        var login = (string)((Button)sender).Tag;
        if (string.Equals(login, _me?.Login, StringComparison.OrdinalIgnoreCase)) return;
        var error = await SwitchToAsync(login);
        if (error != null) MessageBox.Show(this, error, Loc.T("Accounts.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnMenuAdd(object sender, RoutedEventArgs e)
    {
        AccountToggle.IsChecked = false;
        Navigate(new LoginPage(), Loc.T("Accounts.Add"));
    }

    private void OnMenuManage(object sender, RoutedEventArgs e)
    {
        AccountToggle.IsChecked = false;
        ShowAccounts();
    }

    /// <summary>Переключиться на сохранённую учётную запись. Возвращает текст ошибки или null.</summary>
    public async Task<string?> SwitchToAsync(string login)
    {
        if (AccountStore.Find(login) is not { } account) return null;
        try
        {
            var me = await App.Api.GetMeAsync(account.Session);
            AccountStore.Touch(login);
            App.ShowMain(account.Session, me);
            return null;
        }
        catch (ApiException error) when (error.Code == 401)
        {
            AccountStore.Remove(login);
            UpdateHeader();
            return Loc.T("Accounts.Expired", login);
        }
        catch (ApiException error)
        {
            return error.Message;
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e) => Navigate(new SettingsPage(), Loc.T("Settings.Title"));

    private void OnChangePassword(object sender, RoutedEventArgs e)
    {
        if (_me != null) Navigate(new PasswordPage(_me.Login), Loc.T("Password.Title"));
    }

    private void OnStaff(object sender, RoutedEventArgs e)
    {
        if (_session == null || _me == null) return;
        if (_back.Any(entry => entry.Page is ModerationPage) || _current.Page is ModerationPage)
        {
            // Модерация уже открыта — возвращаемся к ней, не теряя состояние
            while (_current.Page is not ModerationPage && _back.Count > 0) Back();
            return;
        }
        var role = _me.Role switch { "owner" => "главный администратор", "admin" => "администратор", _ => "модератор" };
        Navigate(new ModerationPage(_session, _me), $"Модерация — {_me.Login} ({role})");
    }

    // Выход завершает сессию и убирает учётную запись из сохранённых
    private async void OnLogout(object sender, RoutedEventArgs e)
    {
        if (_session != null)
        {
            try { await App.Api.LogoutAsync(_session); } catch (ApiException) { /* сессия могла уже истечь */ }
        }
        if (_me != null) AccountStore.Remove(_me.Login);
        AfterLogout();
    }
}
