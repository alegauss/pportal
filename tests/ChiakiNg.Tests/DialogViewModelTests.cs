using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP14: the three dialogs PP37 did not do, and the rules that make them three rather than one.
/// </summary>
public class DialogViewModelTests
{
    /// <summary>
    /// "Nothing chosen" is an INDEX OF -1, not a null. A port that modelled the absence as a null
    /// reference would enable the button on a fresh dialog, where the combo has a selection and
    /// that selection means nothing.
    /// </summary>
    [Fact]
    public void AManualHostNeedsAnAddressAndAConsole()
    {
        var model = new ManualHostViewModel();
        Assert.False(model.CanAdd);

        model.Host = "10.0.0.5";
        Assert.False(model.CanAdd);          // still no console

        model.RegisteredIndex = 0;
        Assert.True(model.CanAdd);

        model.RegisteredIndex = ManualHostViewModel.NoConsole;
        Assert.False(model.CanAdd);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(" 10.0.0.5 ", true)]
    public void TheManualHostIsTrimmed(string host, bool can)
    {
        var model = new ManualHostViewModel { RegisteredIndex = 0, Host = host };
        Assert.Equal(can, model.CanAdd);
    }

    /// <summary>
    /// The finding: the console PIN is REQUIRED here and OPTIONAL in registration. The same value,
    /// two rules - so a port that shared one validator between the dialogs breaks whichever it
    /// did not write it for, and the symptom is a button that says nothing about which applied.
    /// </summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("123", false)]
    [InlineData("1234", true)]
    [InlineData("12345", false)]
    [InlineData("abcd", false)]
    public void TheConsolePinDialogRequiresFourDigits(string pin, bool can)
    {
        var model = new ConsolePinViewModel { Pin = pin };
        Assert.Equal(can, model.CanSubmit);
    }

    /// <summary>And the same empty value is accepted by the registration dialog, which is the point.</summary>
    [Fact]
    public void TheSamePinIsOptionalDuringRegistration()
    {
        Assert.False(new ConsolePinViewModel { Pin = "" }.CanSubmit);

        var regist = new RegistViewModel { Host = "10.0.0.5", RemotePlayPin = "12345678", ConsolePin = "" };
        Assert.True(regist.CanRegister);
    }

    /// <summary>Deleting needs only the box: the dialog has no second thing to ask for.</summary>
    [Fact]
    public void DeletingAProfileNeedsOnlyTheBox()
    {
        var model = new ProfileViewModel { Mode = ProfileDialogMode.Delete };
        Assert.False(model.CanAccept);

        model.DeleteChecked = true;
        Assert.True(model.CanAccept);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(" work ", true)]
    public void CreatingAProfileNeedsATrimmedName(string name, bool can)
    {
        var model = new ProfileViewModel { Mode = ProfileDialogMode.Create, NewName = name };
        Assert.Equal(can, model.CanAccept);
    }

    /// <summary>Switching to the profile already in use is refused.</summary>
    [Fact]
    public void SwitchingToTheCurrentProfileIsRefused()
    {
        var model = new ProfileViewModel { Selected = "work", Current = "work" };
        Assert.False(model.CanAccept);

        model.Selected = "home";
        Assert.True(model.CanAccept);
    }

    /// <summary>
    /// And the subtle half of that: the combo shows "default" for the profile stored as the EMPTY
    /// STRING, so selecting "default" while the current profile is "" is the same no-op wearing
    /// the other spelling. The two strings are not equal, which is what makes it easy to miss.
    /// </summary>
    [Fact]
    public void SelectingDefaultWhileAlreadyOnItIsRefused()
    {
        var model = new ProfileViewModel
        {
            Selected = ProfileViewModel.DefaultProfile,
            Current = "",
        };

        Assert.False(model.CanAccept);

        // But selecting it from a named profile is a real switch.
        model.Current = "work";
        Assert.True(model.CanAccept);
    }

    /// <summary>All three rules are still the QML's own.</summary>
    [Fact]
    public void TheRulesAreStillTheQmlsOwn()
    {
        if (DialogSource.Locate("ManualHostDialog") is null)
            return;

        Assert.True(DialogSource.ManualHostUsesMinusOne(
            File.ReadAllText(DialogSource.Locate("ManualHostDialog")!)), "index -1");

        Assert.Equal("[0-9]{4}", DialogSource.Validator(
            File.ReadAllText(DialogSource.Locate("ConsolePinDialog")!)));

        Assert.True(DialogSource.ProfileRefusesDefaultAgainstEmpty(
            File.ReadAllText(DialogSource.Locate("ProfileDialog")!)), "default against empty");
    }

    /// <summary>
    /// And the two console-PIN validators really are different in the source, which is the whole
    /// basis for keeping them apart in the port.
    /// </summary>
    [Fact]
    public void TheTwoConsolePinValidatorsDifferInTheSource()
    {
        if (DialogSource.Locate("ConsolePinDialog") is null || RegistDialogSource.Locate() is null)
            return;

        string standalone = DialogSource.Validator(
            File.ReadAllText(DialogSource.Locate("ConsolePinDialog")!))!;
        IReadOnlyList<string> registValidators =
            RegistDialogSource.Validators(File.ReadAllText(RegistDialogSource.Locate()!));

        Assert.Equal("[0-9]{4}", standalone);
        Assert.Equal("^$|[0-9]{4}", registValidators[1]);
        Assert.NotEqual(standalone, registValidators[1]);
    }
}
