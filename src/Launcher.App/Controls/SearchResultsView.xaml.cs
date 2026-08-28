using System.Collections.Specialized;
using System.ComponentModel;
using Launcher.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Launcher.App.Controls;

/// <summary>The search results list, shown in place of the tab content while a query is active.</summary>
public sealed partial class SearchResultsView : UserControl
{
    public SearchResultsView()
    {
        Library = App.Services.GetRequiredService<LibraryViewModel>();
        InitializeComponent();

        Library.SearchResults.CollectionChanged += OnResultsChanged;
        Library.PropertyChanged += OnLibraryPropertyChanged;
    }

    public LibraryViewModel Library { get; }

    /// <summary>
    /// Shown only when a query produced nothing - not while the box is empty, which is
    /// when this whole view is hidden anyway. Set in code rather than bound: the condition
    /// spans two sources and a plain property binding could not be notified of either.
    /// </summary>
    private void UpdateEmptyState() =>
        EmptyState.Visibility = Library.IsSearchActive && Library.SearchResults.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>
    /// Guards the two-way sync below. Selection is pushed by hand rather than with a
    /// TwoWay binding on SelectedIndex, because repopulating the list momentarily resets
    /// SelectedIndex to -1 and the binding writes that straight back over the view model.
    /// </summary>
    private bool _syncingSelection;

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
        ApplySelection();
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SelectedResultIndex))
        {
            ApplySelection();
        }
    }

    /// <summary>Pushes the view model's selection into the list and keeps it in view.</summary>
    private void ApplySelection()
    {
        int index = Library.SelectedResultIndex;

        _syncingSelection = true;
        try
        {
            Results.SelectedIndex = index >= 0 && index < Library.SearchResults.Count ? index : -1;
        }
        finally
        {
            _syncingSelection = false;
        }

        // The keyboard drives selection from the search box, which never gives the list
        // focus, so nothing else would scroll the highlighted row into view.
        if (Results.SelectedItem is not null)
        {
            Results.ScrollIntoView(Results.SelectedItem);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingSelection && Results.SelectedIndex >= 0)
        {
            Library.SelectedResultIndex = Results.SelectedIndex;
        }
    }

    private async void OnResultClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchResultViewModel result)
        {
            await Library.LaunchResultAsync(result);
        }
    }
}
