using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the General tab as markup - that it loads, that the combos are filled before the
/// bindings resolve, and that a choice made on screen reaches the store's spelling.
/// </summary>
public class GeneralSettingsViewTests
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

    private static void Realise(FrameworkElement element)
    {
        element.Measure(new Size(900, 700));
        element.Arrange(new Rect(0, 0, 900, 700));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoadsWithItsCombosFilled() => OnSta(() =>
    {
        var view = new GeneralSettingsView();

        Assert.Equal(3, ((ComboBox)view.FindName("DisconnectCombo")).Items.Count);
        Assert.Equal(2, ((ComboBox)view.FindName("SuspendCombo")).Items.Count);
        Assert.Equal(4, ((ComboBox)view.FindName("AudioVideoCombo")).Items.Count);
        Assert.Equal(17, ((ComboBox)view.FindName("Shortcut1Combo")).Items.Count);
    });

    /// <summary>
    /// The stored choice survives the fill. ItemsSource resets SelectedIndex to -1, so a list
    /// assigned after the bindings resolved would show the first row for every tab on the screen -
    /// and the tab would look like a fresh install rather than the user's settings.
    /// </summary>
    [Fact]
    public void TheStoredChoiceSurvivesTheCombosBeingFilled() => OnSta(() =>
    {
        var model = new GeneralSettingsViewModel(new FakePreferences()
            .Set("settings/disconnect_action", "sleep")
            .Set("settings/audio_video_disabled", 3));

        var view = new GeneralSettingsView { DataContext = model };
        Realise(view);

        Assert.Equal(1, ((ComboBox)view.FindName("DisconnectCombo")).SelectedIndex);
        Assert.Equal("Enter Sleep Mode", ((ComboBox)view.FindName("DisconnectCombo")).SelectedItem);
        Assert.Equal(3, ((ComboBox)view.FindName("AudioVideoCombo")).SelectedIndex);
    });

    /// <summary>
    /// A choice made on screen reaches the store's spelling and not the index. This is the whole
    /// finding, taken the long way round: through the combo, the binding and the view model.
    /// </summary>
    [Fact]
    public void AChoiceOnScreenBecomesTheStringTheStoreHolds() => OnSta(() =>
    {
        var model = new GeneralSettingsViewModel(new FakePreferences());
        var view = new GeneralSettingsView { DataContext = model };
        Realise(view);

        var combo = (ComboBox)view.FindName("DisconnectCombo");
        Assert.Equal(2, combo.SelectedIndex);
        Assert.Equal("ask", model.DisconnectStored);

        combo.SelectedIndex = 0;
        Realise(view);

        Assert.Equal(0, model.DisconnectIndex);
        Assert.Equal("nothing", model.DisconnectStored);

        var suspend = (ComboBox)view.FindName("SuspendCombo");
        suspend.SelectedIndex = 1;
        Realise(view);

        Assert.Equal("sleep", model.SuspendStored);
    });

    /// <summary>
    /// Unchecking the stream menu hides four combos. It is one bool driving a visibility that is
    /// not the bool's own property, so a silent raise here is a row that never disappears.
    /// </summary>
    [Fact]
    public void UncheckingTheStreamMenuHidesTheShortcutRow() => OnSta(() =>
    {
        var model = new GeneralSettingsViewModel();
        var view = new GeneralSettingsView { DataContext = model };
        Realise(view);

        var row = (FrameworkElement)view.FindName("ShortcutRow");
        var box = (CheckBox)view.FindName("StreamMenuBox");

        Assert.Equal(Visibility.Visible, row.Visibility);

        box.IsChecked = false;
        Realise(view);

        Assert.False(model.StreamMenuEnabled);
        Assert.Equal(Visibility.Collapsed, row.Visibility);

        box.IsChecked = true;
        Realise(view);
        Assert.Equal(Visibility.Visible, row.Visibility);
    });

    /// <summary>The log directory is shown and never typed into - a label and an Open button.</summary>
    [Fact]
    public void TheLogDirectoryIsShownAndNotEditable() => OnSta(() =>
    {
        var model = new GeneralSettingsViewModel();
        var view = new GeneralSettingsView { DataContext = model };
        Realise(view);

        var label = (TextBlock)view.FindName("LogDirectoryLabel");

        Assert.Equal(QtPaths.LogDirectory, label.Text);
        Assert.NotNull(view.FindName("OpenLogButton"));
    });
}
