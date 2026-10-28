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

/// <summary>Строка списка игроков: отметки «в сети» и «бан».</summary>
public record PlayerRow(long Id, string Nick, int Rank, long Experience, string? Clan, string Uid, PlayerOnline? Online, bool Banned)
{
    public string Marks => string.Join("  ", new[] { Online != null ? "● в сети" : null, Banned ? "бан" : null }.Where(m => m != null));

    public System.Windows.Media.Brush MarksBrush => (System.Windows.Media.Brush)System.Windows.Application.Current.Resources[Banned ? "ErrorBrush" : "OnlineBrush"];
}

public record PlayersPage(PlayerRow[] Players, int Page, int Pages, int Total);

public record PlayerDetails(long Id, string Nick, int Rank, long Experience, string? Clan, long PvpRatingPoints, string Uid,
    MoneyInfo Money, int Items, PlayerOnline? Online, BanInfo[] Bans, string[]? MissionsUnlocked = null, string[]? MissionsLocked = null);

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

// --- Администрирование: склад, магазин, каталог предметов ---

public record DurabilityInfo(int Points, int Total);

/// <summary>Предмет на складе игрока (kind: permanent | rental | consumable | default | unlimited).</summary>
public record StorageItem(int Id, string Name, string Group, string Category, string Kind, bool Equipped,
    DurabilityInfo? Durability, long? ExpiresAt, long? SecondsLeft, int? Quantity, long? BoughtAt)
{
    public string Title => ItemNames.Title(Name, Category);

    public string State => Kind switch
    {
        "default" => "стандартный",
        "rental" => SecondsLeft > 0 ? "аренда · осталось " + ItemNames.Duration(SecondsLeft.Value) : "аренда · истёк",
        "consumable" => $"{Quantity} шт.",
        "permanent" when Durability is { } d => $"навсегда · прочность {d.Points * 100 / Math.Max(1, d.Total)}%",
        _ => "навсегда",
    } + (Equipped ? " · надет" : "");
}

public record ShopOffer(int Id, string ItemName, string Group, string Category, string Classes,
    long GameMoney, long CryMoney, long CrownMoney, long Discount, int Rank, int DurabilityPoints, long RepairCost,
    int Quantity, string ExpirationTime, string OfferStatus, string ItemCategoryOverride, bool Refundable,
    string State, ShopOffer? Original, string? UpdatedBy, string? UpdatedAt)
{
    public string Title => ItemNames.Title(ItemName, Category);

    public string Price => string.Join(" + ", new[]
    {
        GameMoney > 0 ? $"{Math.Max(0, GameMoney - Discount):N0} WB" : null,
        CryMoney > 0 ? $"{Math.Max(0, CryMoney - Discount):N0} кр." : null,
        CrownMoney > 0 ? $"{Math.Max(0, CrownMoney - Discount):N0} корон" : null,
    }.Where(p => p != null).DefaultIfEmpty("бесплатно"));

    public string Term => ExpirationTime != "" ? ItemNames.Term(ExpirationTime)
        : Quantity > 0 ? $"{Quantity} шт."
        : "навсегда";

    public string Marks => string.Join(" · ", new[]
    {
        State switch { "edited" => "изменено", "hidden" => "снято", "added" => "добавлено", _ => null },
        OfferStatus != "NORMAL" ? OfferStatus : null,
    }.Where(m => m != null));
}

public record ShopPage(ShopOffer[] Offers, int Page, int Pages, int Total, string[] Statuses);

public record GameItem(string Name, string Group, string Category, string Classes)
{
    public string Title => ItemNames.Title(Name, Category);
    public string GroupTitle => ItemNames.Group(Group);
    public string CategoryTitle => ItemNames.Category(Category);
}

public record ItemsPage(GameItem[] Items, int Page, int Pages, int Total);

