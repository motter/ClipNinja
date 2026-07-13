using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClipNinjaV2.Views;

/// <summary>
/// Tiny reusable input dialog. Returns null if canceled, the string if OK.
/// </summary>
public static class InputPrompt
{
    public static string? Show(Window owner, string prompt, string title, string initialValue = "", int maxLength = 0)
    {
        var dlg = new Window
        {
            Owner = owner,
            Title = title,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var promptText = new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(promptText, 0);
        grid.Children.Add(promptText);

        var textBox = new TextBox
        {
            Text = initialValue,
            Margin = new Thickness(0, 0, 0, 8),
        };
        if (maxLength > 0) textBox.MaxLength = maxLength;
        Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var ok = new Button { Content = "OK", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);

        dlg.Content = grid;

        string? result = null;
        ok.Click += (_, _) => { result = textBox.Text; dlg.DialogResult = true; };
        cancel.Click += (_, _) => { dlg.DialogResult = false; };

        dlg.Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };

        var ok2 = dlg.ShowDialog();
        return ok2 == true ? result : null;
    }
}
