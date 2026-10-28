using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Строка списка учётных записей.</summary>
public record AccountRowView(string Login, string Details, bool IsCurrent)
{
    public Visibility SwitchVisibility => IsCurrent ? Visibility.Collapsed : Visibility.Visible;
    public Brush Border => IsCurrent ? (Brush)Application.Current.Resources["SelectedBorderBrush"] : Brushes.Transparent;
}

/// <summary>Переключатель учётных записей: сохранённые на этом компьютере сессии.</summary>
public partial class AccountsPage : UserControl
{
    private readonly string? _currentLogin;

    public AccountsPage(string? currentLogin)
    {
        InitializeComponent();
        _currentLogin = currentLogin;
        Refresh();
    }

    private void Refresh()
    {
        var rows = AccountStore.All.Select(a =>
        {
            var current = string.Equals(a.Login, _currentLogin, StringComparison.OrdinalIgnoreCase);
            var details = (current ? Loc.T("Accounts.Current") + " · " : "") + Loc.T("Accounts.LastUsed", a.LastUsed.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
            return new AccountRowView(a.Login, details, current);
        }).ToList();
        AccountsList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnSwitch(object sender, RoutedEventArgs e)
    {
        var login = (string)((Button)sender).Tag;
        if (AccountStore.Find(login) is not { } account) return;
        StatusText.Text = await App.Shell.SwitchToAsync(account.Login) ?? "";
        Refresh();
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        var login = (string)((Button)sender).Tag;
        if (AccountStore.Find(login) is not { } account) return;
        if (MessageBox.Show(App.Shell, Loc.T("Accounts.RemoveConfirm", login), Loc.T("Accounts.Title"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try { await App.Api.LogoutAsync(account.Session); } catch (ApiException) { /* сессия могла уже истечь */ }
        AccountStore.Remove(login);
        // Убрали текущую — выходим из неё
        if (string.Equals(login, _currentLogin, StringComparison.OrdinalIgnoreCase))
        {
            App.Shell.AfterLogout();
            return;
        }
        Refresh();
    }

    private void OnAdd(object sender, RoutedEventArgs e) => App.Shell.Navigate(new LoginPage(), Loc.T("Accounts.Add"));
}
