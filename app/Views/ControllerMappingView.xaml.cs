using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Session;

namespace ChiakiNg.Views;

/// <summary>
/// PP18/PP223: the controller mapping screen, and the clicks that reach the pad.
///
/// PP13's rule made this a constructor and PP217 built the session it now talks to. What is here
/// is the part that could not be written before either existed: which row and which of its two
/// slots a click was on.
///
/// The pattern is <see cref="DialogHostView.HandleKey"/>'s, for the reason PP37 filed. Resolving a
/// clicked Button to a row needs a visual tree; deciding what that row means needs none - so the
/// decision is <see cref="ClickSlot"/>, a plain method a test can call with a data context and no
/// window, and the handler above it is the one line of plumbing that stays untested.
///
/// The events go OUT rather than the session coming in. This view knows nothing about SDL, about a
/// capture or about a document; it says which slot was clicked and lets whoever assembled it
/// decide, which is what keeps the screen assertable without any of them.
/// </summary>
public partial class ControllerMappingView : UserControl
{
    public ControllerMappingView()
    {
        InitializeComponent();

        // On the list rather than on the control: Update and the capture's Close are named
        // buttons with their own meanings, and a handler at the top would catch those too.
        RowsList.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnRowClick));

        UpdateButton.Click += (_, _) => ApplyRequested?.Invoke();
        CaptureCloseButton.Click += (_, _) => CloseCaptureRequested?.Invoke();
    }

    /// <summary>
    /// A row's slot was clicked: the chiaki button's value, which of the two slots, and the row's
    /// position - the three things <see cref="ControllerMappingSession.OpenCapture"/> asks for.
    /// </summary>
    public event Action<int, int, int>? CaptureRequested;

    /// <summary>The capture dialog was dismissed without a press.</summary>
    public event Action? CloseCaptureRequested;

    /// <summary>Update was pressed.</summary>
    public event Action? ApplyRequested;

    /// <summary>
    /// The click, as a plain method.
    /// </summary>
    /// <param name="rowIndex">Which row, by position - which is what the QML's Repeater index is.</param>
    /// <param name="slot">0 or 1. A row is one button wide or two, and this says which.</param>
    /// <returns>False where there is nothing at that position, which a test can assert.</returns>
    public bool ClickSlot(int rowIndex, int slot)
    {
        if (DataContext is not ControllerMappingViewModel model)
            return false;

        if (rowIndex < 0 || rowIndex >= model.Rows.Count)
            return false;

        // A slot the row does not draw captures into a binding that does not exist, which is the
        // rule PP173 put on the second button's visibility. Refused here as well, so the screen
        // and the keyboard cannot disagree about it.
        if (slot == 1 && !model.Rows[rowIndex].HasSecond)
            return false;

        CaptureRequested?.Invoke(model.Rows[rowIndex].Value, slot, rowIndex);
        return true;
    }

    private void OnRowClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button button)
            return;

        if (button.DataContext is not MappingRowView row)
            return;

        if (DataContext is not ControllerMappingViewModel model)
            return;

        int slot = int.TryParse(
            button.Tag as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tag)
            ? tag
            : 0;

        ClickSlot(model.Rows.IndexOf(row), slot);
        e.Handled = true;
    }
}
