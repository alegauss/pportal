using System.Windows;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP223: the click that reaches the pad, and the window that shows the screen.
///
/// The decision is a plain method for the reason <see cref="DialogHostView.HandleKey"/> gives:
/// resolving a clicked Button to a row needs a visual tree, and deciding what that row MEANS needs
/// none. What stays untested is the one line that turns a RoutedEventArgs into a row index.
/// </summary>
public class MappingScreenClickTests
{
    private static void OnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private const int Cross = 1 << 0;
    private const int Moon = 1 << 1;

    private static ControllerMappingViewModel Model()
    {
        var model = new ControllerMappingViewModel { ControllerType = "DualSense" };
        model.Rows.Add(new MappingRowView(Cross, "Cross", "b0", ""));
        model.Rows.Add(new MappingRowView(Moon, "Moon", "b1", "b4"));
        return model;
    }

    /// <summary>A click carries the three things the session asks for, and nothing else.</summary>
    [Fact]
    public void AClickCarriesTheButtonTheSlotAndTheRow() => OnSta(() =>
    {
        var view = new ControllerMappingView { DataContext = Model() };

        (int Value, int Slot, int Index)? asked = null;
        view.CaptureRequested += (value, slot, index) => asked = (value, slot, index);

        Assert.True(view.ClickSlot(rowIndex: 1, slot: 0));
        Assert.Equal((Moon, 0, 1), asked);
    });

    /// <summary>The second slot is its own capture, on the same row.</summary>
    [Fact]
    public void TheSecondSlotIsItsOwnCapture() => OnSta(() =>
    {
        var view = new ControllerMappingView { DataContext = Model() };

        (int Value, int Slot, int Index)? asked = null;
        view.CaptureRequested += (value, slot, index) => asked = (value, slot, index);

        Assert.True(view.ClickSlot(rowIndex: 1, slot: 1));
        Assert.Equal((Moon, 1, 1), asked);
    });

    /// <summary>
    /// A second slot the row does not draw is refused. PP173 made that button's visibility follow
    /// the binding; a capture opened on it would write into a binding that does not exist.
    /// </summary>
    [Fact]
    public void ASlotTheRowDoesNotDrawIsRefused() => OnSta(() =>
    {
        var view = new ControllerMappingView { DataContext = Model() };

        bool asked = false;
        view.CaptureRequested += (_, _, _) => asked = true;

        Assert.False(view.ClickSlot(rowIndex: 0, slot: 1));
        Assert.False(asked);
    });

    /// <summary>And a row that is not there asks for nothing rather than throwing.</summary>
    [Fact]
    public void ARowThatIsNotThereAsksForNothing() => OnSta(() =>
    {
        var view = new ControllerMappingView { DataContext = Model() };

        Assert.False(view.ClickSlot(rowIndex: -1, slot: 0));
        Assert.False(view.ClickSlot(rowIndex: 9, slot: 0));
    });

    /// <summary>With no model behind it the screen asks for nothing at all.</summary>
    [Fact]
    public void WithNoModelItAsksForNothing()
        => OnSta(() => Assert.False(new ControllerMappingView().ClickSlot(0, 0)));

    /// <summary>Update and the capture's Close are their own signals, not a row's.</summary>
    [Fact]
    public void UpdateAndCloseAreTheirOwnSignals() => OnSta(() =>
    {
        var view = new ControllerMappingView { DataContext = Model() };

        bool applied = false;
        bool closed = false;
        view.ApplyRequested += () => applied = true;
        view.CloseCaptureRequested += () => closed = true;

        // Raised through the buttons themselves, which is what the constructor wired.
        ((System.Windows.Controls.Button)view.FindName("UpdateButton")).RaiseEvent(
            new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        ((System.Windows.Controls.Button)view.FindName("CaptureCloseButton")).RaiseEvent(
            new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));

        Assert.True(applied);
        Assert.True(closed);
    });

    /// <summary>
    /// The window opens empty, which is how PP1 filed it, and shows a screen when given one.
    /// </summary>
    [Fact]
    public void TheWindowOpensEmptyAndTakesAScreen() => OnSta(() =>
    {
        var window = new MainWindow();

        Assert.False(window.HasScreen);

        window.ShowScreen(new ControllerMappingView());

        Assert.True(window.HasScreen);
    });
}
