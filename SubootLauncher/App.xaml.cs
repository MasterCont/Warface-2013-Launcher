using System.Windows;
using SubootLauncher.Services;
using SubootLauncher.Views;

namespace SubootLauncher;

/// <summary>
/// Точка входа: если сессия сохранена и ещё действует — сразу главное окно, иначе окно входа.
/// </summary>
public partial class App : Application
{
    public static ApiClient Api { get; } = new();
    public static Settings Settings { get; } = Settings.Load();

    /// <summary>Оформления: ключ → название и файл темы.</summary>
    public static readonly Dictionary<string, (string Name, string File)> Themes = new()
    {
        ["classic"] = ("Сайт 2013", "Themes/Classic.xaml"),
        ["game"] = ("Как в игре", "Themes/Game.xaml"),
    };

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var theme = Themes.GetValueOrDefault(Settings.Theme ?? "classic", Themes["classic"]);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(theme.File, UriKind.Relative) });
        // Общие для обеих тем стили (списки и поля панелей) — после темы, они ссылаются на её кисти
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Shared.xaml", UriKind.Relative) });

        var session = Settings.LoadSession();
        if (session != null)
        {
            try
            {
                var me = await Api.GetMeAsync(session);
                ShowMain(session, me);
                return;
            }
            catch (ApiException error) when (error.Code == 401)
            {
                Settings.ForgetSession();
            }
            catch (ApiException)
            {
                // Сервер недоступен — покажем окно входа, там будет видна ошибка
            }
        }
        ShowLogin();
    }

    public static void ShowLogin()
    {
        var window = new LoginWindow();
        window.Closed += (_, _) => { if (!window.LoggedIn) Current.Shutdown(); };
        window.Show();
    }

    public static void ShowMain(string session, AccountInfo me)
    {
        var window = new MainWindow(session, me);
        window.Closed += (_, _) => { if (!window.LoggedOut) Current.Shutdown(); };
        window.Show();
    }
}
