using System.Globalization;
using Launcher.Core.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using VirtualKey = Windows.System.VirtualKey;

namespace Launcher.App.Services;

/// <inheritdoc cref="IDialogService"/>
public sealed class DialogService : IDialogService, IDisposable
{
    /// <summary>
    /// WinUI allows only one ContentDialog at a time and throws on the second. Two quick
    /// context-menu clicks would otherwise crash the app.
    /// </summary>
    private readonly SemaphoreSlim _dialogLock = new(1, 1);

    private Window? _window;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    public async Task<string?> PromptForTextAsync(
        string title,
        string label,
        string initialValue,
        string acceptButtonText)
    {
        var input = new TextBox
        {
            Text = initialValue,
            SelectionStart = 0,
            SelectionLength = initialValue.Length,
        };

        var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(input);

        ContentDialog dialog = CreateDialog(title, panel, acceptButtonText);
        if (dialog is null)
        {
            return null;
        }

        ContentDialogResult result = await ShowAsync(dialog);
        return result == ContentDialogResult.Primary ? input.Text.Trim() : null;
    }

    public async Task<LaunchOptionsEdit?> EditLaunchOptionsAsync(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var arguments = new TextBox
        {
            Text = entry.Arguments ?? string.Empty,
            PlaceholderText = "No arguments",
        };

        var workingDirectory = new TextBox
        {
            Text = entry.WorkingDirectory ?? string.Empty,
            PlaceholderText = "Defaults to the target's own folder",
        };

        var panel = new StackPanel { Spacing = 8, MinWidth = 420 };
        panel.Children.Add(new TextBlock { Text = "Arguments" });
        panel.Children.Add(arguments);
        panel.Children.Add(new TextBlock { Text = "Working directory", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(workingDirectory);

        ContentDialog dialog = CreateDialog("Edit launch options", panel, "Save");
        if (dialog is null)
        {
            return null;
        }

        if (await ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        return new LaunchOptionsEdit(
            NullIfBlank(arguments.Text),
            NullIfBlank(workingDirectory.Text));
    }

    public async Task ShowPropertiesAsync(AppEntry entry, string? iconPath)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Stat the file off the UI thread - it may live on a slow or disconnected drive.
        string sizeText = await Task.Run(() => DescribeSize(entry.TargetPath));

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 6, MinWidth = 460 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 12),
        };

        if (iconPath is not null)
        {
            try
            {
                header.Children.Add(new Image
                {
                    Width = 48,
                    Height = 48,
                    Source = new BitmapImage(new Uri(iconPath)),
                });
            }
            catch (Exception)
            {
                // A missing cache file just means no icon in the header.
            }
        }

        header.Children.Add(new TextBlock
        {
            Text = entry.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        });

        AddRow(grid, "Name", entry.DisplayName);

        if (!string.Equals(entry.DisplayName, entry.OriginalName, StringComparison.Ordinal))
        {
            AddRow(grid, "Original name", entry.OriginalName);
        }

        AddRow(grid, "Kind", DescribeKind(entry));

        if (!string.IsNullOrWhiteSpace(entry.TargetPath))
        {
            AddRow(grid, "Target", entry.TargetPath);
        }

        if (!string.IsNullOrWhiteSpace(entry.LaunchUri))
        {
            AddRow(grid, "Link", entry.LaunchUri);
        }

        if (!string.IsNullOrWhiteSpace(entry.AppUserModelId))
        {
            AddRow(grid, "Application id", entry.AppUserModelId);
        }

        if (!string.IsNullOrWhiteSpace(entry.Arguments))
        {
            AddRow(grid, "Arguments", entry.Arguments);
        }

        if (!string.IsNullOrWhiteSpace(entry.WorkingDirectory))
        {
            AddRow(grid, "Working directory", entry.WorkingDirectory);
        }

        if (!string.IsNullOrWhiteSpace(entry.ShortcutPath))
        {
            AddRow(grid, "Shortcut", entry.ShortcutPath);
        }

        AddRow(grid, "Size", sizeText);
        AddRow(grid, "Launch count", entry.LaunchCount.ToString(CultureInfo.CurrentCulture));
        AddRow(grid, "Last launched", DescribeLastLaunched(entry.LastLaunchedUtc));

        if (entry.IsFiltered)
        {
            AddRow(grid, "Filtered as", entry.FilterReason.ToString());
        }

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(grid);

        var scroller = new ScrollViewer
        {
            Content = body,
            MaxHeight = 460,
            HorizontalScrollMode = ScrollMode.Disabled,
        };

        ContentDialog dialog = CreateDialog("Properties", scroller, acceptButtonText: null);
        if (dialog is null)
        {
            return;
        }

        await ShowAsync(dialog);
    }

    public async Task<string?> PickIconSourceAsync()
    {
        if (_window is null)
        {
            return null;
        }

        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                ViewMode = PickerViewMode.Thumbnail,
            };

