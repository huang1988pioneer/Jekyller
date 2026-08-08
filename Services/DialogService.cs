using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Jekyller.Services;

public interface IDialogService
{
    Task<string?> PickFolderAsync(string title);
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowMessageAsync(string title, string message);
}

public sealed class DialogService : IDialogService
{
    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var window = GetMainWindow();
        if (window is null)
            return null;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        }).ConfigureAwait(true);

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = GetMainWindow();
        if (owner is null)
            return false;

        var tcs = new TaskCompletionSource<bool>();
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var yes = new Button { Content = "確定", MinWidth = 90 };
        var no = new Button { Content = "取消", MinWidth = 90 };

        yes.Click += (_, _) =>
        {
            tcs.TrySetResult(true);
            dialog.Close();
        };
        no.Click += (_, _) =>
        {
            tcs.TrySetResult(false);
            dialog.Close();
        };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 16, 0, 0),
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Children = { yes, no }
                },
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        await dialog.ShowDialog(owner).ConfigureAwait(true);
        return await tcs.Task.ConfigureAwait(true);
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var owner = GetMainWindow();
        if (owner is null)
            return;

        var dialog = new Window
        {
            Title = title,
            Width = 480,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };

        var ok = new Button
        {
            Content = "關閉",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        ok.Click += (_, _) => dialog.Close();

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Children = { ok }
                },
                new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };

        await dialog.ShowDialog(owner).ConfigureAwait(true);
    }
}
