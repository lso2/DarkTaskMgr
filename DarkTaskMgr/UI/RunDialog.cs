using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DarkTaskMgr.UI
{
    /// <summary>
    /// The small "Run new task" prompt. It is built in code rather than XAML because it is a single
    /// field and two buttons, so a whole XAML page would be more ceremony than the dialog is worth.
    /// It inherits the application's dark resources, so it matches the rest of the window.
    /// </summary>
    internal static class RunDialog
    {
        public static string Show(Window owner)
        {
            var win = new Window
            {
                Title = "Create new task",
                Width = 460,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize
            };

            var root = new Border
            {
                Background = (Brush)Application.Current.Resources["PanelBrush"],
                BorderBrush = (Brush)Application.Current.Resources["StrokeBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18)
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "Type the name of a program, folder, or document and DarkTaskMgr will open it.",
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 12.5
            });

            var box = new TextBox
            {
                FontSize = 13,
                Padding = new Thickness(8, 6, 8, 6),
                Background = (Brush)Application.Current.Resources["PanelAltBrush"],
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["StrokeBrush"],
                CaretBrush = (Brush)Application.Current.Resources["TextPrimaryBrush"]
            };
            panel.Children.Add(box);

            string result = null;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var ok = new Button { Content = "Open", Style = (Style)Application.Current.Resources["AccentButton"], MinWidth = 84, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.Resources["SubtleButton"], MinWidth = 84, IsCancel = true };
            ok.Click += (s, e) => { result = box.Text; win.DialogResult = true; };
            cancel.Click += (s, e) => { win.DialogResult = false; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            root.Child = panel;
            win.Content = root;

            win.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { win.DragMove(); } catch { } } };
            win.Loaded += (s, e) => { box.Focus(); };

            bool okPressed = win.ShowDialog() == true;
            return okPressed ? result : null;
        }
    }
}
