using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP19: the two prompts, and the three things about them that are not obvious.
/// </summary>
public class PromptDialogTests
{
    /// <summary>
    /// Focus is handed back exactly ONCE on every path. Accept restores it and latches; reject with
    /// a callback does the same; reject without one leaves the close to do it.
    /// </summary>
    [Fact]
    public void FocusIsRestoredExactlyOnceOnEveryPath()
    {
        var accepted = new ConfirmDialogViewModel();
        accepted.Accept();
        accepted.Closed();
        Assert.Equal(1, accepted.RestoreCount);

        var rejectedWithCallback = new ConfirmDialogViewModel { HasRejectCallback = true };
        rejectedWithCallback.Reject();
        rejectedWithCallback.Closed();
        Assert.Equal(1, rejectedWithCallback.RestoreCount);

        var rejectedWithout = new ConfirmDialogViewModel();
        rejectedWithout.Reject();
        rejectedWithout.Closed();
        Assert.Equal(1, rejectedWithout.RestoreCount);

        // And a dismissal that is neither button.
        var dismissed = new ConfirmDialogViewModel();
        dismissed.Closed();
        Assert.Equal(1, dismissed.RestoreCount);
    }

    /// <summary>
    /// The latch is what makes that true, and it is what a port would get wrong by naming it after
    /// what the QML calls it: a flag set only when a dialog really was opening would let the close
    /// restore focus a second time, off whatever the callback just opened.
    /// </summary>
    [Fact]
    public void TheLatchIsWhatStopsTheSecondRestore()
    {
        var model = new ConfirmDialogViewModel();

        Assert.False(model.FocusAlreadyRestored);
        model.Accept();
        Assert.True(model.FocusAlreadyRestored);

        // Any number of closes after that change nothing.
        model.Closed();
        model.Closed();
        Assert.Equal(1, model.RestoreCount);
    }

    /// <summary>
    /// And it is never cleared, so a reused instance carries it - the second dismissal restores
    /// nothing. Reproduced rather than fixed, because the QML creates these per use and quietly
    /// resetting it would be a divergence nobody could see.
    /// </summary>
    [Fact]
    public void TheLatchIsStickyAcrossReuse()
    {
        var model = new ConfirmDialogViewModel();
        model.Accept();
        model.Closed();
        Assert.Equal(1, model.RestoreCount);

        // A second cycle on the same instance: the latch is still set, so nothing is restored.
        model.Reject();
        model.Closed();
        Assert.Equal(1, model.RestoreCount);
    }

    /// <summary>
    /// The remind prompt has three answers, and only ONE of them writes anything. "Later" writing
    /// nothing is what makes it a deferral rather than a refusal - a port that folded it into "no"
    /// would turn every "ask me again" into "never ask again".
    /// </summary>
    [Fact]
    public void OnlyDecliningWritesThePreference()
    {
        var accepted = new RemindDialogViewModel { IsRemotePlay = true };
        accepted.Accept();
        Assert.Equal(PromptOutcome.Accepted, accepted.Outcome);
        Assert.Null(accepted.DeclinedKey);

        var later = new RemindDialogViewModel { IsRemotePlay = true };
        later.Later();
        Assert.Equal(PromptOutcome.Later, later.Outcome);
        Assert.Null(later.DeclinedKey);

        var declined = new RemindDialogViewModel { IsRemotePlay = true };
        declined.Reject();
        Assert.Equal(PromptOutcome.Declined, declined.Outcome);
        Assert.Equal(RemindDialogViewModel.RemotePlayAskKey, declined.DeclinedKey);
    }

    /// <summary>One flag picks between two preferences, and they are not interchangeable.</summary>
    [Theory]
    [InlineData(true, "settings/remote_play_ask")]
    [InlineData(false, "settings/add_steam_shortcut_ask")]
    public void TheFlagPicksWhichPreferenceADeclineWrites(bool remotePlay, string key)
    {
        var model = new RemindDialogViewModel { IsRemotePlay = remotePlay };
        Assert.Equal(key, model.AskKey);

        model.Reject();
        Assert.Equal(key, model.DeclinedKey);
    }

    /// <summary>
    /// The chain: the STEAM prompt's closure is what opens the remote-play one. It runs only when
    /// remotePlayAsk is still set, and only when the four PSN values are not all present - if they
    /// are, it stops asking instead of prompting for something already done.
    /// </summary>
    [Fact]
    public void ClosingTheSteamPromptChainsIntoThePsnPrompt()
    {
        var unlinked = new RemindDialogViewModel { IsRemotePlay = false };
        unlinked.Later();
        unlinked.Closed(new FakePreferences());
        Assert.Equal(RemindFollowUp.ShowPsnPrompt, unlinked.FollowUp);

        var linked = new RemindDialogViewModel { IsRemotePlay = false };
        linked.Later();
        linked.Closed(Linked());
        Assert.Equal(RemindFollowUp.ClearRemotePlayAsk, linked.FollowUp);
    }

