using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Winwright.InApp;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Video tab as markup. The interest is the decoder combo, whose contents are not known
/// until a view model arrives - so the fill happens later than the General tab's and the stored
/// index has to survive it anyway.
/// </summary>
public class VideoSettingsViewTests
{
    private static void Realise(FrameworkElement element)
    {
        element.Measure(new Size(900, 800));
        element.Arrange(new Rect(0, 0, 900, 800));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoadsWithTheFixedListFilled() => Apartment.Run(() =>
    {
        var view = new VideoSettingsView();
        Assert.Equal(6, ((ComboBox)view.FindName("WindowTypeCombo")).Items.Count);
    });

    /// <summary>
    /// The decoder list arrives with the view model, and the chosen index survives the fill. This
    /// is the General tab's hazard one step worse: there the list was a constant, here it cannot be
    /// assigned until the runtime list is known.
    /// </summary>
    [Fact]
    public void TheDecoderChoiceSurvivesALateFill() => Apartment.Run(() =>
    {
        var model = new VideoSettingsViewModel(
            new FakePreferences().Set("settings/hw_decoder", "d3d11va"),
            ["vulkan", "d3d11va"]);

        var view = new VideoSettingsView { DataContext = model };
        Realise(view);

        var combo = (ComboBox)view.FindName("DecoderCombo");

        Assert.Equal(4, combo.Items.Count);           // none, vulkan, d3d11va, auto
        Assert.Equal(2, combo.SelectedIndex);
        Assert.Equal("d3d11va", combo.SelectedItem);
        Assert.Equal("d3d11va", model.DecoderStored);
    });

    /// <summary>
    /// And selecting the first entry on screen stores the empty string, which is the finding taken
    /// the long way round - through the combo, the binding and the view model.
    /// </summary>
    [Fact]
    public void SelectingNoneOnScreenStoresTheEmptyString() => Apartment.Run(() =>
    {
        var model = new VideoSettingsViewModel(new FakePreferences(), ["vulkan"]);
        var view = new VideoSettingsView { DataContext = model };
        Realise(view);

        var combo = (ComboBox)view.FindName("DecoderCombo");
        Assert.Equal("auto", model.DecoderStored);

        combo.SelectedIndex = 0;
        Realise(view);

        Assert.Equal("none", combo.SelectedItem);
        Assert.Equal("", model.DecoderStored);
        Assert.True(DecoderChoice.MeansNoHardwareDecoder(model.DecoderStored));
    });

    /// <summary>
    /// The window type reaches the store as settings.cpp's word and not as the label on screen -
    /// three of the six differ, so this is checked on one that does.
    /// </summary>
    [Fact]
    public void TheWindowTypeReachesTheStoreAsItsOwnWord() => Apartment.Run(() =>
    {
        var model = new VideoSettingsViewModel(new FakePreferences(), ["vulkan"]);
        var view = new VideoSettingsView { DataContext = model };
        Realise(view);

        var combo = (ComboBox)view.FindName("WindowTypeCombo");
        Assert.Equal(3, combo.SelectedIndex);

        combo.SelectedIndex = 2;
        Realise(view);

        Assert.Equal("Adjust Resolution Manually", combo.SelectedItem);
        Assert.Equal("Adjust Manually", model.WindowStored);
    });

    /// <summary>Choosing Custom Resolution reveals the two fields, and nothing else does.</summary>
    [Fact]
    public void TheResolutionFieldsFollowTheWindowType() => Apartment.Run(() =>
    {
        var model = new VideoSettingsViewModel();
        var view = new VideoSettingsView { DataContext = model };
        Realise(view);

        var group = (FrameworkElement)view.FindName("CustomResolutionGroup");
        var combo = (ComboBox)view.FindName("WindowTypeCombo");

        Assert.Equal(Visibility.Collapsed, group.Visibility);

        combo.SelectedIndex = WindowTypeChoice.CustomResolution;
        Realise(view);
        Assert.Equal(Visibility.Visible, group.Visibility);

        combo.SelectedIndex = 5;
        Realise(view);
        Assert.Equal(Visibility.Collapsed, group.Visibility);
    });

    /// <summary>
    /// The restart warning is shown for the renderer that actually restarts and no other. It is the
    /// only control on the screen that can end the process, so the screen says so rather than
    /// leaving the user to find out.
    /// </summary>
    [Fact]
    public void OnlyTheRestartingBackendWarnsAboutIt() => Apartment.Run(() =>
    {
        var model = new VideoSettingsViewModel { RenderBackend = "vulkan" };
        var view = new VideoSettingsView { DataContext = model };
        Realise(view);

        var warning = (TextBlock)view.FindName("RestartWarning");
        Assert.Equal(Visibility.Collapsed, warning.Visibility);

        model.RenderBackend = "opengl";
        Realise(view);
        Assert.Equal(Visibility.Visible, warning.Visibility);
    });

    /// <summary>
    /// The resolution fields commit on LostFocus, not on every keystroke - PP140's rule, and the
    /// reason typing "1920" does not store 1, then 19, then 192 on the way.
    /// </summary>
    [Fact]
    public void TheResolutionFieldsDoNotWritePerKeystroke() => Apartment.Run(() =>
    {
        var model = new VideoSettingsViewModel { WindowIndex = WindowTypeChoice.CustomResolution };
        var view = new VideoSettingsView { DataContext = model };
        Realise(view);

        var field = (TextBox)view.FindName("WidthField");
        field.Text = "1920";
        Realise(view);

        // The binding has not pushed, so the field's committed value is untouched.
        Assert.Equal(0, model.Width.Value);

        model.Width.Type("1920");
        model.Width.Commit();
        Assert.Equal(1920, model.Width.Value);
    });
}
