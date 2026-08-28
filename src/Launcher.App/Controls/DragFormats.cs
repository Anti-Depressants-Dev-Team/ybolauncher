namespace Launcher.App.Controls;

/// <summary>
/// Keys used on a <c>DataPackage</c> for drags that stay inside the app.
/// <para>
/// The payload rides in <c>DataPackage.Properties</c> rather than a registered clipboard
/// format: these drags never leave the process, and properties keep real objects without
/// a serialization round trip.
/// </para>
/// </summary>
internal static class DragFormats
{
    /// <summary>Entry ids being dragged, joined by <see cref="Separator"/>.</summary>
    public const string EntryIds = "ybolauncher/entry-ids";

    /// <summary>Id of the tab the drag started from, so the drop knows whether to move or copy.</summary>
    public const string SourceTabId = "ybolauncher/source-tab";

    /// <summary>Unit separator - cannot appear in an entry id, which is hex.</summary>
    public const char Separator = '';
}
