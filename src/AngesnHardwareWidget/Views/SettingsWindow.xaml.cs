using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AngesnHardwareWidget.Services;
using AngesnHardwareWidget.ViewModels;
using AngesnHardwareWidget.Models;

namespace AngesnHardwareWidget.Views;

/// <summary>
/// A plain Window rather than a MahApps MetroWindow: the application theme is Dark.Teal and this
/// dialog is deliberately light, matching the AI Usage Monitor's Settings window.
///
/// The code-behind exists only for drag-to-reorder, which is inherently a view concern: the
/// ViewModel is told "move this row to this index" and knows nothing about mouse positions.
/// </summary>
public partial class SettingsWindow : Window
{
    private const string RowDataFormat = "AngesnHardwareWidget.MetricStageRow";

    public SettingsWindow(SettingsService settings, HardwareSensorCatalog sensorCatalog)
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(settings, sensorCatalog, Close);
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    /// <summary>
    /// Starts a drag from the grip only. Anchoring it to the grip rather than the whole row is what
    /// keeps the checkbox clickable and the threshold boxes typeable.
    /// </summary>
    private void OnDragGripPressed(object sender, MouseButtonEventArgs eventArgs)
    {
        if (ViewModel is not { } viewModel
            || sender is not FrameworkElement { DataContext: MetricStageRowViewModel row } grip)
        {
            return;
        }

        eventArgs.Handled = true;

        // Remembered so an abandoned drag (Escape, or a drop outside the list) can put the row back
        // where it started, rather than leaving the live preview reorder half-applied.
        var originalIndex = viewModel.StageRows.IndexOf(row);

        try
        {
            var result = DragDrop.DoDragDrop(grip, new DataObject(RowDataFormat, row), DragDropEffects.Move);

            if (result != DragDropEffects.Move)
            {
                viewModel.MoveRowPreview(row, originalIndex);
            }
        }
        catch (Exception exception)
        {
            // A drag that the shell refuses must not take the dialog down with it.
            AppLog.Warn($"Row drag failed: {exception.GetType().Name}: {exception.Message}");
            viewModel.MoveRowPreview(row, originalIndex);
        }
    }

    /// <summary>
    /// Reorders the list as the cursor passes over each row, so the move is visible while dragging
    /// rather than only after the drop. Nothing is persisted here; see OnRowDrop.
    /// </summary>
    private void OnRowDragOver(object sender, DragEventArgs eventArgs)
    {
        eventArgs.Handled = true;

        if (!eventArgs.Data.GetDataPresent(RowDataFormat))
        {
            eventArgs.Effects = DragDropEffects.None;
            return;
        }

        eventArgs.Effects = DragDropEffects.Move;

        if (ViewModel is not { } viewModel
            || eventArgs.Data.GetData(RowDataFormat) is not MetricStageRowViewModel dragged
            || sender is not ListBoxItem { DataContext: MetricStageRowViewModel target } container
            || ReferenceEquals(dragged, target))
        {
            return;
        }

        var targetIndex = viewModel.StageRows.IndexOf(target);
        if (targetIndex < 0)
        {
            return;
        }

        // Only swap once the cursor is past the midpoint of the row it is over. Reacting on first
        // contact makes rows flip back and forth while the cursor sits near a boundary.
        var draggedIndex = viewModel.StageRows.IndexOf(dragged);
        var pastMidpoint = eventArgs.GetPosition(container).Y > container.ActualHeight / 2;

        if ((draggedIndex < targetIndex && !pastMidpoint) || (draggedIndex > targetIndex && pastMidpoint))
        {
            return;
        }

        viewModel.MoveRowPreview(dragged, targetIndex);
    }

    /// <summary>The order is already correct from the live preview; this just makes it stick.</summary>
    private void OnRowDrop(object sender, DragEventArgs eventArgs)
    {
        eventArgs.Handled = true;

        if (!eventArgs.Data.GetDataPresent(RowDataFormat) || ViewModel is not { } viewModel)
        {
            return;
        }

        eventArgs.Effects = DragDropEffects.Move;
        viewModel.CommitRowOrder();
    }
}
