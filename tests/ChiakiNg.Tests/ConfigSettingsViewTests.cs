using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Config tab as markup - the profile line, and the two hints that live inside their own
/// checkboxes rather than beside them.
/// </summary>
public class ConfigSettingsViewTests
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
    public void ItLoads() => OnSta(() => Assert.NotNull(new ConfigSettingsView()));

    /// <summary>The profile line names the unnamed profile, and repaints when one is chosen.</summary>
    [Fact]
    public void TheProfileLineNamesTheUnnamedOne() => OnSta(() =>
    {
        var model = new ConfigSettingsViewModel();
        var view = new ConfigSettingsView { DataContext = model };
        Realise(view);

        var caption = (TextBlock)view.FindName("ProfileCaption");
        Assert.Equal("Current Profile: default", caption.Text);

        model.Profile = "couch";
        Realise(view);
        Assert.Equal("Current Profile: couch", caption.Text);
    });

    /// <summary>
    /// The two hints are inside the checkboxes' own text. Asserted on the CONTENT rather than on a
    /// sibling label, which is the difference from the other eight tabs.
    /// </summary>
    [Fact]
    public void TheHintsAreInsideTheCheckboxesThemselves() => OnSta(() =>
    {
        var view = new ConfigSettingsView { DataContext = new ConfigSettingsViewModel() };
        Realise(view);

        var sanitize = (CheckBox)view.FindName("SanitizeBox");
        var verbose = (CheckBox)view.FindName("VerboseBox");

        Assert.Equal("Sanitize Logs (checked)", sanitize.Content);
        Assert.Equal("Verbose Logging (unchecked)", verbose.Content);

        // And each starts in the state its own text claims.
        Assert.True(sanitize.IsChecked);
        Assert.False(verbose.IsChecked);
    });

    /// <summary>The About button is built from the name the two clients share.</summary>
    [Fact]
    public void TheAboutButtonNamesThisApplication() => OnSta(() =>
    {
        var view = new ConfigSettingsView { DataContext = new ConfigSettingsViewModel() };
        Realise(view);

        Assert.Equal(
            ConfigSettingsViewModel.AboutCaption(QtPaths.Application),
            ((Button)view.FindName("AboutButton")).Content);
    });
}
