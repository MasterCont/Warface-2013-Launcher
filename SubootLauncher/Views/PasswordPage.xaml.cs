using System.Windows;
using System.Windows.Controls;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>
/// Смена пароля. Сервер при этом завершает все сессии аккаунта — поэтому сразу входим заново
/// и сохраняем новую сессию, если вход был запомнен.
/// </summary>
public partial class PasswordPage : UserControl
{
    private readonly string _login;

    public PasswordPage(string login)
    {
        InitializeComponent();
        _login = login;
        Loaded += (_, _) => OldBox.Focus();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (NewBox.Password != RepeatBox.Password)
        {
            ShowError(Loc.T("Password.Mismatch"));
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await App.Api.ChangePasswordAsync(_login, OldBox.Password, NewBox.Password);
            var session = await App.Api.LoginAsync(_login, NewBox.Password, remember: true);
            AccountStore.Replace(_login, session.Session!);
            MessageBox.Show(App.Shell, Loc.T("Password.Done"), Loc.T("Password.Title"));
            App.Shell.Back();
        }
        catch (ApiException error)
        {
            ShowError(error.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }
}
