using Avalonia.Interactivity;

namespace Loot.Views;

/// <summary>A user-dismissible notification surface that remains above other application windows.</summary>
internal sealed partial class TopmostNotificationWindow : Avalonia.Controls.Window
{
    public TopmostNotificationWindow(string title, string body, string? subtitle)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        BodyMarkdown.Markdown = body;
        SubtitleText.Text = subtitle;
        SubtitleText.IsVisible = !string.IsNullOrWhiteSpace(subtitle);
        CloseButton.Content = Loot.Localization.Resources.ResourceManager.GetString("Close", Loot.Localization.Resources.Culture) ?? "Close";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
