using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SubootLauncher.Services;

/// <summary>
/// Настройки лаунчера в %APPDATA%\SubootGames: папка игры, выбранный сервер, сохранённая сессия.
/// Сессия шифруется DPAPI (как в Start.bat): скопировав файл на другой компьютер, ей не воспользоваться.
/// </summary>
public class Settings
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SubootGames");
    private static readonly string File = Path.Combine(Folder, "launcher.json");
    private static readonly string SessionFile = Path.Combine(Folder, "launcher.session");

    public string? GamePath { get; set; }
    public string? ServerId { get; set; }
    /// <summary>Оформление: classic (сайт 2013, по умолчанию) или game (как в игре).</summary>
    public string? Theme { get; set; }
    /// <summary>Язык интерфейса: ru (по умолчанию) или en.</summary>
    public string? Language { get; set; }

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File)) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string? LoadSession()
    {
        try
        {
            var bytes = ProtectedData.Unprotect(System.IO.File.ReadAllBytes(SessionFile), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveSession(string session)
    {
        Directory.CreateDirectory(Folder);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(session), null, DataProtectionScope.CurrentUser);
        System.IO.File.WriteAllBytes(SessionFile, bytes);
    }

    public static void ForgetSession()
    {
        try { System.IO.File.Delete(SessionFile); } catch { /* файла может не быть */ }
    }
}