/// <summary>Достижение игры с прогрессом игрока.</summary>
public record PlayerAchievement(int Id, string Kind, long Amount, string Name, string BannerType, string BannerImage,
    long Progress, bool Completed, long? CompletionTime)
{
    public string Title => $"#{Id}  {Name}";

    public string Details => $"{AchievementNames.Kind(Kind)} · {AchievementNames.Banner(BannerType)} · "
        + (Completed
            ? "выполнено" + (CompletionTime is > 0 and var t ? " " + DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime.ToString("dd.MM.yyyy") : "")
            : $"{Progress:N0} / {Amount:N0}");
}

// --- Жалобы и журнал аудита ---

/// <summary>Жалоба игрока из игры (меню игрока → «Пожаловаться»).</summary>
public record Complaint(long Id, DateTime CreatedAt, string Source, string? ReporterUid, string? ReporterNick,
    string? TargetUid, string TargetNick, string Type, string? Comment, string? Channel, int? RoomId,
    string Status, string? Note, string? HandledBy, DateTime? HandledAt)
{
    public static readonly Dictionary<string, string> Types = new()
    {
        ["speed_bot"] = "спидхак",
        ["aim_bot"] = "аимбот",
        ["perspective_hacking"] = "волхак (видит сквозь стены)",
        ["verbal_abuse"] = "оскорбления",
        ["master_does_not_start_room"] = "мастер не запускает комнату",
        ["other"] = "другое",
    };

    public static readonly (string? Id, string Title)[] Statuses =
    [
        ("new", "Новые"),
        ("in_progress", "В работе"),
        ("resolved", "Решённые"),
        ("rejected", "Отклонённые"),
        (null, "Все"),
    ];

    public string TypeTitle => Types.GetValueOrDefault(Type, Type);

    public string StatusTitle => Status switch
    {
        "new" => "новая",
        "in_progress" => "в работе",
        "resolved" => "решена",
        "rejected" => "отклонена",
        _ => Status,
    };

    public string Header => $"#{Id} · {CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} · {StatusTitle}";

    public string Who => $"{ReporterNick ?? "?"} → {TargetNick}";

    public string Preview => TypeTitle + (string.IsNullOrWhiteSpace(Comment) ? "" : " · " + (Comment!.Length > 70 ? Comment[..70] + "…" : Comment));
}

public record ComplaintsPage(Complaint[] Complaints, int Page, int Pages, int Total, Dictionary<string, int> Counts);

/// <summary>Запись журнала аудита: кто, откуда, что изменил и чем ответил сервер.</summary>
public record AuditEntry(long Id, DateTime At, int? KeyId, string? KeyName, string Role, string? Ip,
    string Method, string Path, JsonElement? Body, int Code, string? Reason)
{
    public string Header => $"{At.ToLocalTime():dd.MM.yyyy HH:mm:ss} · {KeyName ?? "без ключа"} ({Role})" + (Ip != null ? " · " + Ip : "");

    public string Line => $"{Method} {Uri.UnescapeDataString(Path)} → {Code}" + (string.IsNullOrEmpty(Reason) ? "" : " " + Reason);

    public bool Failed => Code >= 400;

    public string BodyText => Body is { ValueKind: JsonValueKind.Object } body && body.EnumerateObject().Any()
        ? JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
        : "(без тела)";
}

// --- PvE-миссии и сервер ---

public static class MissionNames
{
    public static string Difficulty(string difficulty) => difficulty switch
    {
        "easy" => "лёгкая",
        "normal" => "нормальная",
        "hard" => "сложная",
        _ => difficulty,
    };

    public static string Type(string type) => type switch
    {
        "trainingmission" => "Обучение",
        "easymission" => "Лёгкая",
        "normalmission" => "Нормальная",
        "hardmission" => "Сложная",
        _ => type,
    };
}

/// <summary>Текущая PvE-миссия из кэша.</summary>
public record MissionInfo(string Uid, string Type, string Difficulty, string Name, string TimeOfDay, string Map, string MapTitle,
    string[] Sublevels, int SecondaryObjectives)
{
    public string Title => $"{MissionNames.Type(Type)} · {MapTitle}";
    public string Details => $"{string.Join(" → ", Sublevels)} · {TimeOfDay} · доп. заданий: {SecondaryObjectives}";
}

