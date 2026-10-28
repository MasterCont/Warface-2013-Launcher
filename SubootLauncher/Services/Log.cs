using System.IO;
using System.Reflection;

namespace SubootLauncher.Services;

/// <summary>
/// Журнал лаунчера %APPDATA%\SubootGames\launcher.log: запуск и ошибки. Если у игрока лаунчер не открывается,
/// этот файл можно прислать администратору. Больше 1 МБ — начинается заново.
/// </summary>
public static class Log
{
    public static readonly string File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SubootGames", "launcher.log");

    private static readonly object Sync = new();

    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                if (System.IO.File.Exists(File) && new FileInfo(File).Length > 1024 * 1024) System.IO.File.Delete(File);
                System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Журнал не должен ронять лаунчер
        }
    }

    public static void Error(string where, Exception error) => Write($"ОШИБКА {where}: {error}");
}
