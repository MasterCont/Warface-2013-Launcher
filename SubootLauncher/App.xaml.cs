using System.Diagnostics;
using System.Threading;
using System.Windows;
using SubootLauncher.Services;
using SubootLauncher.Views;

namespace SubootLauncher;

/// <summary>
/// Точка входа: если сессия сохранена и ещё действует — сразу главное окно, иначе окно входа.
/// Лаунчер запускается в одном экземпляре: повторный запуск показывает уже открытое окно.
/// </summary>
public partial class App : Application
{
    public static ApiClient Api { get; } = new();
    public static Settings Settings { get; } = Settings.Load();

    /// <summary>Оформления: ключ → файл темы (название — Loc «Theme.{ключ}»).</summary>
    public static readonly Dictionary<string, string> Themes = new()
    {
        ["classic"] = "Themes/Classic.xaml",
        ["game"] = "Themes/Game.xaml",
    };

    // Имена в пределах сеанса пользователя: у каждого пользователя Windows свой лаунчер
    private const string MutexName = @"Local\SubootLauncher.Instance";
    private const string ShowEventName = @"Local\SubootLauncher.Show";
    private const string RestartArgument = "--restart";

    private static Mutex? _instance;
    private static EventWaitHandle? _showEvent;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!TakeInstance(e.Args.Contains(RestartArgument)))
        {
            Shutdown();
            return;
        }

        Loc.Init(Settings.Language);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Themes.GetValueOrDefault(Settings.Theme ?? "classic", Themes["classic"]), UriKind.Relative) });
        // Общие для обеих тем стили (списки, поля, логотип) — после темы, они ссылаются на её кисти
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

    /// <summary>
    /// Первый экземпляр забирает mutex и ждёт сигнала «покажись». Второй подаёт сигнал и выходит.
    /// При перезапуске (смена языка или оформления) новый экземпляр ждёт, пока старый закроется.
    /// </summary>
    private static bool TakeInstance(bool restarting)
    {
        _instance = new Mutex(false, MutexName);
        bool owned;
        try
        {
            owned = _instance.WaitOne(restarting ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // прошлый экземпляр упал, не освободив mutex
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!owned)
        {
            _showEvent.Set();
            return false;
        }

        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                Current?.Dispatcher.BeginInvoke(BringToFront);
            }
        }) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void BringToFront()
    {
        var window = Shell;
        if (window == null || !window.IsVisible) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        // Activate не всегда выводит окно поверх чужих: на мгновение делаем его «поверх всех»
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    /// <summary>Перезапуск для применения языка и оформления: сессия сохранена, вход не нужен.</summary>
    public static void Restart()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, RestartArgument) { UseShellExecute = false });
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _instance?.ReleaseMutex(); } catch (ApplicationException) { /* не владели */ }
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Единственное окно лаунчера; закрыли его — лаунчер завершается.</summary>
    public static ShellWindow Shell { get; private set; } = null!;

    private static void EnsureShell()
    {
        if (Shell != null) return;
        Shell = new ShellWindow();
        Shell.Closed += (_, _) => Current.Shutdown();
        Shell.Show();
    }

    public static void ShowLogin()
    {
        EnsureShell();
        Shell.ShowLogin();
    }

    public static void ShowMain(string session, AccountInfo me)
    {
        EnsureShell();
        Shell.ShowHome(session, me);
    }
}
