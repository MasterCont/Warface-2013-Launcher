using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SubootLauncher.Services;

/// <summary>Сохранённая учётная запись: логин и сессия (уже расшифрованная).</summary>
public record SavedAccount(string Login, string Session, DateTime LastUsed);

/// <summary>
/// Несколько сохранённых учётных записей в %APPDATA%\SubootGames\launcher.accounts. Файл целиком шифруется DPAPI
/// (как раньше одна сессия): на другом компьютере или под другим пользователем Windows его не прочитать.
/// Текущая учётная запись — последняя использованная.
/// </summary>
public static class AccountStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SubootGames");
    private static readonly string File = Path.Combine(Folder, "launcher.accounts");

    private static List<SavedAccount>? _accounts;

    public static IReadOnlyList<SavedAccount> All => Load().OrderByDescending(a => a.LastUsed).ToList();

    public static SavedAccount? Current => All.FirstOrDefault();

    public static SavedAccount? Find(string login) =>
        Load().FirstOrDefault(a => string.Equals(a.Login, login, StringComparison.OrdinalIgnoreCase));

    /// <summary>Сохранить (или обновить) учётную запись и сделать её текущей.</summary>
    public static void Save(string login, string session)
    {
        var accounts = Load();
        accounts.RemoveAll(a => string.Equals(a.Login, login, StringComparison.OrdinalIgnoreCase));
        accounts.Add(new SavedAccount(login, session, DateTime.UtcNow));
        Write(accounts);
    }

    /// <summary>Отметить учётную запись текущей (при переключении).</summary>
    public static void Touch(string login)
    {
        if (Find(login) is { } account) Save(account.Login, account.Session);
    }

    /// <summary>Обновить сессию, только если учётная запись сохранена (смена пароля выдаёт новую).</summary>
    public static void Replace(string login, string session)
    {
        var accounts = Load();
        var index = accounts.FindIndex(a => string.Equals(a.Login, login, StringComparison.OrdinalIgnoreCase));
        if (index == -1) return;
        accounts[index] = accounts[index] with { Session = session };
        Write(accounts);
    }

    public static void Remove(string login)
    {
        var accounts = Load();
        accounts.RemoveAll(a => string.Equals(a.Login, login, StringComparison.OrdinalIgnoreCase));
        Write(accounts);
    }

    private static List<SavedAccount> Load()
    {
        if (_accounts != null) return _accounts;
        try
        {
            var bytes = ProtectedData.Unprotect(System.IO.File.ReadAllBytes(File), null, DataProtectionScope.CurrentUser);
            _accounts = JsonSerializer.Deserialize<List<SavedAccount>>(Encoding.UTF8.GetString(bytes)) ?? [];
        }
        catch
        {
            _accounts = [];
        }
        return _accounts;
    }

    private static void Write(List<SavedAccount> accounts)
    {
        _accounts = accounts;
        Directory.CreateDirectory(Folder);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(accounts)), null, DataProtectionScope.CurrentUser);
        System.IO.File.WriteAllBytes(File, bytes);
    }

    /// <summary>Перенос единственной сессии прежних версий лаунчера (launcher.session) — логин узнаём у сервера.</summary>
    public static async Task MigrateAsync(ApiClient api)
    {
        var legacy = Settings.LoadSession();
        if (legacy == null) return;
        try
        {
            var me = await api.GetMeAsync(legacy);
            if (Find(me.Login) == null) Save(me.Login, legacy);
            Settings.ForgetSession();
        }
        catch (ApiException error) when (error.Code == 401)
        {
            Settings.ForgetSession();
        }
        catch (ApiException)
        {
            // Сервер недоступен — перенесём в следующий раз
        }
    }
}
