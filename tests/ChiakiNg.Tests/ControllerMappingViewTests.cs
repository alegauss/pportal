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
    /// <summary>
    /// PP619: a row's slot says which slot it is, in the one field this application controls.
    ///
    /// PP227's harness picked the first row by taking every Button and keeping the ones whose
    /// AutomationId was EMPTY. That identifies a row by what it lacks, so it held only while every
    /// other button on the screen kept an id and would have broken silently the first time one did
    /// not — a new unnamed button becoming row zero, with nothing to say the check had moved.
    ///
    /// Read off the XAML rather than off a window, because this is a claim about the view's own
    /// declaration and the window it draws needs a pad plugged in.
    /// </summary>
    [Fact]
    public void EachRowSlotCarriesAnIdBuiltFromTheRowItBelongsTo()
    {
        string? view = SanitizerSource.LocateRelative(Path.Combine("app", "Views", "ControllerMappingView.xaml"));
        Assert.True(view is not null, "no ControllerMappingView.xaml above this assembly");

        string markup = File.ReadAllText(view!);

        // Value and never Name: the label moves with a translation and the position moves whenever
        // a row is added, and an id built on either addresses a different row afterwards.
        Assert.Contains("AutomationProperties.AutomationId=\"{Binding Value, StringFormat=slot.{0}.0}\"", markup, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"{Binding Value, StringFormat=slot.{0}.1}\"", markup, StringComparison.Ordinal);

        // And the context a screen reader was missing, added beside the binding rather than over it:
        // the content is the physical button bound here, and a Name set on top would replace the
        // value a person sees with the label of the row it sits in.
        Assert.Contains("first slot", markup, StringComparison.Ordinal);
        Assert.Contains("second slot", markup, StringComparison.Ordinal);
    }

}
