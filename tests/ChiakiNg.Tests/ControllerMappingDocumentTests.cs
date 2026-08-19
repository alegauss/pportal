using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP18: the mapping screen's document, which is the half of it that does not need a pad plugged in.
///
/// The screen is the live event stream and cannot be asserted without a device - that is its
/// design's own argument. Everything between the token a press produces and the string that reaches
/// settings is not, and this is that.
/// </summary>
public class ControllerMappingDocumentTests
{
    /// <summary>
    /// A mapping in the shape SDL emits: guid, name, then key:value pairs and the metadata it
    /// carries along. Deliberately NOT in key order, because the order is a rule of its own.
    /// </summary>
    private const string Xbox =
        "030000005e040000e002000000007200,Xbox Wireless Controller," +
        "a:b0,b:b1,x:b2,y:b3,back:b6,start:b7,leftshoulder:b4,rightshoulder:b5," +
        "leftstick:b8,rightstick:b9,guide:b10,dpup:h0.1,dpdown:h0.4,dpleft:h0.8," +
        "dpright:h0.2,leftx:a0,lefty:a1,rightx:a3,righty:a4,lefttrigger:a2," +
        "righttrigger:a5,platform:Windows,crc:7200,";

    private static ControllerMappingDocument Parsed() =>
        ControllerMappingDocument.Parse(Xbox, "Xbox Wireless Controller")!;

    [Fact]
    public void TheGuidAndTheNameComeOffTheFront()
    {
        ControllerMappingDocument document = Parsed();

        Assert.Equal("030000005e040000e002000000007200", document.Guid);
        Assert.Equal("Xbox Wireless Controller", document.ControllerType);
        Assert.False(document.Altered);
    }

    /// <summary>
    /// A star means "whatever the pad calls itself", and a pad that calls itself nothing gets a
    /// name rather than an empty row heading.
    /// </summary>
    [Theory]
    [InlineData("Mystery Pad", "Mystery Pad")]
    [InlineData("", "Unidentified Controller")]
    public void AStarNameIsResolvedAgainstThePad(string fallback, string expected)
    {
        ControllerMappingDocument document =
            ControllerMappingDocument.Parse("0300aa,*,a:b0,", fallback)!;

        Assert.Equal(expected, document.ControllerType);
    }

    /// <summary>A string with nothing but a guid is not a mapping - it is a truncated read.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("030000005e040000e002000000007200")]
    public void AStringWithoutANameIsRefused(string mapping)
        => Assert.Null(ControllerMappingDocument.Parse(mapping, "Pad"));

    /// <summary>
    /// Metadata stays in the map - it is written back out - but nothing may be bound to it. Without
    /// the exclusion, `platform` is a control called Windows and `crc` is one called 7200.
    /// </summary>
    [Fact]
    public void MetadataIsCarriedButIsNotAControl()
    {
        ControllerMappingDocument document = Parsed();

        Assert.Equal(["Windows"], document.Physical("platform"));
        Assert.Null(document.TargetOf("Windows"));
        Assert.Null(document.TargetOf("7200"));

        Assert.Equal("a", document.TargetOf("b0"));
    }

    /// <summary>
    /// A key that appears twice concatenates. SDL writes one for a control with two bindings, and
    /// overwriting would drop the first without a symptom until the pad half-works.
    /// </summary>
    [Fact]
    public void ARepeatedKeyConcatenates()
    {
        ControllerMappingDocument document =
            ControllerMappingDocument.Parse("0300aa,Pad,a:b0,b:b1,a:b11,", "Pad")!;

        Assert.Equal(["b0", "b11"], document.Physical("a"));
        Assert.Equal("a", document.TargetOf("b11"));
    }

    /// <summary>Twenty-three rows, always, in the order the QMap's integer key puts them.</summary>
    [Fact]
    public void EveryRowIsShownWhetherBoundOrNot()
    {
        IReadOnlyList<MappingRow> rows = Parsed().Rows();

        Assert.Equal(23, rows.Count);
        Assert.Equal("Cross", rows[0].Name);
        Assert.Equal("MIC", rows[^1].Name);
        List<int> values = [.. rows.Select(r => r.Value)];
        Assert.Equal(values.Order().ToList(), values);

        // The microphone is not on an Xbox pad, and its row is there anyway with nothing on it.
        Assert.Empty(rows[^1].Physical);
        Assert.Equal(["b0"], rows[0].Physical);
    }

