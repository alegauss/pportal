using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Winwright.InApp;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP167: the dialog the nine tabs did not add up to - an order that is an index, a first control
/// that is computed on one tab, and two keys that stop at the ends.
/// </summary>
public class SettingsTabsTests
{
    /// <summary>
    /// The order IS the index, and two switches in the QML dispatch on it. Asserted as numbers
    /// rather than as a sequence of names, because the numbers are what those switches mean.
    /// </summary>
    [Fact]
    public void TheOrderIsTheIndexTheSwitchesDispatchOn()
    {
        Assert.Equal(0, (int)SettingsTab.General);
        Assert.Equal(3, (int)SettingsTab.AudioWifi);
        Assert.Equal(6, (int)SettingsTab.Controllers);
        Assert.Equal(8, (int)SettingsTab.Config);

        Assert.Equal(9, SettingsTabsViewModel.Order.Count);
    }

    /// <summary>Eight tabs name a fixed first control.</summary>
    [Theory]
    [InlineData(SettingsTab.General, "disconnectAction")]
    [InlineData(SettingsTab.Video, "hwDecoderCombo")]
    [InlineData(SettingsTab.Keys, "resetAllKeys")]
    [InlineData(SettingsTab.Controllers, "controllerMappingChange")]
    [InlineData(SettingsTab.Config, "profile")]
    public void EightTabsNameTheirFirstControl(SettingsTab tab, string expected)
        => Assert.Equal(expected, new SettingsTabsViewModel { Current = tab }.FirstControl);

    /// <summary>
    /// And the ninth computes it, because two of its controls are never on screen together - the
    /// Login and Clear buttons PP165 found. A fixed first item there would focus a hidden button.
    /// </summary>
    [Fact]
    public void TheRemoteTabComputesItsFirstControl()
    {
        var model = new SettingsTabsViewModel { Current = SettingsTab.Remote };

        // Not logged in: Login is on screen and takes the focus.
        Assert.Equal("openPsnLogin", model.FirstControl);

        // Logged in: the button that replaced it does.
        model.LoginVisible = false;
        model.ClearVisible = true;
        Assert.Equal("resetPsnTokens", model.FirstControl);

        // Neither: the next control down.
        model.ClearVisible = false;
        Assert.Equal("holePunchGuessingCheckbox", model.FirstControl);

        // Nothing at all: the scroll area itself, so focus still lands somewhere.
        model.PortGuessingVisible = false;
        Assert.Equal("remoteFlick", model.FirstControl);
    }

    /// <summary>
    /// Page Up and Page Down stop at the ends rather than wrapping - the strip has ends, which is
    /// what decrementCurrentIndex and incrementCurrentIndex do.
    /// </summary>
    [Fact]
    public void ThePagingKeysStopAtTheEnds()
    {
        var model = new SettingsTabsViewModel();

        model.PreviousTab();
        Assert.Equal(SettingsTab.General, model.Current);

        model.NextTab();
        Assert.Equal(SettingsTab.Video, model.Current);

        model.Current = SettingsTab.Config;
        model.NextTab();
        Assert.Equal(SettingsTab.Config, model.Current);
    }

    /// <summary>An index outside the nine changes nothing rather than throwing.</summary>
    [Fact]
    public void AnIndexOutsideTheNineIsIgnored()
    {
        var model = new SettingsTabsViewModel { Current = SettingsTab.Keys };

        model.CurrentIndex = 99;
        model.CurrentIndex = -1;

        Assert.Equal(SettingsTab.Keys, model.Current);
    }

    /// <summary>The dialog draws the nine, in that order, each holding its own tab's screen.</summary>
    [Fact]
    public void TheDialogDrawsTheNineInOrder() => Apartment.Run(() =>
    {
        var view = new SettingsView { DataContext = new SettingsTabsViewModel() };
        view.Measure(new Size(1200, 900));
        view.Arrange(new Rect(0, 0, 1200, 900));
        view.UpdateLayout();

        var tabs = (TabControl)view.FindName("Tabs");

        Assert.Equal(SettingsTabsViewModel.Order.Count, tabs.Items.Count);

        for (int i = 0; i < tabs.Items.Count; i++)
        {
            SettingsTab tab = SettingsTabsViewModel.Order[i];
            Assert.Equal(SettingsTabsViewModel.Labels[tab], ((TabItem)tabs.Items[i]).Header);
        }

        Assert.IsType<GeneralSettingsView>(((TabItem)tabs.Items[0]).Content);
        Assert.IsType<ConfigSettingsView>(((TabItem)tabs.Items[8]).Content);
        Assert.IsType<ControllerSettingsView>(((TabItem)tabs.Items[6]).Content);
    });

    /// <summary>Every rule above, still stated the same way in the screen.</summary>
    [Fact]
    public void TheDialogsRulesAreStillTheQtClients()
    {
        string? file = SettingsTabsSource.LocateQml();
        if (file is null)
            return;

        string qml = File.ReadAllText(file);

        Assert.True(SettingsTabsSource.TheNineAreStillInThisOrder(qml), "the nine, in order");
        Assert.True(SettingsTabsSource.TheFocusSwitchIsStillNumbered(qml), "a numbered focus switch");
        Assert.True(SettingsTabsSource.TheScrollSwitchIsStillNumbered(qml), "and a numbered scroll one");
        Assert.True(
            SettingsTabsSource.TheRemoteTabStillComputesItsFirstControl(qml),
            "the ninth computes its own");
        Assert.True(SettingsTabsSource.PagingIsStillTheTwoKeys(qml), "two paging keys");
    }
}
