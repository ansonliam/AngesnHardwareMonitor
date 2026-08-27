using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HardwareWidget.Services;
using HardwareWidget.ViewModels;

namespace HardwareWidget.Views;

/// <summary>
/// A plain Window rather than a MahApps MetroWindow: the application theme is Dark.Teal and this
/// dialog is deliberately light, matching the AI Usage Monitor's Settings window.
///
/// The code-behind exists only for drag-to-reorder, which is inherently a view concern: the
/// ViewModel is told "move this row to this index" and knows nothing about mouse positions.
/// </summary>
public partial class SettingsWindow : Window
{
    private const string RowDataFormat = "HardwareWidget.MetricStageRow";

    public SettingsWindow(SettingsService settings)
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(settings, Close);
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    /// <summary>
    /// Starts a drag from the grip only. Anchoring it to the grip rather than the whole row is what
    /// keeps the checkbox clickable and the threshold boxes typeable.
    /// </summary>
    private void OnDragGripPressed(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is not FrameworkElement { DataContext: MetricStageRowViewModel row } grip)
        {
            return;
        }

        eventArgs.Handled = true;

        try
        {
            DragDrop.DoDragDrop(grip, new DataObject(RowDataFormat, row), DragDropEffects.Move);
        }
        catch (Exception exception)
        {
            // A drag that the shell refuses must not take the dialog down with it.
            AppLog.Warn($"Row drag failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void OnRowDragOver(object sender, DragEventArgs eventArgs)
    {
        eventArgs.Effects = eventArgs.Data.GetDataPresent(RowDataFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;

        eventArgs.Handled = true;
    }

    private void OnRowDrop(object sender, DragEventArgs eventArgs)
    {
        eventArgs.Handled = true;

        if (ViewModel is not { } viewModel
            || eventArgs.Data.GetData(RowDataFormat) is not MetricStageRowViewModel dragged
            || sender is not ListBoxItem { DataContext: MetricStageRowViewModel target })
        {
            return;
        }

        var targetIndex = viewModel.StageRows.IndexOf(target);
        if (targetIndex < 0)
        {
            return;
        }

        // Dropping onto a row takes that row's position, which reads the same whether the drag went
        // up or down and can still reach either end of the list.
        viewModel.MoveRow(dragged, targetIndex);
    }
}
