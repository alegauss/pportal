using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Winwright.InApp;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP18: the mapping screen as markup.
///
/// What these do NOT assert is a press. There is no pad here and none is faked; the sequence a
/// press drives is asserted in <see cref="ControllerMappingScreenTests"/> without a device, and
/// what is left for the markup is the grid, the modal and the one string that changes with the
/// pad's name.
/// </summary>
public class ControllerMappingViewTests
{
    private static void Realise(FrameworkElement element)
    {
        element.Measure(new Size(1000, 900));
        element.Arrange(new Rect(0, 0, 1000, 900));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoads() => Apartment.Run(() => Assert.NotNull(new ControllerMappingView()));

    /// <summary>
    /// A row is one button wide or two, and the second slot is bound to its own visibility. A slot
    /// drawn over nothing would open a capture that writes into a binding that does not exist.
    /// </summary>
    [Fact]
    public void ARowIsOneButtonWideOrTwo()
    {
        var one = new MappingRowView(1, "Cross", "a", "");
        var two = new MappingRowView(2, "Circle", "b", "x");

        Assert.False(one.HasSecond);
        Assert.True(two.HasSecond);
    }

    /// <summary>The grid draws a row per binding, refilled in place rather than reassigned.</summary>
    [Fact]
    public void TheGridDrawsARowPerBinding() => Apartment.Run(() =>
    {
        var model = new ControllerMappingViewModel { ControllerType = "DualSense" };
        model.Rows.Add(new MappingRowView(1, "Cross", "a", ""));
        model.Rows.Add(new MappingRowView(2, "Circle", "b", "x"));

        var view = new ControllerMappingView { DataContext = model };
        Realise(view);

        Assert.Equal(2, ((ItemsControl)view.FindName("RowsList")).Items.Count);
        Assert.Equal("DualSense", ((TextBlock)view.FindName("Title")).Text);
    });

    /// <summary>
    /// The capture modal appears with the state and carries the pad's name in its prompt - the one
    /// string on this screen that changes with the device.
    /// </summary>
    [Fact]
    public void TheCaptureModalCarriesThePadsName() => Apartment.Run(() =>
    {
        var model = new ControllerMappingViewModel { ControllerType = "Xbox Series Controller" };
        var view = new ControllerMappingView { DataContext = model };
        Realise(view);

        var overlay = (FrameworkElement)view.FindName("CaptureOverlay");
        Assert.Equal(Visibility.Collapsed, overlay.Visibility);

        model.OpenCapture(buttonValue: 1, buttonIndex: 0, mappingIndex: 0);
        Realise(view);

        Assert.Equal(Visibility.Visible, overlay.Visibility);
        Assert.Contains(
            "Xbox Series Controller",
            ((TextBlock)view.FindName("CapturePrompt")).Text,
            StringComparison.Ordinal);
    });

    /// <summary>The Update button follows the alteration and nothing else.</summary>
    [Fact]
    public void TheUpdateButtonFollowsTheAlteration() => Apartment.Run(() =>
    {
        var model = new ControllerMappingViewModel { ControllerType = "DualSense" };
        var view = new ControllerMappingView { DataContext = model };
        Realise(view);

        var update = (Button)view.FindName("UpdateButton");
        Assert.False(update.IsEnabled);

        model.OpenCapture(1, 0, 0);
        model.ButtonSelected("a");
        Realise(view);

        Assert.True(update.IsEnabled);
    });

    /// <summary>And the analog opt-in reaches the model, off to begin with.</summary>
    [Fact]
    public void TheAnalogOptInReachesTheModel() => Apartment.Run(() =>
    {
        var model = new ControllerMappingViewModel { ControllerType = "DualSense" };
        var view = new ControllerMappingView { DataContext = model };
        Realise(view);

        var box = (CheckBox)view.FindName("AnalogBox");
        Assert.False(box.IsChecked);

        box.IsChecked = true;
        Realise(view);

        Assert.True(model.EnableAnalogStickMapping);
    });
}
