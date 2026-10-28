namespace SubootLauncher.Services;

/// <summary>
/// Подписи предметов по-русски. Настоящие названия игры лежат в зашифрованном Russian.pak,
/// поэтому показываем внутреннее имя предмета и русскую категорию.
/// </summary>
public static class ItemNames
{
    private static readonly Dictionary<string, string> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AssaultRifle"] = "штурмовая винтовка",
        ["SMG"] = "пистолет-пулемёт",
        ["SniperRifle"] = "снайперская винтовка",
        ["Shotgun"] = "дробовик",
        ["Machinegun"] = "пулемёт",
        ["Pistol"] = "пистолет",
        ["Melee"] = "нож",
        ["Grenade"] = "граната",
        ["Helmet"] = "шлем",
        ["Vest"] = "бронежилет",
        ["Gloves"] = "перчатки",
        ["Boots"] = "ботинки",
        ["Face"] = "лицо",
        ["attachment"] = "модуль",
        ["skin"] = "камуфляж",
        ["decal"] = "наклейка",
        ["contract"] = "контракт",
        ["RandomBox"] = "коробка удачи",
        ["booster"] = "ускоритель",
        ["ResurrectionCoins"] = "жетоны воскрешения",
        ["ClanCreation"] = "создание клана",
        ["ammopack"] = "патроны",
        ["armorkit"] = "ремкомплект брони",
        ["medkit"] = "аптечка",
        ["defibrillator"] = "дефибриллятор",
        ["game_money"] = "варбаксы",
        ["crown_money"] = "короны",
        ["exp"] = "опыт",
        ["weapon"] = "оружие",
        ["armor"] = "снаряжение",
        ["ammo"] = "патроны",
    };

    public static readonly (string? Id, string Title)[] Groups =
    [
        (null, "Все"),
        ("Weapons", "Оружие"),
        ("Armor", "Снаряжение"),
        ("Accessories", "Модули"),
        ("ShopItems", "Особые"),
        ("Skins", "Камуфляжи"),
        ("Decals", "Наклейки"),
        ("Ammo", "Патроны"),
    ];

    public static string Category(string category) =>
        category == "" ? "" : Categories.GetValueOrDefault(category, category);

    public static string Group(string group) => Groups.FirstOrDefault(g => g.Id == group).Title ?? group;

    /// <summary>Русское название из игры (если есть) и категория; внутреннее имя — когда названия нет.</summary>
    public static string Title(string name, string category, string? title = null) =>
        !string.IsNullOrEmpty(title)
            ? (category == "" ? title : $"{title}  ·  {Category(category)}")
            : (category == "" ? name : $"{name}  ·  {Category(category)}");

    /// <summary>Остаток срока: «3 д 4 ч», «5 ч 10 мин».</summary>
    public static string Duration(long seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays} д {span.Hours} ч";
        if (span.TotalHours >= 1) return $"{span.Hours} ч {span.Minutes} мин";
        return $"{Math.Max(1, span.Minutes)} мин";
    }

    /// <summary>Срок аренды из магазина: «7d» → «7 дн.», «12h» → «12 ч», «1m» → «1 мес.».</summary>
    public static string Term(string term) => term.Length < 2 ? term : term[^1] switch
    {
        'd' => term[..^1] + " дн.",
        'h' => term[..^1] + " ч",
        'm' => term[..^1] + " мес.",
        _ => term,
    };
}
