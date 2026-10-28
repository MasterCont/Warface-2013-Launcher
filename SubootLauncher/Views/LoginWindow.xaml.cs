using System.Windows;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Окно входа и регистрации (регистрация — по ключу приглашения, если сервер приватный).</summary>
public partial class LoginWindow : Window
{
    public bool LoggedIn { get; private set; }

    private bool IsRegister => RegisterTab.IsChecked == true;

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoginBox.Focus();
    }

    private async void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        HideError();
        SubmitButton.IsEnabled = true;
        RepeatPanel.Visibility = IsRegister ? Visibility.Visible : Visibility.Collapsed;
        SubmitButton.Content = Loc.T(IsRegister ? "Login.SubmitRegister" : "Login.Submit");
        InvitePanel.Visibility = Visibility.Collapsed;
        if (!IsRegister) return;

        try
        {
            var mode = await App.Api.GetRegistrationModeAsync();
            if (mode == "closed")
            {
                ShowError(Loc.T("Login.Closed"));
                SubmitButton.IsEnabled = false;
                return;
            }
            InvitePanel.Visibility = mode == "invite" ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException error)
        {
            ShowError(error.Message);
        }
    }

    private async void OnSubmit(object sender, RoutedEventArgs e)
    {
        HideError();
        var login = LoginBox.Text.Trim();
        var password = PasswordBox.Password;
        if (login == "" || password == "")
        {
            ShowError(Loc.T("Login.Empty"));
            return;
        }
        if (IsRegister && password != RepeatBox.Password)
        {
            ShowError(Loc.T("Login.Mismatch"));
            return;
        }

        SubmitButton.IsEnabled = false;
        try
        {
            if (IsRegister)
            {
                await App.Api.RegisterAsync(login, password, InviteBox.Text.Trim());
            }
            // Сессия нужна главному окну всегда; «Запомнить» решает только, сохранять ли её на диск
            var session = await App.Api.LoginAsync(login, password, remember: true);
            if (RememberBox.IsChecked == true) Settings.SaveSession(session.Session!);
            var me = await App.Api.GetMeAsync(session.Session!);

            LoggedIn = true;
            App.ShowMain(session.Session!, me);
            Close();
        }
        catch (ApiException error)
        {
            ShowError(error.Message);
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
