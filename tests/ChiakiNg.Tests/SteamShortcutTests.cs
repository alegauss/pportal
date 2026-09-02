using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Winwright.InApp;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP19: the shortcut dialog, and the four things about it that are not layout.
/// </summary>
public class SteamShortcutTests
{
    /// <summary>
    /// The defaults come from the profile by TRUTHINESS, so the empty string takes the no-profile
    /// branch - the same "" against "default" distinction PP14 found in the profile dialog.
    /// </summary>
    [Theory]
    [InlineData("", "chiaki-ng", "")]
    [InlineData("couch", "chiaki-ng couch", "--profile=couch")]
    [InlineData("work", "chiaki-ng work", "--profile=work")]
    public void TheDefaultsComeFromTheProfile(string profile, string name, string options)
    {
        Assert.Equal(name, SteamShortcutViewModel.DefaultName(profile));
        Assert.Equal(options, SteamShortcutViewModel.DefaultOptions(profile));

        var model = new SteamShortcutViewModel(profile);
        Assert.Equal(name, model.Name);
        Assert.Equal(options, model.Options);
    }

    /// <summary>
    /// With no profile the launch options are NOTHING, not a flag with an empty value - which is
    /// what a port filling in a template would produce, and what Steam would then pass through.
    /// </summary>
    [Fact]
    public void WithNoProfileThereIsNoFlagAtAll()
    {
        Assert.Equal("", SteamShortcutViewModel.DefaultOptions(""));
        Assert.Equal("", SteamShortcutViewModel.DefaultOptions(null));
        Assert.DoesNotContain("--profile", SteamShortcutViewModel.DefaultOptions(""));
    }

    /// <summary>
    /// The name gets a SPACE and the option gets an equals sign. Two different joins for the same
    /// value, and getting either wrong shows up in Steam's library rather than here.
    /// </summary>
    [Fact]
    public void TheTwoJoinsAreDifferent()
    {
        Assert.Equal("chiaki-ng couch", SteamShortcutViewModel.DefaultName("couch"));
        Assert.Equal("--profile=couch", SteamShortcutViewModel.DefaultOptions("couch"));
    }

    /// <summary>
    /// The button rule: a trimmed name and no creation in flight. The options never block it, which
    /// is what "optional" has to mean here.
    /// </summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(" chiaki-ng ", true)]
    public void TheButtonNeedsATrimmedName(string name, bool can)
    {
        var model = new SteamShortcutViewModel { Name = name, Options = "" };
        Assert.Equal(can, model.CanCreate);
    }

    [Fact]
    public void ACreationInFlightRefusesASecond()
    {
        var model = new SteamShortcutViewModel("couch");
        Assert.True(model.CanCreate);

        model.Accept();
        Assert.True(model.Opening);
        Assert.False(model.CanCreate);

        // And only "done" gives the button back.
        model.Progress("working", ok: false, done: false);
        Assert.False(model.CanCreate);

        model.Progress("finished", ok: false, done: true);
        Assert.True(model.CanCreate);
    }

    /// <summary>
    /// The finding: "stop asking" is written only on SUCCESS, from inside the callback. A failed
    /// creation leaves the prompt to come back - the opposite of a port that wrote the preference
    /// when Create was pressed.
    /// </summary>
    [Fact]
    public void OnlyASuccessStopsThePromptComingBack()
    {
        var failed = new SteamShortcutViewModel("couch");
        failed.Accept();
        failed.Progress("could not find Steam", ok: false, done: true);

        Assert.False(failed.Succeeded);
        Assert.Null(failed.StoppedAskingKey);

        var worked = new SteamShortcutViewModel("couch");
        worked.Accept();
        worked.Progress("created", ok: true, done: true);

        Assert.True(worked.Succeeded);
        Assert.Equal("settings/add_steam_shortcut_ask", worked.StoppedAskingKey);
    }

    /// <summary>
    /// And `ok` is checked before `done`, so a run reporting success and then continuing to log has
    /// already stopped the prompt - the two flags are independent.
    /// </summary>
    [Fact]
    public void SuccessCountsBeforeTheRunIsFinished()
    {
        var model = new SteamShortcutViewModel("couch");
        model.Accept();

        model.Progress("created", ok: true, done: false);
        Assert.True(model.Succeeded);
        Assert.True(model.Opening);       // still running

        model.Progress("tidying up", ok: false, done: true);
        Assert.True(model.Succeeded);     // not un-set by a later report
        Assert.False(model.Opening);
    }