    /// <summary>
    /// Accepting the Steam prompt suppresses the chain entirely - the same latch that stops the
    /// second focus restore stops the follow-up.
    /// </summary>
    [Fact]
    public void AcceptingTheSteamPromptSuppressesTheChain()
    {
        var model = new RemindDialogViewModel { IsRemotePlay = false };
        model.Accept();
        model.Closed(new FakePreferences());

        Assert.Equal(RemindFollowUp.None, model.FollowUp);
        Assert.Equal(1, model.RestoreCount);
    }

    /// <summary>The remote-play prompt does not chain into itself.</summary>
    [Fact]
    public void TheRemotePlayPromptDoesNotChain()
    {
        var model = new RemindDialogViewModel { IsRemotePlay = true };
        model.Later();
        model.Closed(new FakePreferences());

        Assert.Equal(RemindFollowUp.None, model.FollowUp);
    }

    /// <summary>And nothing chains once remote play has been declined.</summary>
    [Fact]
    public void NothingChainsOnceRemotePlayHasBeenDeclined()
    {
        var model = new RemindDialogViewModel { IsRemotePlay = false };
        model.Later();
        model.Closed(new FakePreferences().Set("settings/remote_play_ask", false));

        Assert.Equal(RemindFollowUp.None, model.FollowUp);
    }

    /// <summary>
    /// "PSN is linked" is all four values, not any of them - a partial link still prompts, which is
    /// what a half-finished login leaves behind.
    /// </summary>
    [Fact]
    public void PsnIsLinkedOnlyWhenAllFourArePresent()
    {
        Assert.True(RemindDialogViewModel.PsnIsLinked(Linked()));

        foreach (string missing in RemindDialogViewModel.PsnTokenKeys)
        {
            FakePreferences partial = Linked().Set(missing, null);
            Assert.False(RemindDialogViewModel.PsnIsLinked(partial), $"without {missing}");

            // An empty string counts as absent too, which is what a cleared setting looks like.
            Assert.False(RemindDialogViewModel.PsnIsLinked(Linked().Set(missing, "")), $"empty {missing}");
        }
    }

    /// <summary>Every rule above is still the QML's own.</summary>
    [Fact]
    public void TheRulesAreStillTheQmlsOwn()
    {
        string? confirm = PromptDialogSource.Locate(PromptDialogSource.ConfirmDialog);
        string? remind = PromptDialogSource.Locate(PromptDialogSource.RemindDialog);
        if (confirm is null || remind is null)
            return;

        string confirmText = File.ReadAllText(confirm);
        string remindText = File.ReadAllText(remind);

        Assert.True(PromptDialogSource.AcceptRestoresFocusUnderTheLatch(confirmText), "accept latch");
        Assert.True(PromptDialogSource.CloseIsGuardedByTheLatch(confirmText), "close guard");
        Assert.True(PromptDialogSource.TheLatchIsNeverCleared(confirmText), "latch never cleared");
        Assert.True(PromptDialogSource.RejectRestoresOnlyWithACallback(confirmText), "reject callback");

        Assert.True(PromptDialogSource.AcceptRestoresFocusUnderTheLatch(remindText), "remind accept");
        Assert.True(PromptDialogSource.CloseIsGuardedByTheLatch(remindText), "remind close guard");
        Assert.True(PromptDialogSource.DeclinePicksBetweenTwoKeys(remindText), "two keys");
        Assert.True(PromptDialogSource.LaterIsAClose(remindText), "later is a close");
        Assert.True(PromptDialogSource.TheYesKeyClosesRatherThanAccepting(remindText), "the yes key");
        Assert.True(PromptDialogSource.CloseChainsIntoThePsnPrompt(remindText), "the chain");

        Assert.True(
            PromptDialogSource.ThePsnCheckIsTheseFour(
                remindText,
                ["psnRefreshToken", "psnAuthToken", "psnAuthTokenExpiry", "psnAccountId"]),
            "the four psn settings");
    }

    private static FakePreferences Linked()
        => new FakePreferences()
            .Set("settings/psn_refresh_token", "r")
            .Set("settings/psn_auth_token", "a")
            .Set("settings/psn_auth_token_expiry", "2026-08-19 12:00:00 UTC")
            .Set("settings/psn_account_id", "Fx1AAAAAAAA=");
}