            // Unpackaged WinUI 3 pickers have no implicit parent window, so the HWND has
            // to be supplied explicitly or PickSingleFileAsync throws.
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);

            foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".ico", ".exe", ".dll", ".lnk" })
            {
                picker.FileTypeFilter.Add(extension);
            }

            StorageFile? file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch (Exception)
        {
            // A picker that fails to open is not worth taking the app down for.
            return null;
        }
    }

    /// <summary>Preset accent colours, kept short so the picker stays a single row.</summary>
    private static readonly (string Name, string? Hex)[] AccentPresets =
    [
        ("Default", null),
        ("Red", "#E74856"),
        ("Orange", "#FF8C00"),
        ("Yellow", "#FFB900"),
        ("Green", "#10893E"),
        ("Teal", "#00B7C3"),
        ("Blue", "#0078D4"),
        ("Purple", "#8764B8"),
        ("Pink", "#E3008C"),
    ];

    public async Task<TabEdit?> EditTabAsync(string title, string name, string? glyph, string? accentColorHex)
    {
        var nameBox = new TextBox
        {
            Text = name,
            PlaceholderText = "Tab name",
            SelectionStart = 0,
            SelectionLength = name.Length,
        };

        string? chosenGlyph = TabGlyphs.IsFluentGlyph(glyph) ? glyph : null;
        var glyphButtons = new List<ToggleButton>();

        // A fixed picker rather than a free-text box: tab icons are monochrome Fluent
        // glyphs, and letting anything be typed is what allowed emoji in the first place.
        var glyphGrid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        const int GlyphColumns = 8;

        for (int column = 0; column < GlyphColumns; column++)
        {
            glyphGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        // "None" first, then every glyph in the set.
        var options = new List<TabGlyph?> { null };
        options.AddRange(TabGlyphs.All);

        for (int i = 0; i < options.Count; i++)
        {
            TabGlyph? option = options[i];
            int row = i / GlyphColumns;

            if (glyphGrid.RowDefinitions.Count <= row)
            {
                glyphGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var button = new ToggleButton
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(0),
                Tag = option?.Glyph,
                IsChecked = option?.Glyph == chosenGlyph,
                Content = option is null
                    ? new FontIcon { Glyph = "", FontSize = 16 }
                    : new FontIcon { Glyph = option.Glyph, FontSize = 16 },
            };

            AutomationProperties.SetName(button, option?.Name ?? "No icon");
            ToolTipService.SetToolTip(button, option?.Name ?? "No icon");

            button.Checked += (sender, _) =>
            {
                chosenGlyph = (string?)((ToggleButton)sender).Tag;

                // Radio behaviour: a set of ToggleButtons is the only way to show the
                // glyph itself as the choice, which RadioButtons cannot do.
                foreach (ToggleButton other in glyphButtons)
                {
                    if (!ReferenceEquals(other, sender))
                    {
                        other.IsChecked = false;
                    }
                }
            };

            // Unchecking the current selection means "no icon" rather than nothing.
            button.Unchecked += (sender, _) =>
            {
                if (chosenGlyph == (string?)((ToggleButton)sender).Tag)
                {
                    chosenGlyph = null;
                }
            };

            Grid.SetRow(button, row);
            Grid.SetColumn(button, i % GlyphColumns);
            glyphGrid.Children.Add(button);
            glyphButtons.Add(button);
        }

        var colorBox = new ComboBox { MinWidth = 180 };
        foreach ((string presetName, string? hex) in AccentPresets)
        {
            colorBox.Items.Add(new ComboBoxItem { Content = presetName, Tag = hex });
        }

        int selected = Array.FindIndex(
            AccentPresets,
            p => string.Equals(p.Hex, accentColorHex, StringComparison.OrdinalIgnoreCase));

        colorBox.SelectedIndex = selected >= 0 ? selected : 0;

        var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
        panel.Children.Add(new TextBlock { Text = "Name" });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "Icon", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(glyphGrid);
        panel.Children.Add(new TextBlock { Text = "Accent colour", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(colorBox);

        ContentDialog dialog = CreateDialog(title, panel, "Save");

        if (await ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        string chosenName = nameBox.Text.Trim();
        if (chosenName.Length == 0)
        {
            chosenName = name.Length > 0 ? name : "New tab";
        }

        string? chosenHex = (colorBox.SelectedItem as ComboBoxItem)?.Tag as string;

        return new TabEdit(chosenName, chosenGlyph, chosenHex);
    }

    public async Task<HotkeyBinding?> CaptureHotkeyAsync(HotkeyBinding current)
    {
        ArgumentNullException.ThrowIfNull(current);

        HotkeyBinding captured = current.Clone();

        var preview = new TextBlock
        {
            Text = captured.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        };

        var hint = new TextBlock
        {
            Text = "Hold Ctrl, Alt or Shift and press a key. Escape closes without changing anything.",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

        var warning = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
            Visibility = Visibility.Collapsed,
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 380, Padding = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(preview);
        panel.Children.Add(hint);
        panel.Children.Add(warning);

        ContentDialog dialog = CreateDialog("Set the global hotkey", panel, "Use this");

        // PreviewKeyDown, not KeyDown: the dialog and its buttons handle Enter, Space and
        // the arrow keys themselves, and all of those are legitimate hotkey characters.
        dialog.PreviewKeyDown += (_, args) =>
        {
            var key = (uint)args.Key;

            if (HotkeyBinding.IsModifierKey(key))
            {
                // The user is still holding modifiers and has not chosen a key yet.
                return;
            }

            if (args.Key == VirtualKey.Escape)
            {
                return;
            }

            args.Handled = true;

            captured = new HotkeyBinding
            {
                Alt = IsKeyDown(VirtualKey.Menu),
                Control = IsKeyDown(VirtualKey.Control),
                Shift = IsKeyDown(VirtualKey.Shift),
                Windows = IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows),
                Key = key,
            };

            preview.Text = captured.ToString();

            warning.Text = "A hotkey needs at least one of Ctrl, Alt or Shift.";
            warning.Visibility = captured.IsValid ? Visibility.Collapsed : Visibility.Visible;
        };

        if (await ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        return captured.IsValid ? captured : null;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    public async Task<IReadOnlyList<string>> ManageHiddenAppsAsync(IReadOnlyList<AppEntry> hidden)
    {
        ArgumentNullException.ThrowIfNull(hidden);

        if (hidden.Count == 0)
        {
            await ConfirmAsync("Hidden apps", "Nothing is hidden right now.", acceptButtonText: "OK");
            return [];
        }

        var boxes = new List<(CheckBox Box, string Id)>(hidden.Count);
        var list = new StackPanel { Spacing = 4 };

        foreach (AppEntry entry in hidden.OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var box = new CheckBox { Content = entry.DisplayName };
            boxes.Add((box, entry.Id));
            list.Children.Add(box);
        }

        var body = new StackPanel { Spacing = 8, MinWidth = 380 };
        body.Children.Add(new TextBlock
        {
            Text = "Tick the apps you want back on Home.",
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 320 });

        ContentDialog dialog = CreateDialog("Hidden apps", body, "Show selected");

        if (await ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return [];
        }

        return [.. boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Id)];
    }

    public async Task<string?> PickExportPathAsync(string suggestedName)
    {
        if (_window is null)
        {
            return null;
        }

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = suggestedName,
            };

            picker.FileTypeChoices.Add("Launcher configuration", [".zip"]);
            InitializePicker(picker);

            StorageFile? file = await picker.PickSaveFileAsync();
            return file?.Path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<string?> PickImportPathAsync()
    {
        if (_window is null)
        {
            return null;
        }

        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".zip");
            InitializePicker(picker);

            StorageFile? file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Unpackaged WinUI 3 pickers have no implicit parent window, so the HWND has to be
    /// supplied or the pick throws.
    /// </summary>
    private void InitializePicker(object picker)
    {
        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(_window!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string acceptButtonText)
    {
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 };

        ContentDialog dialog = CreateDialog(title, text, acceptButtonText);
        if (dialog is null)
        {
            return false;
        }

        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Builds a dialog rooted in the shell window. Returns null when no window is attached,
    /// which happens only if a dialog is requested before the shell exists.
    /// </summary>
    private ContentDialog CreateDialog(string title, object content, string? acceptButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = acceptButtonText is null ? "Close" : "Cancel",
            XamlRoot = _window?.Content?.XamlRoot,
        };

        if (acceptButtonText is not null)
        {
            dialog.PrimaryButtonText = acceptButtonText;
            dialog.DefaultButton = ContentDialogButton.Primary;
        }

        // Dialogs live outside the page's visual tree, so they do not inherit the theme
        // the user picked and would otherwise render in the system theme.
        if (_window?.Content is FrameworkElement root)
        {
            dialog.RequestedTheme = root.RequestedTheme;
        }

        return dialog;
    }

    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (dialog.XamlRoot is null)
        {
            return ContentDialogResult.None;
        }

        await _dialogLock.WaitAsync();
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception)
        {
            return ContentDialogResult.None;
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    private static void AddRow(Grid grid, string label, string value)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var caption = new TextBlock
        {
            Text = label,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

        Grid.SetRow(caption, row);
        Grid.SetColumn(caption, 0);
        grid.Children.Add(caption);

        var body = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };

        Grid.SetRow(body, row);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
    }

    private static string DescribeKind(AppEntry entry) => entry.LaunchKind switch
    {
        LaunchKind.PackagedApp => "Packaged app",
        LaunchKind.Uri => "Link",
        _ => "Desktop app",
    };

    private static string DescribeLastLaunched(DateTimeOffset? value) =>
        value is null
            ? "Never"
            : value.Value.ToLocalTime().ToString("f", CultureInfo.CurrentCulture);

    private static string DescribeSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Not applicable";
        }

        try
        {
            if (!File.Exists(path))
            {
                return Directory.Exists(path) ? "Folder" : "File not found";
            }

            long bytes = new FileInfo(path).Length;
            string[] units = ["bytes", "KB", "MB", "GB"];
            double size = bytes;
            int unit = 0;

            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return unit == 0
                ? string.Format(CultureInfo.CurrentCulture, "{0:N0} {1}", size, units[unit])
                : string.Format(CultureInfo.CurrentCulture, "{0:N1} {1}", size, units[unit]);
        }
        catch (Exception)
        {
            return "Unknown";
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose() => _dialogLock.Dispose();
}