    /// <summary>Both fields are trimmed on the way out; the base path is passed through as-is.</summary>
    [Fact]
    public void TheRequestTrimsTheFieldsAndNotThePath()
    {
        var model = new SteamShortcutViewModel
        {
            Name = "  chiaki-ng couch  ",
            Options = "  --profile=couch  ",
            SteamBasePath = @"  C:\Steam  ",
        };

        (string name, string options, string basePath) = model.Request();

        Assert.Equal("chiaki-ng couch", name);
        Assert.Equal("--profile=couch", options);
        Assert.Equal(@"  C:\Steam  ", basePath);
    }

    /// <summary>
    /// The PSN chain, which this dialog carries a SECOND COPY of in the QML. The port has one: the
    /// follow-up and the token test are the remind dialog's, so the two screens cannot disagree.
    /// </summary>
    [Fact]
    public void ClosingChainsThroughTheSameModelAsTheRemindPrompt()
    {
        var fromReminder = new SteamShortcutViewModel("", fromReminder: true);
        fromReminder.Closed(new FakePreferences());
        Assert.Equal(RemindFollowUp.ShowPsnPrompt, fromReminder.FollowUp);

        var linked = new SteamShortcutViewModel("", fromReminder: true);
        linked.Closed(Linked());
        Assert.Equal(RemindFollowUp.ClearRemotePlayAsk, linked.FollowUp);

        // Opened from the menu rather than the prompt: no chain.
        var direct = new SteamShortcutViewModel("", fromReminder: false);
        direct.Closed(new FakePreferences());
        Assert.Equal(RemindFollowUp.None, direct.FollowUp);

        // And nothing chains once remote play has been declined.
        var declined = new SteamShortcutViewModel("", fromReminder: true);
        declined.Closed(new FakePreferences().Set("settings/remote_play_ask", false));
        Assert.Equal(RemindFollowUp.None, declined.FollowUp);
    }

    /// <summary>
    /// The port's one copy really is one: this dialog and the remind prompt reach the same answer
    /// for the same store, which is what the two QML copies could drift on.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothScreensAgreeAboutTheFollowUp(bool linked)
    {
        FakePreferences store = linked ? Linked() : new FakePreferences();

        var shortcut = new SteamShortcutViewModel("", fromReminder: true);
        shortcut.Closed(store);

        var remind = new RemindDialogViewModel { IsRemotePlay = false };
        remind.Later();
        remind.Closed(store);

        Assert.Equal(remind.FollowUp, shortcut.FollowUp);
    }

    /// <summary>Every rule above is still the QML's own, including the duplication.</summary>
    [Fact]
    public void TheRulesAreStillTheQmlsOwn()
    {
        if (SteamShortcutSource.Locate() is null)
            return;

        string qml = File.ReadAllText(SteamShortcutSource.Locate()!);

        Assert.True(SteamShortcutSource.TheButtonNeedsATrimmedNameAndNoRun(qml), "button rule");
        Assert.True(SteamShortcutSource.TheDefaultsComeFromTheProfile(qml), "profile defaults");
        Assert.True(SteamShortcutSource.OnlySuccessStopsAsking(qml), "success only");
        Assert.True(SteamShortcutSource.ThePsnChainIsDuplicatedHere(qml), "the second chain");
    }

    /// <summary>The screen loads and the button follows the rule through the binding.</summary>
    [Fact]
    public void TheViewFollowsTheRule()
    {
        Apartment.Run(
            () =>
            {
                var model = new SteamShortcutViewModel("couch");
                var view = new SteamShortcutView { DataContext = model };
                view.Measure(new Size(600, 500));
                view.Arrange(new Rect(0, 0, 600, 500));
                view.UpdateLayout();

                var create = (Button)view.FindName("CreateButton");
                Assert.True(create.IsEnabled);
                Assert.Equal("chiaki-ng couch", ((TextBox)view.FindName("NameField")).Text);
                Assert.Equal("--profile=couch", ((TextBox)view.FindName("OptionsField")).Text);

                ((TextBox)view.FindName("NameField")).Text = "   ";
                view.UpdateLayout();
                Assert.False(create.IsEnabled);
            },
            named: "the Steam shortcut screen");
    }

    private static FakePreferences Linked()
        => new FakePreferences()
            .Set("settings/psn_refresh_token", "r")
            .Set("settings/psn_auth_token", "a")
            .Set("settings/psn_auth_token_expiry", "2026-08-19 12:00:00 UTC")
            .Set("settings/psn_account_id", "Fx1AAAAAAAA=");
}