    /// <summary>
    /// Source order and screen order differ, and the difference starts at the d-pad: the Qt source
    /// lists Cross, Moon, Box, Pyramid, then the d-pad, but Touchpad and PS sort AFTER Options and
    /// Share while the triggers sort after both - and the four stick axes come last of all.
    /// </summary>
    [Fact]
    public void TheStickAxesSortAfterEveryButton()
    {
        IReadOnlyList<MappingRow> rows = Parsed().Rows();
        var names = rows.Select(r => r.Name).ToList();

        Assert.True(names.IndexOf("Left Stick X") > names.IndexOf("R2"));
        Assert.True(names.IndexOf("R2") > names.IndexOf("PS"));
        Assert.True(names.IndexOf("PS") > names.IndexOf("Options"));
    }

    /// <summary>Binding a control where it already sits changes nothing at all.</summary>
    [Fact]
    public void RebindingAControlToItselfIsANoOp()
    {
        ControllerMappingDocument document = Parsed();

        document.Assign(1 << 0, "b0", 0);

        Assert.Equal(["b0"], document.Physical("a"));
        Assert.False(document.Altered);
    }

    /// <summary>
    /// A control moves rather than copies, and the row it leaves is REMOVED. That is why a mapping
    /// written after an edit can be shorter than the one it was read from.
    /// </summary>
    [Fact]
    public void BindingAControlElsewhereEmptiesAndRemovesTheRowItLeft()
    {
        ControllerMappingDocument document = Parsed();

        document.Assign(1 << 1, "b0", 0);

        Assert.Equal("b", document.TargetOf("b0"));
        Assert.Equal(["b0"], document.Physical("b"));
        Assert.Empty(document.Physical("a"));
        Assert.DoesNotContain("a:", document.Serialise());
        Assert.True(document.Altered);
    }

    /// <summary>
    /// Displacing the occupant of an index unbinds it rather than pushing it along - the pad's B
    /// button is bound to nothing afterwards, and its row holds only what replaced it.
    /// </summary>
    [Fact]
    public void BindingOntoAnOccupiedIndexDisplacesWhatWasThere()
    {
        ControllerMappingDocument document =
            ControllerMappingDocument.Parse("0300aa,Pad,a:b0,b:b1,", "Pad")!;

        document.Assign(1 << 1, "b0", 0);

        Assert.Equal(["b0"], document.Physical("b"));
        Assert.Equal("", document.TargetOf("b1"));
    }

    /// <summary>
    /// A displaced control can be bound again, and the empty target it carries is what makes that
    /// work: the move does not try to unbind it from a row it no longer sits on.
    /// </summary>
    [Fact]
    public void ADisplacedControlCanBeBoundAgain()
    {
        ControllerMappingDocument document =
            ControllerMappingDocument.Parse("0300aa,Pad,a:b0,b:b1,", "Pad")!;

        document.Assign(1 << 1, "b0", 0);
        document.Assign(1 << 0, "b1", 0);

        Assert.Equal(["b1"], document.Physical("a"));
        Assert.Equal(["b0"], document.Physical("b"));
    }

    /// <summary>
    /// The index is a request, not a promise. Index 0 prepends and every other index appends, so a
    /// binding aimed at index 2 of a one-entry row lands at index 1 and the row never has a hole.
    /// </summary>
    [Fact]
    public void IndexZeroPrependsAndEveryOtherIndexAppends()
    {
        ControllerMappingDocument document =
            ControllerMappingDocument.Parse("0300aa,Pad,a:b0,b:b1,x:b2,", "Pad")!;

        document.Assign(1 << 0, "b1", 5);
        Assert.Equal(["b0", "b1"], document.Physical("a"));

        // And index 0 is a displacement even on a row that has room, so b0 comes off rather than
        // sliding along - a row never grows at the front.
        document.Assign(1 << 0, "b2", 0);
        Assert.Equal(["b2", "b1"], document.Physical("a"));
        Assert.Equal("", document.TargetOf("b0"));
    }

    /// <summary>
    /// The row that shows it is one deep enough to tell an append from an insert: binding onto
    /// index 1 of a three-entry row displaces what was there and lands at the END, not back at the
    /// index it displaced. So the row a user sees after the edit is not the row they aimed at.
    /// </summary>
    [Fact]
    public void ADisplacedMiddleBindingIsReplacedAtTheEnd()
    {
        ControllerMappingDocument document =
            ControllerMappingDocument.Parse("0300aa,Pad,a:b0,a:b1,a:b2,b:b3,", "Pad")!;

        Assert.Equal(["b0", "b1", "b2"], document.Physical("a"));

        document.Assign(1 << 0, "b3", 1);

        Assert.Equal(["b0", "b2", "b3"], document.Physical("a"));
        Assert.Equal("", document.TargetOf("b1"));
    }

