using System.ComponentModel;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP37: a screen's logic asserted without opening a window.
///
/// This is the argument made concrete. Every test here is about a button being enabled, which is
/// the thing users notice and the thing a port gets subtly wrong - and not one of them needs a
/// visual tree, a dispatcher or an STA thread. A port that put this in code-behind would need all
/// three, and would therefore have none of these.
/// </summary>
public class RegistViewModelTests
{
    private static RegistViewModel Ready() => new()
    {
        Host = "192.168.1.10",
        RemotePlayPin = "12345678",
    };

    [Fact]
    public void AFilledFormCanRegister() => Assert.True(Ready().CanRegister);

    /// <summary>
    /// PP37's own example: the button must not enable one character early. Seven digits is what a
    /// port that validated "digits, up to eight" would accept, and the console refuses it after
    /// the client has said the input was fine.
    /// </summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    [InlineData("123456789", false)]
    [InlineData("1234567a", false)]
    [InlineData(" 12345678", false)]
    public void TheRemotePlayPinIsExactlyEightDigits(string pin, bool valid)
    {
        var model = Ready();
        model.RemotePlayPin = pin;

        Assert.Equal(valid, model.RemotePlayPinValid);
        Assert.Equal(valid, model.CanRegister);
    }

    /// <summary>
    /// The console PIN is empty OR four digits, and the empty half is the one a port loses.
    /// Validating four digits and nothing else refuses every console with no PIN set - which is
    /// most of them - by disabling a button, with nothing saying which field was at fault.
    /// </summary>
    [Theory]
    [InlineData("", true)]
    [InlineData("1234", true)]
    [InlineData("123", false)]
    [InlineData("12345", false)]
    [InlineData("abcd", false)]
    public void TheConsolePinIsEmptyOrExactlyFourDigits(string pin, bool valid)
    {
        var model = Ready();
        model.ConsolePin = pin;

        Assert.Equal(valid, model.ConsolePinValid);
        Assert.Equal(valid, model.CanRegister);
    }

    /// <summary>Whitespace is not a host, which is what the QML's trim() is there for.</summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(" 192.168.1.10 ", true)]
    public void TheHostIsTrimmedBeforeItCounts(string host, bool can)
    {
        var model = Ready();
        model.Host = host;

        Assert.Equal(can, model.CanRegister);
    }

    /// <summary>
    /// A hidden identifier does not block the button however empty it is - `!onlineId.visible ||
    /// onlineId.text.trim()`. A port that required both fields unconditionally would make a PS4
    /// before firmware 8 unregisterable, and the dialog would simply never enable.
    /// </summary>
    [Fact]
    public void AHiddenIdentifierDoesNotBlockTheButton()
    {
        var model = Ready();
        Assert.True(model.CanRegister);

        model.OnlineIdVisible = true;
        Assert.False(model.CanRegister);

        model.OnlineId = "someone";
        Assert.True(model.CanRegister);
    }

    [Fact]
    public void AVisibleIdentifierIsTrimmedToo()
    {
        var model = Ready();
        model.AccountIdVisible = true;
        model.AccountId = "   ";

        Assert.False(model.CanRegister);
    }

    /// <summary>
    /// Every field raises CanRegister, because the button depends on all of them. A view model
    /// that raised only its own property leaves a button that is correct and never repainted -
    /// which looks exactly like validation that is wrong.
    /// </summary>
    [Fact]
    public void EveryFieldRaisesTheButtonsProperty()
    {
        var model = Ready();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        model.Host = "other";
        model.RemotePlayPin = "87654321";
        model.ConsolePin = "1234";

        Assert.Equal(3, raised.Count(n => n == nameof(RegistViewModel.CanRegister)));
    }

    /// <summary>The two patterns are the QML's own, read out of it rather than remembered.</summary>
    [Fact]
    public void TheValidatorsAreTheQmlsOwn()
    {
        string? file = RegistDialogSource.Locate();
        if (file is null)
            return;

        string qml = File.ReadAllText(file);

        Assert.Equal<string[]>(["[0-9]{8}", "^$|[0-9]{4}"], [.. RegistDialogSource.Validators(qml)]);
        Assert.True(RegistDialogSource.ButtonRuleIsUnchanged(qml), "the button rule moved");
    }
}
