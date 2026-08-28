namespace Launcher.App.ViewModels;

/// <summary>
/// The operations a tile's context menu invokes. Implemented by <see cref="HomeViewModel"/>.
/// <para>
/// The commands live on the tile so <c>x:Bind</c> inside the item template can reach them
/// directly, but the work belongs to the page: it owns the dialogs, the InfoBar and
/// persistence. This interface is the seam between the two.
/// </para>
/// </summary>
public interface IAppTileHost
{
    Task LaunchAsync(AppTileViewModel tile, bool asAdministrator);

    Task OpenFileLocationAsync(AppTileViewModel tile);

    Task RenameAsync(AppTileViewModel tile);

    Task ChangeIconAsync(AppTileViewModel tile);

    Task ResetIconAsync(AppTileViewModel tile);

    Task EditLaunchOptionsAsync(AppTileViewModel tile);

    Task ToggleFavoriteAsync(AppTileViewModel tile);

    Task ToggleHiddenAsync(AppTileViewModel tile);

    Task ShowPropertiesAsync(AppTileViewModel tile);
}
