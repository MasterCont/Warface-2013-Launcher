using System.Diagnostics;
using System.IO;

namespace SubootLauncher.Services;

/// <summary>Поиск папки игры и запуск Game.exe с одноразовым токеном (как Start.bat).</summary>
public static class GameLauncher
{
    public static string ExePath(string gamePath) => Path.Combine(gamePath, "Bin32Release", "Game.exe");

    public static bool IsGameFolder(string? gamePath) => gamePath != null && File.Exists(ExePath(gamePath));

    /// <summary>Папка рядом с лаунчером, если лаунчер положили в папку игры.</summary>
    public static string? Detect()
    {
        var here = AppContext.BaseDirectory;
        foreach (var candidate in new[] { here, Path.GetDirectoryName(here.TrimEnd('\\')) })
        {
            if (IsGameFolder(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Работает ли Game.exe из этой папки игры — запущенный лаунчером или через Start.bat.
    /// Процесс другой игры с тем же именем не считается.
    /// </summary>
    public static bool IsRunning(string? gamePath)
    {
        if (!IsGameFolder(gamePath)) return false;
        var exe = Path.GetFullPath(ExePath(gamePath!));
        foreach (var process in Process.GetProcessesByName("Game"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception)
                {
                    // Нет доступа к процессу (запущен от администратора) — считаем, что это наша игра
                    return true;
                }
            }
        }
        return false;
    }

    public static void Start(string gamePath, GameSession session, ServerInfo? server)
    {
        var host = server?.Host ?? session.Game.Server;
        var port = server?.Port ?? session.Game.Port;
        var args = string.Join(" ",
            "-devmode",
            "-uid", session.Uid,
            "-token", $"{session.Game.Version}~{session.Token}",
            "+online_server", host,
            "+online_server_port", port,
            "+online_use_protect 0 +online_check_certificate 0 +online_use_tls 1");

        Process.Start(new ProcessStartInfo
        {
            FileName = ExePath(gamePath),
            Arguments = args,
            // Как Start.bat: рабочая папка — корень игры
            WorkingDirectory = gamePath,
            UseShellExecute = false,
        })?.Dispose();
    }
}
