namespace SubootLauncher.Services;

/// <summary>
/// Подписи достижений по-русски. Названия игры лежат в зашифрованном Russian.pak, поэтому
/// показываем ключ названия (например, destroyer1) и русский тип условия.
/// </summary>
public static class AchievementNames
{
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kills"] = "убийства",
        ["damage_done"] = "нанесённый урон",
        ["damage_taken"] = "полученный урон",
        ["game_end"] = "завершённые матчи",
        ["rank"] = "звание",
        ["heal"] = "лечение",
        ["resurrects_done"] = "воскрешения",
        ["climb_assist"] = "подсадки",
        ["PPES_up"] = "броня союзникам",
        ["crown_collected"] = "короны",
        ["money"] = "деньги",
        ["flag_captured"] = "захват флага",
        ["bomb_explode"] = "подрыв бомбы",
        ["add_friend"] = "друзья",
        ["leader_board_update"] = "таблица лидеров",
        ["instant_award"] = "особая награда",
        ["sponsor_progress"] = "поставщики",
    };

    public static readonly (string? Id, string Title)[] Banners =
    [
        (null, "Все награды"),
        ("badge", "Значки"),
        ("mark", "Нашивки"),
        ("stripe", "Полосы"),
    ];

    public static string Kind(string kind) => Kinds.GetValueOrDefault(kind, kind);

    public static string Banner(string type) => type switch
    {
        "badge" => "значок",
        "mark" => "нашивка",
        "stripe" => "полоса",
        _ => type,
    };
}
