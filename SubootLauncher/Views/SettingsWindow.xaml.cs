using System.Windows;
using SubootLauncher.Services;

namespace SubootLauncher.Views;

/// <summary>Язык и оформление. Применяются перезапуском лаунчера (сессия сохранена — вход не нужен).</summary>
public partial class SettingsWindow : Window
{
    private readonly string[] _themes = App.Themes.Keys.ToArray();

    public SettingsWindow()
    {
        InitializeComponent();
        LanguageBox.ItemsSource = Loc.Languages.Select(l => l.Title).ToList();
        LanguageBox.SelectedIndex = Math.Max(0, Array.FindIndex(Loc.Languages, l => l.Id == Loc.Language));
        ThemeBox.ItemsSource = _themes.Select(t => Loc.T("Theme." + t)).ToList();
        ThemeBox.SelectedIndex = Math.Max(0, Array.IndexOf(_themes, App.Settings.Theme ?? "classic"));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var language = Loc.Languages[Math.Max(0, LanguageBox.SelectedIndex)].Id;
        var theme = _themes[Math.Max(0, ThemeBox.SelectedIndex)];
        if (language == Loc.Language && theme == (App.Settings.Theme ?? "classic"))
        {
            Close();
            return;
        }
        if (MessageBox.Show(this, Loc.T("Settings.Confirm"), Loc.T("Settings.Title"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        App.Settings.Language = language;
        App.Settings.Theme = theme;
        App.Settings.Save();
        App.Restart();
    }
}
