using System.Windows.Markup;

namespace SubootLauncher.Services;

/// <summary>
/// Язык интерфейса игрока (ru / en). Выбирается в настройках и применяется при перезапуске,
/// как оформление. В XAML: <c>Text="{l:T Login.Title}"</c>, в коде: <c>Loc.T("Login.Title")</c>.
/// Панель модерации пока только на русском.
/// </summary>
public static class Loc
{
    public static readonly (string Id, string Title)[] Languages = [("ru", "Русский"), ("en", "English")];

    public static string Language { get; private set; } = "ru";

    public static bool English => Language == "en";

    public static void Init(string? language) => Language = language == "en" ? "en" : "ru";

    public static string T(string key) =>
        (English ? En : Ru).TryGetValue(key, out var text) ? text : Ru.GetValueOrDefault(key, key);

    public static string T(string key, params object[] args) => string.Format(T(key), args);

    private static readonly Dictionary<string, string> Ru = new()
    {
        ["App.Subtitle"] = "Частный сервер · Suboot Games",
        ["Common.Refresh"] = "Обновить",
        ["Common.Choose"] = "Выбрать…",
        ["Common.Settings"] = "Настройки",
        ["Common.Back"] = "‹ Назад",

        ["Login.Login"] = "Вход",
        ["Login.Register"] = "Регистрация",
        ["Login.Invite"] = "Ключ приглашения",
        ["Login.InviteHint"] = "Сервер частный: ключ выдаёт администратор",
        ["Login.User"] = "Логин",
        ["Login.Password"] = "Пароль",
        ["Login.Repeat"] = "Повторите пароль",
        ["Login.Remember"] = "Запомнить меня на этом компьютере",
        ["Login.Submit"] = "Войти",
        ["Login.SubmitRegister"] = "Зарегистрироваться",
        ["Login.Closed"] = "Регистрация закрыта: аккаунт выдаёт администратор",
        ["Login.Empty"] = "Введите логин и пароль",
        ["Login.Mismatch"] = "Пароли не совпадают",

        ["Main.Staff"] = "Модерация",
        ["Main.ChangePassword"] = "Сменить пароль",
        ["Main.Logout"] = "Выйти",
        ["Main.Character"] = "Персонаж",
        ["Main.NoProfile"] = "Персонаж ещё не создан: нажмите «Играть» — игра предложит выбрать ник.",
        ["Main.Servers"] = "Серверы",
        ["Main.Play"] = "ИГРАТЬ",
        ["Main.Running"] = "ИГРА ЗАПУЩЕНА",
        ["Main.GameFolder"] = "Папка игры",
        ["Main.RoleAdmin"] = "администратор",
        ["Main.RoleModerator"] = "модератор",
        ["Main.Uid"] = "uid {0}",
        ["Main.Experience"] = "Звание {0} · опыт {1:N0}",
        ["Main.Clan"] = " · клан {0}",
        ["Main.Money"] = "Варбаксы {0:N0}    Кредиты {1:N0}    Короны {2:N0}",
        ["Main.NoFolder"] = "не выбрана — укажите папку с Bin32Release\\Game.exe",
        ["Main.FolderDialog"] = "Папка игры (в ней Bin32Release, Game, Engine)",
        ["Main.NotGameFolder"] = "В этой папке нет Bin32Release\\Game.exe",
        ["Main.ChooseFolderFirst"] = "Сначала выберите папку игры",
        ["Main.ServerOffline"] = "Сервер {0} сейчас недоступен",
        ["Main.Starting"] = "Игра запускается…",
        ["Main.AlreadyRunning"] = "Игра уже запущена. Закройте её, чтобы запустить снова.",
        ["Main.StartFailed"] = "Не удалось запустить игру: {0}",
        ["Main.LoginTitle"] = "Вход",
        ["Server.Online"] = "работает",
        ["Server.Degraded"] = "работает частично",
        ["Server.Offline"] = "недоступен",
        ["Server.Players"] = " · игроков: {0}",

        ["Password.Title"] = "Смена пароля",
        ["Password.Current"] = "Текущий пароль",
        ["Password.New"] = "Новый пароль",
        ["Password.Repeat"] = "Повторите новый пароль",
        ["Password.Hint"] = "Пароль сменится на всех устройствах, на остальных компьютерах нужно будет войти заново.",
        ["Password.Submit"] = "Сменить",
        ["Password.Mismatch"] = "Новые пароли не совпадают",
        ["Password.Done"] = "Пароль изменён. Перезапустите лаунчер, чтобы продолжить с новой сессией.",

        ["Settings.Title"] = "Настройки",
        ["Settings.Theme"] = "Оформление",
        ["Settings.Language"] = "Язык",
        ["Settings.Restart"] = "Оформление и язык применяются после перезапуска лаунчера.",
        ["Settings.Save"] = "Сохранить",
        ["Settings.Cancel"] = "Отмена",
        ["Settings.Confirm"] = "Лаунчер перезапустится, чтобы применить настройки. Продолжить?",
        ["Theme.classic"] = "Сайт 2013",
        ["Theme.game"] = "Как в игре",

        ["Ban.Title"] = "Аккаунт заблокирован",
        ["Ban.Reason"] = "Причина: {0}",
        ["Ban.Until"] = "До {0} · осталось {1}",
        ["Ban.Forever"] = "Блокировка бессрочная",
        ["Ban.Since"] = "Заблокирован {0}",
        ["Ban.Hint"] = "Пока блокировка действует, войти в игру нельзя. Если считаете её ошибкой — напишите администрации сервера.",
        ["Ban.Check"] = "Проверить снова",
        ["Ban.Still"] = "Блокировка всё ещё действует",
        ["Ban.Days"] = "{0} д {1} ч",
        ["Ban.Hours"] = "{0} ч {1} мин",
        ["Ban.Minutes"] = "{0} мин",
        ["Main.Accounts"] = "Аккаунты",
        ["Accounts.Title"] = "Учётные записи",
        ["Accounts.Hint"] = "Сохранённые на этом компьютере учётные записи. Пароли не хранятся — только зашифрованная сессия, которую нельзя перенести на другой компьютер.",
        ["Accounts.Current"] = "сейчас",
        ["Accounts.LastUsed"] = "последний вход {0}",
        ["Accounts.Switch"] = "Войти",
        ["Accounts.Remove"] = "Убрать",
        ["Accounts.Add"] = "Добавить учётную запись",
        ["Accounts.Manage"] = "Управление учётными записями…",
        ["Accounts.RemoveConfirm"] = "Убрать {0} из лаунчера? Сессия будет завершена — для входа понадобится пароль.",
        ["Accounts.Expired"] = "Сессия {0} истекла — войдите заново",
        ["Accounts.Empty"] = "Сохранённых учётных записей нет.",
        ["Api.Unreachable"] = "Сервер недоступен. Проверьте интернет и попробуйте ещё раз",
        ["Api.Error"] = "Ошибка сервера",
        ["Api.BadResponse"] = "Неожиданный ответ сервера, обновите лаунчер",
        ["App.StartFailed"] = "Лаунчер не смог запуститься:\n\n{0}\n\nПодробности записаны в файл:\n{1}\n\nОтправьте его администратору сервера.",
        ["App.Stuck"] = "Лаунчер уже запущен, но не отвечает (возможно, завис без окна).\n\nЗакрыть его и запустить заново?",
        ["App.Crash"] = "Непредвиденная ошибка. Лаунчер продолжит работу, но если ошибка повторяется — сообщите администратору.",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["App.Subtitle"] = "Private server · Suboot Games",
        ["Common.Refresh"] = "Refresh",
        ["Common.Choose"] = "Browse…",
        ["Common.Settings"] = "Settings",
        ["Common.Back"] = "‹ Back",

        ["Login.Login"] = "Log in",
        ["Login.Register"] = "Sign up",
        ["Login.Invite"] = "Invite key",
        ["Login.InviteHint"] = "This is a private server: the key is issued by the administrator",
        ["Login.User"] = "Login",
        ["Login.Password"] = "Password",
        ["Login.Repeat"] = "Repeat password",
        ["Login.Remember"] = "Remember me on this computer",
        ["Login.Submit"] = "Log in",
        ["Login.SubmitRegister"] = "Sign up",
        ["Login.Closed"] = "Registration is closed: accounts are issued by the administrator",
        ["Login.Empty"] = "Enter your login and password",
        ["Login.Mismatch"] = "Passwords do not match",

        ["Main.Staff"] = "Moderation",
        ["Main.ChangePassword"] = "Change password",
        ["Main.Logout"] = "Log out",
        ["Main.Character"] = "Character",
        ["Main.NoProfile"] = "No character yet: press Play and the game will ask you to choose a nickname.",
        ["Main.Servers"] = "Servers",
        ["Main.Play"] = "PLAY",
        ["Main.Running"] = "GAME RUNNING",
        ["Main.GameFolder"] = "Game folder",
        ["Main.RoleAdmin"] = "administrator",
        ["Main.RoleModerator"] = "moderator",
        ["Main.Uid"] = "uid {0}",
        ["Main.Experience"] = "Rank {0} · {1:N0} XP",
        ["Main.Clan"] = " · clan {0}",
        ["Main.Money"] = "Warbucks {0:N0}    Credits {1:N0}    Crowns {2:N0}",
        ["Main.NoFolder"] = "not set: choose the folder with Bin32Release\\Game.exe",
        ["Main.FolderDialog"] = "Game folder (contains Bin32Release, Game, Engine)",
        ["Main.NotGameFolder"] = "There is no Bin32Release\\Game.exe in this folder",
        ["Main.ChooseFolderFirst"] = "Choose the game folder first",
        ["Main.ServerOffline"] = "Server {0} is unavailable right now",
        ["Main.Starting"] = "Starting the game…",
        ["Main.AlreadyRunning"] = "The game is already running. Close it to start again.",
        ["Main.StartFailed"] = "Could not start the game: {0}",
        ["Main.LoginTitle"] = "Log in",
        ["Server.Online"] = "online",
        ["Server.Degraded"] = "partially online",
        ["Server.Offline"] = "offline",
        ["Server.Players"] = " · players: {0}",

        ["Password.Title"] = "Change password",
        ["Password.Current"] = "Current password",
        ["Password.New"] = "New password",
        ["Password.Repeat"] = "Repeat new password",
        ["Password.Hint"] = "The password changes everywhere: you will need to log in again on other computers.",
        ["Password.Submit"] = "Change",
        ["Password.Mismatch"] = "New passwords do not match",
        ["Password.Done"] = "Password changed. Restart the launcher to continue with the new session.",

        ["Settings.Title"] = "Settings",
        ["Settings.Theme"] = "Appearance",
        ["Settings.Language"] = "Language",
        ["Settings.Restart"] = "Appearance and language apply after the launcher restarts.",
        ["Settings.Save"] = "Save",
        ["Settings.Cancel"] = "Cancel",
        ["Settings.Confirm"] = "The launcher will restart to apply the settings. Continue?",
        ["Theme.classic"] = "Website 2013",
        ["Theme.game"] = "In-game",

        ["Ban.Title"] = "Account banned",
        ["Ban.Reason"] = "Reason: {0}",
        ["Ban.Until"] = "Until {0} · {1} left",
        ["Ban.Forever"] = "The ban is permanent",
        ["Ban.Since"] = "Banned on {0}",
        ["Ban.Hint"] = "You cannot enter the game while the ban is active. If you think it is a mistake, contact the server administration.",
        ["Ban.Check"] = "Check again",
        ["Ban.Still"] = "The ban is still active",
        ["Ban.Days"] = "{0} d {1} h",
        ["Ban.Hours"] = "{0} h {1} min",
        ["Ban.Minutes"] = "{0} min",
        ["Main.Accounts"] = "Accounts",
        ["Accounts.Title"] = "Accounts",
        ["Accounts.Hint"] = "Accounts saved on this computer. Passwords are not stored, only an encrypted session that cannot be moved to another computer.",
        ["Accounts.Current"] = "current",
        ["Accounts.LastUsed"] = "last used {0}",
        ["Accounts.Switch"] = "Switch",
        ["Accounts.Remove"] = "Remove",
        ["Accounts.Add"] = "Add account",
        ["Accounts.Manage"] = "Manage accounts…",
        ["Accounts.RemoveConfirm"] = "Remove {0} from the launcher? The session will end and the password will be needed to log in.",
        ["Accounts.Expired"] = "The session of {0} has expired, log in again",
        ["Accounts.Empty"] = "No saved accounts.",
        ["Api.Unreachable"] = "The server is unreachable. Check your connection and try again",
        ["Api.Error"] = "Server error",
        ["Api.BadResponse"] = "Unexpected server response, update the launcher",
        ["App.StartFailed"] = "The launcher could not start:\n\n{0}\n\nDetails are written to:\n{1}\n\nSend this file to the server administrator.",
        ["App.Stuck"] = "The launcher is already running but does not respond (it may be stuck without a window).\n\nClose it and start again?",
        ["App.Crash"] = "Unexpected error. The launcher keeps running; if it happens again, tell the administrator.",
    };
}

/// <summary>Строка интерфейса в XAML: <c>{l:T Main.Play}</c>.</summary>
public class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public TExtension() : this("") { }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
