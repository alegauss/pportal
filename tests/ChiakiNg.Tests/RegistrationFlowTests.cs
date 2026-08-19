using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP14: the flow the four dialogs turn out to be - which is one dialog on a path, and three
/// reached from elsewhere.
/// </summary>
public class RegistrationFlowTests
{
    private static readonly byte[] EightBytes = [1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>
    /// The identifier is text for one console and bytes for the other three, and the one it is
    /// text for is the OLDEST - a PS4 below firmware 7.0, whose constant is called PS4_8.
    /// </summary>
    [Theory]
    [InlineData(ConsoleTarget.Ps4Below7, true)]
    [InlineData(ConsoleTarget.Ps4From7, false)]
    [InlineData(ConsoleTarget.Ps4From8, false)]
    [InlineData(ConsoleTarget.Ps5, false)]
    public void OneConsoleWantsAnOnlineIdAndTheRestWantAnAccountId(ConsoleTarget target, bool online)
    {
        Assert.Equal(online, Registration.WantsOnlineId(target));
        Assert.Equal(!online, Registration.WantsAccountId(target));
    }

    /// <summary>
    /// The constants are one version ahead of their labels, which is the whole reason this enum
    /// is named for firmware. Reading PS4_8 as "firmware 8" picks the wrong console by two rows.
    /// </summary>
    [Fact]
    public void TheConstantNamesAreOneVersionAheadOfTheirFirmware()
    {
        Assert.Equal(800, (int)ConsoleTarget.Ps4Below7);
        Assert.Equal(900, (int)ConsoleTarget.Ps4From7);
        Assert.Equal(1000, (int)ConsoleTarget.Ps4From8);
        Assert.Equal(1000100, (int)ConsoleTarget.Ps5);
    }

    /// <summary>A DUID buys the user a question first; without one the dialog opens.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0000000000000000", true)]
    public void ADuidOffersAutomaticRegistrationFirst(string? duid, bool offers)
        => Assert.Equal(offers, Registration.OffersAutomatic(duid));

    /// <summary>An online id goes through as typed, trimmed, with no bytes anywhere.</summary>
    [Fact]
    public void AnOnlineIdIsSentAsText()
    {
        RegistrationRequest request = Registration.Prepare(
            " 192.168.1.9 ", ConsoleTarget.Ps4Below7, "  CoolName  ", "12345678", "",
            out RegistrationRefusal refusal)!;

        Assert.Equal(RegistrationRefusal.None, refusal);
        Assert.Equal("192.168.1.9", request.Host);
        Assert.Equal("CoolName", request.OnlineId);
        Assert.Null(request.AccountId);
        Assert.Equal(12345678u, request.Pin);
        Assert.Equal(0u, request.ConsolePin);
        Assert.False(request.Broadcast);
    }

    /// <summary>
    /// And an account id becomes eight bytes. It is not validated as text anywhere - the only
    /// question asked of it is what it decodes to.
    /// </summary>
    [Fact]
    public void AnAccountIdIsSentAsEightBytes()
    {
        RegistrationRequest request = Registration.Prepare(
            "192.168.1.9", ConsoleTarget.Ps5, LenientBase64.Encode(EightBytes), "12345678", "4321",
            out RegistrationRefusal refusal)!;

        Assert.Equal(RegistrationRefusal.None, refusal);
        Assert.Null(request.OnlineId);
        Assert.Equal(EightBytes, request.AccountId);
        Assert.Equal(4321u, request.ConsolePin);
    }

    /// <summary>
    /// An account id of the wrong length refuses the registration before anything opens - which is
    /// the case the progress dialog must NOT open for, or it fills with nothing and cannot close.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("AAAA")]
    [InlineData("AAAAAAAAAAAAAAAA")]
    public void AnAccountIdOfTheWrongLengthRefusesBeforeAnythingOpens(string id)
    {
        RegistrationRequest? request = Registration.Prepare(
            "192.168.1.9", ConsoleTarget.Ps5, id, "12345678", "",
            out RegistrationRefusal refusal);

        Assert.Null(request);
        Assert.Equal(RegistrationRefusal.InvalidAccountId, refusal);
    }

    /// <summary>
    /// The empty online id is NOT refused here, because nothing checks it. The button is what
    /// stops an empty one, and this half of the flow trusts it - stated so a reader does not go
    /// looking for a check that is not there.
    /// </summary>
    [Fact]
    public void AnEmptyOnlineIdIsNotRefusedByThisHalf()
    {
        RegistrationRequest request = Registration.Prepare(
            "192.168.1.9", ConsoleTarget.Ps4Below7, "", "12345678", "",
            out RegistrationRefusal refusal)!;

        Assert.Equal(RegistrationRefusal.None, refusal);
        Assert.Equal("", request.OnlineId);
    }

    /// <summary>
    /// Broadcast is not a checkbox - it is an address. Settings opens the dialog on
    /// 255.255.255.255 to register whatever answers, and that address IS the flag.
    /// </summary>
    [Theory]
    [InlineData("255.255.255.255", true)]
    [InlineData(" 255.255.255.255 ", true)]
    [InlineData("255.255.255.254", false)]
    public void TheBroadcastAddressIsTheBroadcastFlag(string host, bool broadcast)
    {
        RegistrationRequest request = Registration.Prepare(
            host, ConsoleTarget.Ps4Below7, "name", "12345678", "",
            out _)!;

        Assert.Equal(broadcast, request.Broadcast);
    }

    /// <summary>An unreadable PIN is zero rather than an error, which is what Qt's toULong does.</summary>
    [Theory]
    [InlineData("", 0u)]
    [InlineData("00001234", 1234u)]
    [InlineData("not a pin", 0u)]
    [InlineData("-1", 0u)]
    public void APinThatCannotBeReadIsZero(string text, uint expected)
        => Assert.Equal(expected, Registration.Digits(text));

    /// <summary>
    /// The lenient decode, which is the part a port gets wrong by being careful. An account id
    /// copied out of a JSON blob arrives inside its quotes, and Qt skips them - so it registers a
    /// console where a strict decoder answers "Invalid Account-ID" for an id that is correct.
    /// </summary>
    [Fact]
    public void AnAccountIdPastedWithItsQuotesStillDecodes()
    {
        string clean = LenientBase64.Encode(EightBytes);
        string pasted = "\"" + clean[..5] + "\n " + clean[5..] + "\"";

        Assert.Equal(EightBytes, LenientBase64.Decode(pasted));

        // And it is accepted, where a strict decoder would answer "Invalid Account-ID".
        RegistrationRequest request = Registration.Prepare(
            "192.168.1.9", ConsoleTarget.Ps5, pasted, "12345678", "", out _)!;

        Assert.Equal(EightBytes, request.AccountId);
    }

    /// <summary>
    /// The padding is skipped like anything else, which is why the twelve characters an eight-byte
    /// id is normally written as come back as eight bytes and not nine: eleven digits are
    /// sixty-six bits, and the two left over are dropped rather than rounded up.
    /// </summary>
    [Fact]
    public void ThePaddingIsSkippedAndTheLeftoverBitsAreDropped()
    {
        string encoded = LenientBase64.Encode(EightBytes);

        Assert.Equal(12, encoded.Length);
        Assert.EndsWith("=", encoded, StringComparison.Ordinal);

        Assert.Equal(EightBytes, LenientBase64.Decode(encoded));
        Assert.Equal(EightBytes, LenientBase64.Decode(encoded.TrimEnd('=')));
    }

    /// <summary>
    /// And leniency is not laxity: characters it skips carry no bits, so garbage decodes to the
    /// wrong LENGTH rather than to the wrong bytes, and the length check is what catches it.
    /// </summary>
    [Fact]
    public void SkippedCharactersCarryNoBits()
    {
        Assert.Empty(LenientBase64.Decode("!!!!!!!!!!!!"));
        Assert.Equal(new byte[] { 0xff }, LenientBase64.Decode("//"));
    }

    /// <summary>
    /// A console that was typed in has to be written down, or it registers and then disappears
    /// from the list the moment the dialog closes - the registration having succeeded is exactly
    /// what makes that easy to miss.
    /// </summary>
    [Fact]
    public void AConsoleThatWasTypedInIsWrittenDownAndADiscoveredOneIsNot()
    {
        RegistrationRequest request = Registration.Prepare(
            "192.168.1.9", ConsoleTarget.Ps4Below7, "name", "12345678", "", out _)!;

        Assert.Null(Registration.SettleManualHost(request, discovered: true, null));
        Assert.Equal("192.168.1.9", Registration.SettleManualHost(request, discovered: false, null));
    }

    /// <summary>
    /// And registering a manual host that already has an address does not move it. The typed
    /// address is a fallback for an entry with none, not an update.
    /// </summary>
    [Fact]
    public void AnExistingManualHostKeepsItsOwnAddress()
    {
        RegistrationRequest request = Registration.Prepare(
            "192.168.1.9", ConsoleTarget.Ps4Below7, "name", "12345678", "", out _)!;

        Assert.Equal("console.lan",
            Registration.SettleManualHost(request, discovered: false, "console.lan"));
        Assert.Equal("192.168.1.9",
            Registration.SettleManualHost(request, discovered: false, ""));
    }

    /// <summary>
    /// And the shape it was all read out of. The console PIN check is the one that matters most:
    /// it is what says the four dialogs are not four steps.
    /// </summary>
    [Fact]
    public void TheFlowIsStillTheQtClients()
    {
        string? main = RegistrationFlowSource.Locate(RegistrationFlowSource.MainQml);
        string? list = RegistrationFlowSource.Locate(RegistrationFlowSource.MainViewQml);
        string? cpp = RegistrationFlowSource.Locate(RegistrationFlowSource.Backend);
        string? dialog = RegistDialogSource.Locate();
        if (main is null || list is null || cpp is null || dialog is null)
            return;

        Assert.True(
            RegistrationFlowSource.ADuidOffersAutomaticFirst(File.ReadAllText(main)),
            "a duid still asks before it opens the dialog");
        Assert.True(
            RegistrationFlowSource.TheConsolePinDialogIsOpenedFromTheList(File.ReadAllText(list)),
            "the console pin dialog is still reached from the console list");
        Assert.True(
            RegistrationFlowSource.TheOnlineIdIsForOneTargetOnly(File.ReadAllText(cpp)),
            "the online id is still for exactly one target");
        Assert.True(
            RegistrationFlowSource.TheAccountIdSizeIsCheckedBeforeRegistering(File.ReadAllText(cpp)),
            "the eight-byte check still refuses before registering");
        Assert.True(
            RegistrationFlowSource.AnUndiscoveredConsoleBecomesAManualHost(File.ReadAllText(cpp)),
            "an undiscovered console is still written down as a manual host");
        Assert.True(
            RegistrationFlowSource.TheBroadcastAddressIsTheFlag(File.ReadAllText(dialog)),
            "the broadcast address is still the flag");
    }

    /// <summary>
    /// The four radio buttons carry the four targets, in the order the enum names them - read out
    /// of the dialog rather than trusted, because it is the only place the label and the number
    /// sit next to each other.
    /// </summary>
    [Fact]
    public void TheDialogStillOffersTheseFourTargets()
    {
        string? dialog = RegistDialogSource.Locate();
        if (dialog is null)
            return;

        IReadOnlyList<int> targets =
            RegistrationFlowSource.TargetsInDialogOrder(File.ReadAllText(dialog));

        Assert.Equal(new[] { 800, 900, 1000, 1000100 }, targets);
    }
}
