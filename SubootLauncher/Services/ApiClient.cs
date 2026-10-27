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

// --- Модерация ---

public record OnlinePlayer(long ProfileId, string Uid, string Nick, long Experience, int Rank, string Channel, int? RoomId, bool InMatch)
{
    public string Where => InMatch ? "в матче" : RoomId != null ? "в комнате" : "в лобби";
}

public record OnlineData(OnlinePlayer[] Players, string[] UnavailableChannels);

public record BanInfo(int Id, string Uid, string? Nick, string Reason, DateTime? Until, string CreatedBy, DateTime CreatedAt,
    DateTime? RevokedAt, string? RevokedBy, bool? Active)
{
    public string UntilText => Until is { } until ? "до " + until.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "бессрочно";
    public string Summary => $"{Nick} · {UntilText} · {Reason} · выдал {CreatedBy}" + (RevokedAt != null ? $" · снят ({RevokedBy})" : "");
}

public record PlayerOnline(string Channel, int? RoomId, bool InMatch);

public record PlayerDetails(long Id, string Nick, int Rank, long Experience, string? Clan, long PvpRatingPoints, string Uid,
    MoneyInfo Money, int Items, PlayerOnline? Online, BanInfo[] Bans);

public record InviteUser(long Uid, string Login, DateTime At);

public record InviteInfo(int Id, string CodeHint, string? Note, int MaxUses, int Uses, DateTime CreatedAt, DateTime? ExpiresAt,
    DateTime? RevokedAt, InviteUser[] UsedBy)
{
    public string State => RevokedAt != null ? "отозван" : Uses >= MaxUses ? "использован"
        : ExpiresAt is { } expires && expires < DateTime.UtcNow ? "истёк" : "активен";
    public string Summary => $"#{Id}  {CodeHint}  {State}  {Uses}/{MaxUses}  {Note}"
        + (UsedBy.Length > 0 ? "  → " + string.Join(", ", UsedBy.Select(u => u.Login)) : "");
}

public record CreatedInvite(string Code, int Id, string? Note, int MaxUses, DateTime? ExpiresAt);