    /// <summary>
    /// Altered is a comparison against what was applied, not a flag that latches. Undo an edit by
    /// hand and the screen stops offering to save - which is the behaviour, and it is a good one.
    /// </summary>
    [Fact]
    public void AlteredIsAComparisonAndUndoingClearsIt()
    {
        ControllerMappingDocument document =
            ControllerMappingDocument.Parse("0300aa,Pad,a:b0,b:b1,", "Pad")!;

        document.Assign(1 << 1, "b0", 0);
        Assert.True(document.Altered);

        // Put it back: b0 to Cross, then b1 back onto Moon where it started.
        document.Assign(1 << 0, "b0", 0);
        document.Assign(1 << 1, "b1", 0);

        Assert.False(document.Altered);
    }

    /// <summary>
    /// And removing the emptied row rather than leaving it empty is what makes that work across a
    /// binding the user backs out of: try b0 on a row nothing was on, change your mind, and the
    /// map is the one that was applied. A row left behind holding nothing would compare unequal
    /// forever, and the screen would keep offering to save a mapping identical to the stored one.
    /// </summary>
    [Fact]
    public void ARowEmptiedByAMoveIsRemovedRatherThanLeftEmpty()
    {
        ControllerMappingDocument document =
            ControllerMappingDocument.Parse("0300aa,Pad,a:b0,b:b1,", "Pad")!;

        document.Assign(1 << 2, "b0", 0);
        Assert.True(document.Altered);

        document.Assign(1 << 0, "b0", 0);

        Assert.Empty(document.Physical("x"));
        Assert.False(document.Altered);
    }

    /// <summary>
    /// The rebuild is in key order and the string it parsed was not, so a round trip of an
    /// untouched mapping is a different string. Not a defect on its own - SDL accepts either - but
    /// it is half of the reason the next test says what it says.
    /// </summary>
    [Fact]
    public void TheRebuildIsInKeyOrderAndNotTheOrderItWasRead()
    {
        string written = Parsed().Serialise();

        Assert.StartsWith("Xbox Wireless Controller,a:b0,b:b1,back:b6,crc:7200,", written,
            StringComparison.Ordinal);
        Assert.EndsWith(",x:b2,y:b3", written, StringComparison.Ordinal);
    }

    /// <summary>
    /// The finding. controllerMappingApply decides "the user reset it by hand" by comparing the
    /// rebuilt string against the stored original - but the original begins with the GUID and the
    /// rebuild begins with the controller name, so the two can never be equal.
    ///
    /// The branch is unreachable. A user who undoes every change by hand gets a custom mapping
    /// written for their pad that is identical to the default, and the override stays in their
    /// settings until they press reset. Reproduced here rather than fixed, because a port that
    /// "fixed" it would delete an override the Qt client leaves in place.
    /// </summary>
    [Fact]
    public void TheResetToDefaultBranchCannotBeReached()
    {
        ControllerMappingDocument untouched = Parsed();

        Assert.False(untouched.Altered);
        Assert.False(untouched.LooksLikeTheOriginal(Xbox));

        // And the GUID is the whole of the difference for a mapping that was already key-ordered,
        // which is what identifies it as a missing field rather than a formatting drift.
        const string sorted = "0300aa,Pad,a:b0,b:b1,x:b2,";
        ControllerMappingDocument document = ControllerMappingDocument.Parse(sorted, "Pad")!;

        Assert.False(document.LooksLikeTheOriginal(sorted));
        Assert.True(document.LooksLikeTheOriginal("Pad,a:b0,b:b1,x:b2"));
    }

    /// <summary>
    /// And the shape it was read out of is still there. Every one of these is a sentence in
    /// qmlbackend.cpp, and the comparison is the one the finding above rests on.
    /// </summary>
    [Fact]
    public void TheRulesAreStillTheQtClients()
    {
        string? file = ControllerMappingSource.Locate();
        if (file is null)
            return;

        string cpp = File.ReadAllText(file);

        Assert.True(ControllerMappingSource.RebuildStartsWithTheControllerType(cpp),
            "the rebuilt string still starts with the controller name");
        Assert.True(ControllerMappingSource.TheOriginalIsStoredWithItsGuid(cpp),
            "the original is still stored whole, guid and all");
        Assert.True(ControllerMappingSource.ResetIsDecidedByComparingThem(cpp),
            "reset-to-default is still those two compared");
        Assert.True(ControllerMappingSource.IndexZeroPrependsAndTheRestAppend(cpp),
            "index 0 still prepends where the rest append");
        Assert.True(ControllerMappingSource.MetadataKeysAreNotControls(cpp),
            "crc, platform, type, hint and sdk* are still not controls");
        Assert.True(ControllerMappingSource.AnEmptiedRowIsRemoved(cpp),
            "a row emptied by a move is still removed");
    }
}
