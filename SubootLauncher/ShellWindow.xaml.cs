using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public ShellWindow()
    {
        InitializeComponent();
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
        UpdateHeader();
        SetRoot(me.Ban != null ? new BannedPage(session, me) : new HomePage(session, me));
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
        AccountsButton.Visibility = loggedIn || AccountStore.All.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_me == null) return;
        AccountLogin.Text = _me.Login;
        AccountRole.Text = _me.Role switch
        {
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
        var role = _me.Role == "admin" ? "администратор" : "модератор";
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