public record MissionsData(DateTime? GeneratedAt, MissionInfo[] Missions);

public record SublevelInfo(string Name, string Kind);

public record MapInfo(string Map, string Title, SublevelInfo[] Sublevels);

public record GraphInfo(string Name, string Map, int Stages, string? Boss);

/// <summary>Настройки одного типа миссий; null — как в исходном конфиге.</summary>
public record TypeSettings(bool? Enabled, string[]? Sublevels, string[]? Graphs, string? TimeOfDay, int? SecondaryMin, int? SecondaryMax);

public record MissionTypeInfo(string Name, string Difficulty, string[] Chain, TypeSettings Defaults, GraphInfo[] Graphs);

public record SavedMissionSettings(Dictionary<string, TypeSettings> Types, Dictionary<string, bool> Publish);

public record MissionSettingsData(SavedMissionSettings? Settings, string? UpdatedBy, string? UpdatedAt, MapInfo[] Maps, MissionTypeInfo[] Types);

public record ComponentState(string Name, string State, string? Channel, int? MinRank, int? MaxRank);

public record ServerStatus(int? Online, ComponentState[] Components, ComponentState[] Channels, ComponentState[]? Dedicated);

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
        // PostgreSQL отдаёт bigint (uid аккаунта) строкой: "uid": "1"
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
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

    public Task<PlayersPage> GetPlayersAsync(string search, int page) =>
        SendAsync<PlayersPage>(HttpMethod.Get, $"/v1/mod/players?page={page}&search={Uri.EscapeDataString(search)}");

    public Task<PlayerDetails> GetPlayerAsync(string nick) => SendAsync<PlayerDetails>(HttpMethod.Get, "/v1/mod/players/" + Uri.EscapeDataString(nick));

    public Task<string> MessageAsync(string nick, string text) =>
        SendForReasonAsync(HttpMethod.Post, "/v1/mod/players/" + Uri.EscapeDataString(nick) + "/message", new { text });

    /// <summary>Смена ника; kick — сначала отключить онлайн-игрока (ник меняется после его выхода).</summary>
    public Task<string> RenameAsync(string nick, string newNick, bool kick) =>
        SendForReasonAsync(HttpMethod.Post, "/v1/mod/players/" + Uri.EscapeDataString(nick) + "/rename", new { nick = newNick, kick });

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

    // --- Администратор: профиль и склад игрока ---

    private static string PlayerPath(string nick) => "/v1/admin/players/" + Uri.EscapeDataString(nick);

    /// <summary>Точные значения опыта и денег (только переданные поля).</summary>
    public Task<string> SetProfileAsync(string nick, Dictionary<string, long> values) =>
        SendForReasonAsync(HttpMethod.Patch, PlayerPath(nick), values);

    public Task<StorageItem[]> GetStorageAsync(string nick) => SendAsync<StorageItem[]>(HttpMethod.Get, PlayerPath(nick) + "/items");

    /// <summary>Правка предмета: { repair = true } | { permanent = true } | { hours } | { quantity }.</summary>
    public Task<string> EditItemAsync(string nick, int itemId, object change) =>
        SendForReasonAsync(HttpMethod.Patch, PlayerPath(nick) + "/items/" + itemId, change);

    public Task<string> RemoveItemAsync(string nick, int itemId) =>
        SendForReasonAsync(HttpMethod.Delete, PlayerPath(nick) + "/items/" + itemId);

    /// <summary>Выдача: { items = [{ name, days | quantity }] } и/или game_money, crown_money, experience.</summary>
    public Task<string> GiveAsync(string nick, object gift) => SendForReasonAsync(HttpMethod.Post, PlayerPath(nick) + "/give", gift);

    // --- Администратор: магазин и каталог предметов ---

    public Task<ShopPage> GetShopAsync(string search, string? group, string? state, int page) =>
        SendAsync<ShopPage>(HttpMethod.Get, $"/v1/admin/shop?page={page}&search={Uri.EscapeDataString(search)}"
            + (group != null ? "&group=" + group : "") + (state != null ? "&state=" + state : ""));

    public Task<string> UpdateOfferAsync(int id, Dictionary<string, object> changes) =>
        SendForReasonAsync(HttpMethod.Patch, "/v1/admin/shop/" + id, changes);

    public Task<string> AddOfferAsync(Dictionary<string, object> offer) => SendForReasonAsync(HttpMethod.Post, "/v1/admin/shop", offer);

    public Task<string> HideOfferAsync(int id) => SendForReasonAsync(HttpMethod.Delete, "/v1/admin/shop/" + id);

    public Task<string> ShowOfferAsync(int id) => SendForReasonAsync(HttpMethod.Post, $"/v1/admin/shop/{id}/show");

    public Task<string> ResetOfferAsync(int id) => SendForReasonAsync(HttpMethod.Post, $"/v1/admin/shop/{id}/reset");

    /// <summary>Открыть (true) или закрыть (false) PvE-сложности: trainingmission, easymission, normalmission, hardmission.</summary>
    public Task<string> SetMissionAccessAsync(string nick, Dictionary<string, bool> access) =>
        SendForReasonAsync(HttpMethod.Patch, PlayerPath(nick) + "/missions", access);

    public Task<PlayerAchievement[]> GetAchievementsAsync(string nick) =>
        SendAsync<PlayerAchievement[]>(HttpMethod.Get, PlayerPath(nick) + "/achievements");

    /// <summary>{ achievements = [{ id, completed } | { id, progress }] } или { all = true | false }.</summary>
    public Task<string> SetAchievementsAsync(string nick, object change) =>
        SendForReasonAsync(HttpMethod.Patch, PlayerPath(nick) + "/achievements", change);

    // --- Жалобы (модератор) и журнал аудита (админ) ---

    public Task<ComplaintsPage> GetComplaintsAsync(string? status, string? target, int page) =>
        SendAsync<ComplaintsPage>(HttpMethod.Get, $"/v1/mod/complaints?page={page}"
            + (status != null ? "&status=" + status : "") + (target != null ? "&target=" + Uri.EscapeDataString(target) : ""));

    public Task<string> UpdateComplaintAsync(long id, string? status, string? note) =>
        SendForReasonAsync(HttpMethod.Patch, "/v1/mod/complaints/" + id, new { status, note });

    public Task<AuditEntry[]> GetAuditAsync(long? before, string search, bool errorsOnly) =>
        SendAsync<AuditEntry[]>(HttpMethod.Get, "/v1/admin/audit?limit=50" + (before != null ? "&before=" + before : "")
            + (search != "" ? "&search=" + Uri.EscapeDataString(search) : "") + (errorsOnly ? "&errors=1" : ""));

    // --- PvE-миссии и обслуживание сервера (админ) ---

    public Task<MissionsData> GetMissionsAsync() => SendAsync<MissionsData>(HttpMethod.Get, "/v1/admin/missions");

    public Task<string> SetMissionTimeAsync(string uid, string time) =>
        SendForReasonAsync(HttpMethod.Patch, "/v1/admin/missions/" + Uri.EscapeDataString(uid), new { time_of_day = time });

    public Task<string> RemoveMissionAsync(string uid) =>
        SendForReasonAsync(HttpMethod.Delete, "/v1/admin/missions/" + Uri.EscapeDataString(uid));

    public Task<string> RegenerateMissionsAsync() => SendForReasonAsync(HttpMethod.Post, "/v1/admin/missions/regenerate", new { });

    public Task<MissionSettingsData> GetMissionSettingsAsync() => SendAsync<MissionSettingsData>(HttpMethod.Get, "/v1/admin/missions/settings");

    /// <summary>Сохранить настройки генерации; возвращает предупреждения о несовместимых вариантах.</summary>
    public async Task<string[]> SaveMissionSettingsAsync(object settings)
    {
        var data = await SendAsync<JsonElement>(HttpMethod.Put, "/v1/admin/missions/settings", settings);
        return data.TryGetProperty("warnings", out var warnings) ? warnings.Deserialize<string[]>() ?? [] : [];
    }

    public Task<string> ResetMissionSettingsAsync() => SendForReasonAsync(HttpMethod.Delete, "/v1/admin/missions/settings");

    public Task<ServerStatus> GetStatusAsync() => SendAsync<ServerStatus>(HttpMethod.Get, "/v1/status");

    public Task<Dictionary<string, string>> RebuildCachesAsync(params string[] modules) =>
        SendAsync<Dictionary<string, string>>(HttpMethod.Post, "/v1/admin/caches/rebuild", new { modules });

    public Task<string> BackupAsync() => SendForReasonAsync(HttpMethod.Post, "/v1/admin/backup", new { });

    public Task<ItemsPage> GetItemsAsync(string search, string? group, int page) =>
        SendAsync<ItemsPage>(HttpMethod.Get, $"/v1/admin/items?page={page}&search={Uri.EscapeDataString(search)}" + (group != null ? "&group=" + group : ""));

    // Ответ сервера по-русски для сообщений в панели модерации
    private static readonly Dictionary<string, string> Results = new()
    {
        ["Message delivered"] = "Сообщение доставлено",
        ["Player is offline, message will be shown at next login"] = "Игрок не в сети: сообщение покажется при входе",
        ["Player kicked"] = "Игрок исключён с сервера",
        ["Nickname changed"] = "Ник изменён",
        ["Complaint updated"] = "Жалоба обновлена",
        ["Mission updated"] = "Миссия изменена — игроки увидят через ~10 с",
        ["Mission removed"] = "Миссия убрана до следующей генерации",
        ["Missions regenerated"] = "Миссии сгенерированы заново — игроки увидят через ~10 с",
        ["Mission settings reset"] = "Настройки генерации сброшены к исходным",
        ["Backup created in /var/backups/warface"] = "Резервная копия создана (/var/backups/warface)",
        ["Player banned permanently"] = "Игрок заблокирован бессрочно",
        ["Ban revoked"] = "Бан снят",
        ["Invite revoked"] = "Ключ отозван",
        ["Role set"] = "Роль изменена",
        ["Password set"] = "Пароль изменён",
        ["Profile updated"] = "Профиль сохранён",
        ["Item updated"] = "Предмет изменён",
        ["Item removed"] = "Предмет удалён со склада",
        ["Offer updated"] = "Предложение сохранено, в магазине игроков — через ~10 с",
        ["Offer added"] = "Предложение добавлено, в магазине игроков — через ~10 с",
        ["Offer hidden"] = "Снято с продажи",
        ["Offer is on sale again"] = "Снова в продаже",
        ["Offer restored to original"] = "Возвращены исходные значения",
        ["Added offer deleted"] = "Добавленное предложение удалено",
        ["Achievements updated"] = "Достижения изменены",
        ["Mission access updated"] = "Доступ к миссиям изменён",
        ["Achievements unchanged"] = "Ничего не изменилось",
        ["Queued, the player will receive it within ~10 seconds"] = "Выдано: игрок получит в течение ~10 с",
        ["Queued, the player will receive it at next login"] = "Выдано: игрок получит при следующем входе",
    };

    private async Task<string> SendForReasonAsync(HttpMethod method, string path, object? body = null)
    {
        var reason = await SendAsync<string>(method, path, body, reasonOnly: true);
        if (Loc.English) return reason;
        if (Results.TryGetValue(reason, out var translated)) return translated;
        if (reason.StartsWith("Player banned for ")) return "Игрок заблокирован на " + reason["Player banned for ".Length..].Replace("hours", "ч");
        // Правки онлайн-игрока и пересборка магазина: «<результат>: <пояснение>»
        if (reason.EndsWith(": the player is online, changes apply within ~10 seconds and are visible after relogin"))
            return Results.GetValueOrDefault(reason[..reason.IndexOf(':')], reason) + ". Игрок в сети: применится за ~10 с, увидит после перезахода";
        if (reason.EndsWith(": queued, applied within ~10 seconds after the player logs in"))
            return Results.GetValueOrDefault(reason[..reason.IndexOf(':')], reason) + ". Применится, когда игрок зайдёт";
        if (reason.Contains(" (shop cache rebuild failed"))
            return Results.GetValueOrDefault(reason[..reason.IndexOf(" (")], reason) + ", но кэш магазина не пересобрался: выполните на сервере warface cache rebuild shop";
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
            throw new ApiException(Loc.T("Api.Unreachable"), 0);
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var code = root.TryGetProperty("code", out var c) ? c.GetInt32() : (int)response.StatusCode;
        if (code >= 400)
        {
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
            if (Loc.English) throw new ApiException(reason == "" ? Loc.T("Api.Error") : reason, code);
            throw new ApiException(Reasons.GetValueOrDefault(reason, Translate(reason)), code);
        }
        if (reasonOnly) return (T)(object)(root.GetProperty("reason").GetString() ?? "");
        var data = root.GetProperty("data");
        try
        {
            return data.Deserialize<T>(Json)!;
        }
        catch (JsonException error)
        {
            // Ответ не того вида, что ждёт лаунчер (сервер новее/старее) — ошибка, а не падение приложения
            throw new ApiException(Loc.T("Api.BadResponse") + " (" + path.Split('?')[0] + ": " + error.Message + ")", 0);
        }
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
        _ when reason.StartsWith("Complaint ") && reason.EndsWith(" not found") => "Жалоба не найдена",
        _ when reason.StartsWith("Mission ") && reason.EndsWith(" not found") => "Миссия уже сменилась — обновите список",
        _ when reason.StartsWith("time_of_day must be") => "Время суток: Ч:ММ, например 9:06 или 20:30",
        _ when reason.StartsWith("Mission generation failed") => "Генерация миссий не удалась — подробности в журнале сервера",
        _ when reason.StartsWith("Cache rebuild failed") => "Пересборка кэша не удалась: " + reason[22..],
        _ when reason.StartsWith("Backup failed") => "Резервная копия не создана: " + reason,
        "Player already has this nickname" => "У игрока уже этот ник",
        _ when reason.StartsWith("Nickname must be") => "Ник: 4–16 символов, только кириллица или только латиница, цифры и - . _",
        _ when reason.StartsWith("Nickname '") && reason.EndsWith("' is already taken") => "Ник " + reason[10..^18] + " уже занят",
        _ when reason.Contains("is online: kick first") => "Игрок в сети: ник меняется только после выхода из игры",
        _ when reason.StartsWith("Player was kicked but is still listed online") => "Игрок отключён, но сервер ещё не отпустил его — повторите через несколько секунд",
        _ when reason.StartsWith("Cannot verify that the player is offline") => "Не удалось проверить, что игрок вышел: часть игровых каналов не отвечает",
        "Default equipment cannot be removed" => "Стандартное снаряжение удалить нельзя",
        _ when reason.EndsWith("is equipped and cannot be removed") => "Предмет надет на персонажа, удалить нельзя",
        _ when reason.StartsWith("Item ") && reason.EndsWith(" not found in the profile") => "Предмета уже нет на складе, обновите список",
        _ when reason.EndsWith(" has no durability") => "У этого предмета нет прочности",
        _ when reason.EndsWith(" is not a rental item") => "Это не арендованный предмет",
        _ when reason.EndsWith(" is not a consumable") => "Это не расходный предмет",
        _ when reason.StartsWith("Unknown item '") => "Такого предмета нет в игре",
        _ when reason.StartsWith("Achievement ") && reason.EndsWith(" not found") => "Такого достижения нет",
        _ when reason.StartsWith("progress of achievement ") => "Прогресс: целое число от 0 до " + reason[(reason.LastIndexOf(' ') + 1)..],
        _ when reason.StartsWith("Invalid fields: ") => "Неверные значения: " + reason[16..],
        _ when reason.StartsWith("Offer ") && reason.EndsWith(" not found") => "Предложение не найдено",
        _ when reason.EndsWith(" is not hidden") => "Предложение и так в продаже",
        _ when reason.EndsWith(" was added via API: delete it instead") => "Это добавленное предложение: его можно только удалить",
        "" => "Ошибка сервера",
        _ => reason,
    };
}
