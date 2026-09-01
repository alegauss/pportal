using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Session;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Winwright.InApp;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Consoles tab as markup - that both lists fill from the bound collections, and that a
/// refill keeps the instance the markup holds.
/// </summary>
public class ConsoleSettingsViewTests
{
    private static RegisteredHost Host(string name, byte last) => new()
    {
        ServerNickname = name,
        ServerMac = [0x90, 0x47, 0x48, 0x82, 0xfc, last],
        Target = (int)ChiakiTarget.Ps5_1,
    };


    private static void Realise(FrameworkElement element)
    {
        element.Measure(new Size(900, 800));
        element.Arrange(new Rect(0, 0, 900, 800));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoads() => Apartment.Run(() => Assert.NotNull(new ConsoleSettingsView()));

    /// <summary>Both lists fill from the view model, and the captions are the model's.</summary>
    [Fact]
    public void BothListsFillFromTheModel() => Apartment.Run(() =>
    {
        var model = new ConsoleSettingsViewModel();
        model.Load(
            [Host("Living room", 0x29), Host("Bedroom", 0x2a)],
            [new HiddenHost("Spare", [0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0x01])]);

        var view = new ConsoleSettingsView { DataContext = model };
        Realise(view);

        var registered = (ListBox)view.FindName("RegisteredList");
        var hidden = (ListBox)view.FindName("HiddenList");

        Assert.Equal(2, registered.Items.Count);
        Assert.Single(hidden.Items);
        Assert.Equal("90:47:48:82:fc:29 (PS5, Living room)", registered.Items[0]);
        Assert.Equal("aa:bb:cc:dd:ee:01 (Spare)", hidden.Items[0]);
    });

    /// <summary>
    /// A refill keeps the collection the markup bound, so the lists follow without an ItemsSource
    /// assignment - PP159's rule, applied where it costs nothing rather than after it has cost
    /// something.
    /// </summary>
    [Fact]
    public void ARefillKeepsTheBoundCollection() => Apartment.Run(() =>
    {
        var model = new ConsoleSettingsViewModel();
        model.Load([Host("A", 1), Host("B", 2)], []);

        var view = new ConsoleSettingsView { DataContext = model };
        Realise(view);

        var registered = (ListBox)view.FindName("RegisteredList");
        object? boundBefore = registered.ItemsSource;
        Assert.Equal(2, registered.Items.Count);

        model.Load([Host("A", 1)], [new HiddenHost("X", [1, 2, 3, 4, 5, 6])]);
        Realise(view);

        Assert.Same(boundBefore, registered.ItemsSource);
        Assert.Single(registered.Items);
        Assert.Single(((ListBox)view.FindName("HiddenList")).Items);
    });

    /// <summary>Turning streamer mode on rewrites both lists' captions in place.</summary>
    [Fact]
    public void StreamerModeRewritesBothListsOnScreen() => Apartment.Run(() =>
    {
        var model = new ConsoleSettingsViewModel();
        model.Load(
            [Host("Living room", 0x29)],
            [new HiddenHost("Spare", [0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0x01])]);

        var view = new ConsoleSettingsView { DataContext = model };
        Realise(view);

        var registered = (ListBox)view.FindName("RegisteredList");
        var hidden = (ListBox)view.FindName("HiddenList");

        Assert.Contains("90:47", (string)registered.Items[0]!);

        model.StreamerMode = true;
        Realise(view);

        Assert.StartsWith("hidden (", (string)registered.Items[0]!);
        Assert.StartsWith("hidden (", (string)hidden.Items[0]!);

        // The name survives - it is the address that is private.
        Assert.Contains("Living room", (string)registered.Items[0]!);
        Assert.Contains("Spare", (string)hidden.Items[0]!);
    });

    /// <summary>An empty tab draws two empty lists rather than failing.</summary>
    [Fact]
    public void AnEmptyTabDrawsTwoEmptyLists() => Apartment.Run(() =>
    {
        var view = new ConsoleSettingsView { DataContext = new ConsoleSettingsViewModel() };
        Realise(view);

        Assert.Empty(((ListBox)view.FindName("RegisteredList")).Items);
        Assert.Empty(((ListBox)view.FindName("HiddenList")).Items);
        Assert.NotNull(view.FindName("RegisterNewButton"));
    });
}
