using System.Windows;

namespace SubootLauncher.Views;

/// <summary>Подтверждение опасного действия вводом фразы (ника игрока или «СБРОСИТЬ ВСЕХ»).</summary>
public partial class TypeConfirmWindow : Window
{
    private readonly string _phrase;

    private TypeConfirmWindow(string title, string message, string phrase, string button)
    {
        InitializeComponent();
        _phrase = phrase;
        TitleText.Text = title;
        MessageText.Text = message;
        PromptText.Text = $"Чтобы подтвердить, введите: {phrase}";
        OkButton.Content = button;
        Loaded += (_, _) => InputBox.Focus();
    }

    /// <summary>true — пользователь ввёл фразу точно и нажал кнопку.</summary>
    public static bool Ask(Window owner, string title, string message, string phrase, string button) =>
        new TypeConfirmWindow(title, message, phrase, button) { Owner = owner }.ShowDialog() == true;

    // Вставка из буфера тоже подходит, но совпадение — посимвольное (с учётом регистра)
    private void OnInput(object sender, System.Windows.Controls.TextChangedEventArgs e) => OkButton.IsEnabled = InputBox.Text.Trim() == _phrase;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (InputBox.Text.Trim() == _phrase) DialogResult = true;
    }
}
