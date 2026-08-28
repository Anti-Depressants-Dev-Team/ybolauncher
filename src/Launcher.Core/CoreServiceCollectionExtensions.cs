using Launcher.Core.Discovery;
using Launcher.Core.Icons;
using Launcher.Core.Interop;
using Launcher.Core.Services;
using Launcher.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Core;

/// <summary>
/// Registers the UI-independent half of the launcher.
/// <para>
/// Only services with a real implementation are registered. ILaunchService and
/// ISearchService land in Phases 3 and 5 (SPEC.md) - they are deliberately absent rather
/// than bound to do-nothing placeholders.
/// </para>
/// </summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // JsonStorageService and SettingsService take ILogger<T>; the default container has
        // no support for optional constructor parameters, so logging must be present.
        services.AddLogging();

        services.AddSingleton(_ => StoragePaths.CreateDefault());
        services.AddSingleton<IStorageService, JsonStorageService>();
        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddSingleton<IIconService, IconService>();
        services.AddSingleton<ShellLinkResolver>();
        services.AddSingleton<JunkFilter>();

        // Order matters only for progress reporting; deduplication is order-independent.
        services.AddSingleton<IAppSource, StartMenuAppSource>();
        services.AddSingleton<IAppSource, PackagedAppSource>();
        services.AddSingleton<IAppDiscoveryService, AppDiscoveryService>();

        return services;
    }
}