public record AccountRow(long Uid, string Login, string Role, DateTime CreatedAt, DateTime? LastLoginAt, DateTime? DisabledAt, string? Nick)
{
    public string Summary => $"{Uid,-4} {Login}  ·  {Nick ?? "персонаж не создан"}"
        + (Role != "player" ? $"  ·  {Role}" : "")
        + (LastLoginAt is { } last ? $"  ·  вход {last.ToLocalTime():dd.MM HH:mm}" : "");
}

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
        ["Only an admin can ban permanently, specify hours"] = "Бессрочно банит только админ — укажите срок в часах",
        ["No masterserver channel is reachable"] = "Игровые каналы недоступны",
        ["Active invite not found"] = "Активный ключ не найден",
        ["You cannot remove your own admin role"] = "Нельзя снять админку с самого себя",
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

    // --- Модерация (нужна сессия модератора или админа в StaffSession) ---

    public Task<OnlineData> GetOnlineAsync() => SendAsync<OnlineData>(HttpMethod.Get, "/v1/mod/online");

    public Task<PlayerDetails> GetPlayerAsync(string nick) => SendAsync<PlayerDetails>(HttpMethod.Get, "/v1/mod/players/" + Uri.EscapeDataString(nick));

    public Task<string> MessageAsync(string nick, string text) =>
        SendForReasonAsync(HttpMethod.Post, "/v1/mod/players/" + Uri.EscapeDataString(nick) + "/message", new { text });

    public Task<string> KickAsync(string nick) =>
        SendForReasonAsync(HttpMethod.Post, "/v1/mod/players/" + Uri.EscapeDataString(nick) + "/kick", new { });

    public Task<string> BanAsync(string nick, double? hours, string reason) =>
        SendForReasonAsync(HttpMethod.Post, "/v1/mod/players/" + Uri.EscapeDataString(nick) + "/ban", new { hours, reason });

    public Task<string> UnbanAsync(string nick) =>
        SendForReasonAsync(HttpMethod.Delete, "/v1/mod/players/" + Uri.EscapeDataString(nick) + "/ban");

    public Task<BanInfo[]> GetBansAsync() => SendAsync<BanInfo[]>(HttpMethod.Get, "/v1/mod/bans");

    public Task<string> BroadcastAsync(string text) => SendForReasonAsync(HttpMethod.Post, "/v1/mod/broadcast", new { text });

    // --- Только админ ---

    public Task<InviteInfo[]> GetInvitesAsync() => SendAsync<InviteInfo[]>(HttpMethod.Get, "/v1/admin/invites");

    public Task<CreatedInvite> CreateInviteAsync(string? note, int uses, double? days) =>
        SendAsync<CreatedInvite>(HttpMethod.Post, "/v1/admin/invites", new { note, uses, days });

    public Task<string> RevokeInviteAsync(int id) => SendForReasonAsync(HttpMethod.Delete, "/v1/admin/invites/" + id);

    public Task<AccountRow[]> GetAccountsAsync() => SendAsync<AccountRow[]>(HttpMethod.Get, "/v1/admin/accounts");

    public Task<string> SetRoleAsync(string login, string role) =>
        SendForReasonAsync(HttpMethod.Post, "/v1/admin/accounts/" + Uri.EscapeDataString(login) + "/role", new { role });

    public Task<string> SetPasswordAsync(string login, string password) =>
        SendForReasonAsync(HttpMethod.Post, "/v1/admin/accounts/" + Uri.EscapeDataString(login) + "/password", new { password });

    // Ответ сервера по-русски для сообщений в панели модерации
    private static readonly Dictionary<string, string> Results = new()
    {
        ["Message delivered"] = "Сообщение доставлено",
        ["Player is offline, message will be shown at next login"] = "Игрок не в сети: сообщение покажется при входе",
        ["Player kicked"] = "Игрок исключён с сервера",
        ["Player banned permanently"] = "Игрок заблокирован бессрочно",
        ["Ban revoked"] = "Бан снят",
        ["Invite revoked"] = "Ключ отозван",
        ["Role set"] = "Роль изменена",
        ["Password set"] = "Пароль изменён",
    };

    private async Task<string> SendForReasonAsync(HttpMethod method, string path, object? body = null)
    {
        var reason = await SendAsync<string>(method, path, body, reasonOnly: true);
        if (Results.TryGetValue(reason, out var translated)) return translated;
        if (reason.StartsWith("Player banned for ")) return "Игрок заблокирован на " + reason["Player banned for ".Length..].Replace("hours", "ч");
        if (reason.StartsWith("Message sent to ")) return "Сообщение отправлено, получателей: " + reason["Message sent to ".Length..].Replace(" players", "");
        return reason;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, bool reasonOnly = false)
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
        if (reasonOnly) return (T)(object)(root.GetProperty("reason").GetString() ?? "");
        var data = root.GetProperty("data");
        return data.Deserialize<T>(Json)!;
    }

    private static string Translate(string reason) => reason switch
    {
        _ when reason.StartsWith("Login must") => "Логин: 3–24 символа, латинские буквы, цифры, _ . -",
        _ when reason.StartsWith("Password must") => "Пароль: от 6 до 128 символов",
        _ when reason.StartsWith("Player '") && reason.EndsWith("' not found") => "Игрок " + reason[7..^11] + " не найден",
        _ when reason.EndsWith("' is not online") => "Игрок " + reason[8..^15] + " сейчас не в сети",
        _ when reason.EndsWith("' has no active ban") => "У игрока " + reason[8..^19] + " нет активного бана",
        _ when reason.StartsWith("Moderators can ban for at most") => "Модератор может банить максимум на " + reason[31..].Replace(" hours", " ч"),
        _ when reason.StartsWith("Role '") => "Недостаточно прав для этого действия",
        "" => "Ошибка сервера",
        _ => reason,
    };
}
