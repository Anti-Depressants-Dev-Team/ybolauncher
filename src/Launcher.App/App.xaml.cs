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

        // Activate before hiding even for a minimized start: WinUI does not build the
        // window's content until it is activated, and the tray icon lives in that content.
        _window.Activate();

        if (ShouldStartMinimized(settings.Current))
        {
            Services.GetRequiredService<IWindowService>().Hide();
        }

        Services.GetRequiredService<IHotkeyService>()
            .Apply(settings.Current.Hotkey, settings.Current.HotkeyEnabled);

        // Tabs and discovery load after the window is up, so a first-run scan never
        // blocks the shell from appearing.
        var library = Services.GetRequiredService<LibraryViewModel>();
        await library.InitializeAsync();
    }

    /// <summary>
    /// True when this launch should go straight to the tray - either the setting says so,
    /// or Windows started us from the Run key with the minimized switch.
    /// </summary>
    private static bool ShouldStartMinimized(Launcher.Core.Models.AppSettings settings)
    {
        if (settings.StartMinimized)
        {
            return true;
        }

        return Environment.GetCommandLineArgs()
            .Skip(1)
            .Any(arg => string.Equals(arg, StartupService.MinimizedSwitch, StringComparison.OrdinalIgnoreCase));
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLauncherCore();

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IHotkeyService, HotkeyService>();

        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsViewModel>();

        // Singletons so tabs, selection and the catalog survive every navigation.
        // Both are constructed on the UI thread because LibraryViewModel captures the
        // DispatcherQueue.
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<LibraryViewModel>();

        return services.BuildServiceProvider();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Exception}");

        // Phase 8 replaces this with a crash log written next to the settings file.
        // Until then, let the process fail loudly rather than continue in a broken state.
    }
}
