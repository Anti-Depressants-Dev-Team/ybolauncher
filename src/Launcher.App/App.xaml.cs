using Launcher.App.Services;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Launcher.Core.Services;
using Launcher.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Launcher.App;

/// <summary>
/// Composition root. Builds the DI container, restores settings, then shows the shell.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();

        // A XAML-layer exception must be recorded, not swallowed into a silent exit.
        UnhandledException += OnUnhandledException;
    }

    /// <summary>
    /// The application service provider. Set before the shell window is constructed.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ConfigureServices();

        // Create %LocalAppData%\YBO Launcher (or the portable folder) up front. If this
        // fails the app still runs, it just cannot persist anything.
        Services.GetRequiredService<StoragePaths>().EnsureCreated();

        var settings = Services.GetRequiredService<ISettingsService>();
        await settings.LoadAsync();

        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLauncherCore();

        services.AddSingleton<IThemeService, ThemeService>();

        services.AddSingleton<MainWindow>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Exception}");

        // Phase 8 replaces this with a crash log written next to the settings file.
        // Until then, let the process fail loudly rather than continue in a broken state.
    }
}
