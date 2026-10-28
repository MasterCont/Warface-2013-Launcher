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
    private const string AliveEventName = @"Local\SubootLauncher.Alive";
    private const string RestartArgument = "--restart";

    private static Mutex? _instance;
    private static EventWaitHandle? _showEvent;
    private static EventWaitHandle? _aliveEvent;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Loc.Init(Settings.Language);
        Log.Write($"Запуск {Log.Version} · {Environment.ProcessPath} · Windows {Environment.OSVersion.Version} · {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}");
        // Страховка: необработанная ошибка в обработчике кнопки показывает сообщение, а не роняет лаунчер
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Log.Error("в интерфейсе", args.Exception);
            MessageBox.Show(Shell, Loc.T("App.Crash") + Environment.NewLine + Environment.NewLine + args.Exception.GetType().Name + ": " + args.Exception.Message,
                "Suboot Games", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception error) Log.Error("необработанная", error);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("в фоновой задаче", args.Exception);
            args.SetObserved();
        };

        try
        {
            await StartAsync(e);
        }
        catch (Exception error)
        {
            // Без окна игрок увидел бы «ничего не происходит» — показываем причину и где журнал
            Log.Error("при запуске", error);
            MessageBox.Show(Loc.T("App.StartFailed", error.Message, Log.File), "Suboot Games", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task StartAsync(StartupEventArgs e)
    {
        if (!TakeInstance(e.Args.Contains(RestartArgument)))
        {
            Log.Write("Лаунчер уже запущен — показываем его окно");
            Shutdown();
            return;
        }

        Loc.Init(Settings.Language);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Themes.GetValueOrDefault(Settings.Theme ?? "classic", Themes["classic"]), UriKind.Relative) });
        // Общие для обеих тем стили (списки, поля, логотип) — после темы, они ссылаются на её кисти
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Shared.xaml", UriKind.Relative) });

        // Последняя использованная учётная запись; истёкшие сессии убираем и пробуем следующую
        await AccountStore.MigrateAsync(Api);
        foreach (var account in AccountStore.All)
        {
            try
            {
                var me = await Api.GetMeAsync(account.Session);
                AccountStore.Touch(account.Login);
                ShowMain(account.Session, me);
                return;
            }
            catch (ApiException error) when (error.Code == 401)
            {
                AccountStore.Remove(account.Login);
            }
            catch (ApiException error)
            {
                // Сервер недоступен — покажем вход, там будет видна ошибка
                Log.Write("Сервер недоступен при запуске: " + error.Message);
                break;
            }
        }
        if (AccountStore.All.Count > 0)
        {
            EnsureShell();
            Shell.ShowAccounts();
            return;
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
        _aliveEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AliveEventName);
        if (!owned)
        {
            // Просим запущенный лаунчер показаться и ждём ответа: зависший не ответит
            _aliveEvent.Reset();
            _showEvent.Set();
            if (_aliveEvent.WaitOne(TimeSpan.FromSeconds(4))) return false;
            Log.Write("Запущенный лаунчер не отвечает");
            if (!CloseStuckInstances()) return false;
            try
            {
                owned = _instance.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }
            if (!owned) return false;
        }

        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                // Ответ «жив» — из потока интерфейса: если он завис, второй экземпляр это заметит
                Current?.Dispatcher.BeginInvoke(() =>
                {
                    _aliveEvent!.Set();
                    BringToFront();
                });
            }
        }) { IsBackground = true };
        thread.Start();
        return true;
    }

    /// <summary>Предложить закрыть зависший экземпляр лаунчера (без окна или не отвечает). true — закрыт.</summary>
    private static bool CloseStuckInstances()
    {
        if (MessageBox.Show(Loc.T("App.Stuck"), "Suboot Games", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        foreach (var process in Process.GetProcessesByName("SubootLauncher").Where(p => p.Id != Environment.ProcessId))
        {
            using (process)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                    Log.Write($"Закрыт зависший лаунчер {process.Id}");
                }
                catch (Exception error)
                {
                    Log.Error("при закрытии зависшего лаунчера", error);
                }
            }
        }
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
