using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Native;
using ChiakiNg.Session;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Keys tab as markup - that every row is drawn, in the store's order, and that a rebind
/// moves the row on screen without an ItemsSource assignment.
/// </summary>
public class KeySettingsViewTests
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
        element.Measure(new Size(900, 1200));
        element.Arrange(new Rect(0, 0, 900, 1200));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoads() => OnSta(() => Assert.NotNull(new KeySettingsView()));

    /// <summary>Every binding gets a row - the tab is never partly empty.</summary>
    [Fact]
    public void EveryBindingIsDrawn() => OnSta(() =>
    {
        var model = new KeySettingsViewModel();
        var view = new KeySettingsView { DataContext = model };
        Realise(view);

        var grid = (ItemsControl)view.FindName("BindingsGrid");

        Assert.Equal(KeyMap.Defaults.Count, grid.Items.Count);
        Assert.Equal(26, grid.Items.Count);
    });

    /// <summary>
    /// And in the store's order, which is by button value - so Cross is first and the stick
    /// half-axes are last.
    /// </summary>
    [Fact]
    public void TheRowsAreInTheStoresOrder() => OnSta(() =>
    {
        var model = new KeySettingsViewModel();
        var view = new KeySettingsView { DataContext = model };
        Realise(view);

        var grid = (ItemsControl)view.FindName("BindingsGrid");

        Assert.Equal("Cross", ((KeyBinding)grid.Items[0]!).ButtonName);
        Assert.Contains("Stick", ((KeyBinding)grid.Items[^1]!).ButtonName);
    });

    /// <summary>
    /// A rebind moves the row on screen, and the bound collection is the same instance afterwards -
    /// no ItemsSource was replaced.
    /// </summary>
    [Fact]
    public void ARebindMovesTheRowWithoutReplacingTheSource() => OnSta(() =>
    {
        var model = new KeySettingsViewModel();
        var view = new KeySettingsView { DataContext = model };
        Realise(view);

        var grid = (ItemsControl)view.FindName("BindingsGrid");
        object? boundBefore = grid.ItemsSource;

        Assert.Equal("Return", ((KeyBinding)grid.Items[0]!).KeyName);

        model.Rebind(0, "Space");
        Realise(view);

        Assert.Same(boundBefore, grid.ItemsSource);
        Assert.Equal("Space", ((KeyBinding)grid.Items[0]!).KeyName);
    });

    /// <summary>Clear puts every row back rather than emptying the grid.</summary>
    [Fact]
    public void ClearRestoresTheRowsOnScreen() => OnSta(() =>
    {
        var model = new KeySettingsViewModel();
        var view = new KeySettingsView { DataContext = model };
        Realise(view);

        model.Rebind(0, "Space");
        Realise(view);

        var grid = (ItemsControl)view.FindName("BindingsGrid");
        Assert.Equal("Space", ((KeyBinding)grid.Items[0]!).KeyName);

        model.Clear();
        Realise(view);

        Assert.Equal(26, grid.Items.Count);
        Assert.Equal("Return", ((KeyBinding)grid.Items[0]!).KeyName);
    });

    /// <summary>The two checkboxes reach the model.</summary>
    [Fact]
    public void TheCheckboxesReachTheModel() => OnSta(() =>
    {
        var model = new KeySettingsViewModel();
        var view = new KeySettingsView { DataContext = model };
        Realise(view);

        var keyboard = (CheckBox)view.FindName("KeyboardEnabledBox");
        Assert.True(keyboard.IsChecked);

        keyboard.IsChecked = false;
        Realise(view);
        Assert.False(model.KeyboardEnabled);

        ((CheckBox)view.FindName("MouseTouchEnabledBox")).IsChecked = false;
        Realise(view);
        Assert.False(model.MouseTouchEnabled);
    });

    /// <summary>
    /// A stored binding shows the stored key rather than the default, and the two inverted stick
    /// labels are on screen as the Qt client writes them.
    /// </summary>
    [Fact]
    public void AStoredBindingAndTheInvertedLabelsAreOnScreen() => OnSta(() =>
    {
        var model = new KeySettingsViewModel(
            new FakePreferences(),
            key => key == "keymap/cross" ? "Space" : null);

        var view = new KeySettingsView { DataContext = model };
        Realise(view);

        var grid = (ItemsControl)view.FindName("BindingsGrid");
        var rows = grid.Items.Cast<KeyBinding>().ToList();

        Assert.Equal("Space", rows.First(r => r.ButtonName == "Cross").KeyName);

        // X_UP reads "Right" on screen, which is what PP5's sign asymmetry looks like to a user.
        Assert.Contains(rows, r => r.ButtonName == "Left Stick Right");
        Assert.Contains(rows, r => r.ButtonName == "Left Stick Left");
        Assert.Equal(
            "Left Stick Right",
            rows.First(r => r.ButtonValue == (int)ControllerButtonExt.AnalogStickLeftXUp).ButtonName);
    });
}
