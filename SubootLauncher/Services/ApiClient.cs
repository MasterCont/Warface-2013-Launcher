using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SubootLauncher.Services;

// --- Модели ответов API (имена полей как в JSON: snake_case) ---

public record ServerInfo(
    string Id,
    string Name,
    string Host,
    int Port,
    string Version,
    string State,
    int? Online);

public record MoneyInfo(long Game, long Crown, long Cry);

public record ProfileInfo(
    long Id,
    string Nick,
    int Rank,
    long Experience,
    string? Clan,
    [property: JsonPropertyName("pvp_rating_points")] long PvpRatingPoints,
    MoneyInfo Money);

public record AccountInfo(
    long Uid,
    string Login,
    string Role,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("last_login_at")] DateTime? LastLoginAt,
    ProfileInfo? Profile)
{
    public bool IsStaff => Role is "moderator" or "admin";
}

public record GameInfo(string Version, string Server, int Port);

public record GameSession(
    long Uid,
    string Login,
    string Token,
    string? Session,
    GameInfo Game,
    string[] Args);

/// <summary>Ошибка API с понятным игроку текстом.</summary>
public class ApiException(string message, int code) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>
/// Клиент REST API сервера (https://api.wf.suboot.pro). Ответ всегда { code, status, reason, data }.
/// </summary>
public class ApiClient
{
    public const string BaseUrl = "https://api.wf.suboot.pro";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Тексты ответов API → по-русски
    private static readonly Dictionary<string, string> Reasons = new()
    {
        ["Wrong login or password"] = "Неверный логин или пароль",
        ["Login is already taken"] = "Этот логин уже занят",
        ["Registration is closed"] = "Регистрация закрыта",
        ["Invite key is required"] = "Нужен ключ приглашения от администратора",
        ["Invite key is invalid, used or expired"] = "Ключ приглашения неверный, уже использован или истёк",
        ["Too many requests, try again later"] = "Слишком много попыток, попробуйте позже",
        ["Too many failed attempts, try again later"] = "Слишком много неудачных попыток, попробуйте через 10 минут",
        ["Session expired, log in again"] = "Сессия истекла, войдите снова",
        ["Account is disabled"] = "Аккаунт отключён",
    };

    private readonly HttpClient _http = new() { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Сессия модератора/админа для методов модерации (Authorization: Bearer wfs_…).</summary>
    public string? StaffSession { get; set; }

    public Task<ServerInfo[]> GetServersAsync() => SendAsync<ServerInfo[]>(HttpMethod.Get, "/v1/servers");

    public async Task<string> GetRegistrationModeAsync()
    {
        var data = await SendAsync<JsonElement>(HttpMethod.Get, "/v1/auth/registration");
        return data.GetProperty("mode").GetString() ?? "invite";
    }

    public Task<GameSession> LoginAsync(string login, string password, bool remember) =>
        SendAsync<GameSession>(HttpMethod.Post, "/v1/auth/login", new { login, password, remember });

    public Task RegisterAsync(string login, string password, string invite) =>
        SendAsync<JsonElement>(HttpMethod.Post, "/v1/auth/register", new { login, password, invite });

    public Task<GameSession> LoginBySessionAsync(string session) =>
        SendAsync<GameSession>(HttpMethod.Post, "/v1/auth/session", new { session });

    public Task<AccountInfo> GetMeAsync(string session) =>
        SendAsync<AccountInfo>(HttpMethod.Post, "/v1/auth/me", new { session });

    public Task ChangePasswordAsync(string login, string password, string newPassword) =>
        SendAsync<JsonElement>(HttpMethod.Post, "/v1/auth/password", new { login, password, new_password = newPassword });

    public Task LogoutAsync(string session) =>
        SendAsync<JsonElement>(HttpMethod.Post, "/v1/auth/logout", new { session });

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = JsonContent.Create(body);
        if (StaffSession != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StaffSession);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception)
        {
            throw new ApiException("Сервер недоступен. Проверьте интернет и попробуйте ещё раз", 0);
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var code = root.TryGetProperty("code", out var c) ? c.GetInt32() : (int)response.StatusCode;
        if (code >= 400)
        {
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
            throw new ApiException(Reasons.GetValueOrDefault(reason, Translate(reason)), code);
        }
        var data = root.GetProperty("data");
        return data.Deserialize<T>(Json)!;
    }

    private static string Translate(string reason) => reason switch
    {
        _ when reason.StartsWith("Login must") => "Логин: 3–24 символа, латинские буквы, цифры, _ . -",
        _ when reason.StartsWith("Password must") => "Пароль: от 6 до 128 символов",
        "" => "Ошибка сервера",
        _ => reason,
    };
}
