using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: a settings field that writes on commit, which is PP37's third example.
/// </summary>
public class NumericSettingFieldTests
{
    /// <summary>
    /// Typing does not write. The whole example: per keystroke, "1920" stores 1, then 19, then
    /// 192, and every one of those is a settings write some other binding may act on.
    /// </summary>
    [Fact]
    public void TypingDoesNotWriteTheSetting()
    {
        var field = new NumericSettingField();

        field.Type("1");
        field.Type("19");
        field.Type("192");
        field.Type("1920");

        Assert.Equal(0, field.Value);

        field.Commit();
        Assert.Equal(1920, field.Value);
    }

    /// <summary>
    /// An invalid entry commits ZERO and clears the box. Refusing it instead leaves the old value
    /// in the setting and the bad text on screen - a field that looks edited and is not.
    /// </summary>
    [Fact]
    public void AnInvalidEntryCommitsZeroAndClearsTheBox()
    {
        var field = new NumericSettingField();
        field.Type("1920");
        field.Commit();
        Assert.Equal(1920, field.Value);

        field.Type("nonsense");
        field.Commit();

        Assert.Equal(0, field.Value);
        Assert.Equal("", field.Text);
    }

    /// <summary>Clearing the box lands in the same place, because zero is what unset means here.</summary>
    [Fact]
    public void ClearingTheBoxCommitsZero()
    {
        var field = new NumericSettingField();
        field.Type("800");
        field.Commit();

        field.Type("");
        field.Commit();

        Assert.Equal(0, field.Value);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("9999", 9999)]
    [InlineData("10000", 0)]      // above the bound: invalid, so zero
    [InlineData("-1", 0)]         // parses, out of range, so zero
    public void TheRangeIsZeroToNineThousandNineHundredAndNinetyNine(string text, int expected)
    {
        var field = new NumericSettingField();
        field.Type(text);
        field.Commit();

        Assert.Equal(expected, field.Value);
    }

    /// <summary>
    /// The parse is JavaScript's, and that is not pedantry. parseInt("1920px") is 1920, so the Qt
    /// client commits 1920 for that text; int.TryParse refuses it and a port using that would
    /// clear the field instead - the same keystrokes, a different setting, and nothing saying so.
    /// </summary>
    [Theory]
    [InlineData("1920px", 1920)]
    [InlineData("1080 ", 1080)]
    [InlineData(" 720", 720)]
    [InlineData("12abc", 12)]
    public void TheParseIsLenientLikeJavascripts(string text, int expected)
    {
        var field = new NumericSettingField();
        field.Type(text);
        field.Commit();

        Assert.Equal(expected, field.Value);
    }

    /// <summary>And a string with no leading digits is not a number at all.</summary>
    [Theory]
    [InlineData("px1920")]
    [InlineData("abc")]
    [InlineData("")]
    public void TextWithoutLeadingDigitsIsNotANumber(string text)
        => Assert.Null(NumericSettingField.ParseInt(text));

    /// <summary>
    /// The error tint shows only while the text is non-empty. An empty box is not an error, it is
    /// a box nobody has typed in yet - and tinting it red on open would be a screen that looks
    /// broken at rest.
    /// </summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("1920", false)]
    [InlineData("99999", true)]
    [InlineData("abc", true)]
    public void TheErrorTintNeedsTextAsWellAsInvalidity(string text, bool shows)
    {
        var field = new NumericSettingField();
        field.Type(text);

        Assert.Equal(shows, field.ShowsError);
    }

    /// <summary>
    /// The whole screen writes on commit and nowhere on change - which is the claim a port has to
    /// keep, since binding a property straight to a text box writes per keystroke and nothing
    /// looks different.
    /// </summary>
    [Fact]
    public void TheQtScreenWritesOnCommitAndNeverOnChange()
    {
        string? file = SettingsFieldSource.Locate();
        if (file is null)
            return;

        string qml = File.ReadAllText(file);
        (int onFinished, int onChanged) = SettingsFieldSource.CommitPoints(qml);

        Assert.True(onFinished > 0, "no commit-on-finish handlers found");
        Assert.Equal(0, onChanged);

        Assert.True(SettingsFieldSource.InvalidCommitsZeroAndClears(qml), "zero and clear");
        Assert.Equal(NumericSettingField.Max, SettingsFieldSource.UpperBound(qml));
    }
}
