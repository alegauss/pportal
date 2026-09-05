namespace ChiakiNg.Session;

/// <summary>One number a criterion states, and the program value it was copied from.</summary>
/// <param name="About">The task whose criterion it is.</param>
/// <param name="Lead">That criterion's lead, which is how it is addressed.</param>
/// <param name="Phrase">The words carrying the number, kept short so the sentence can be rewritten.</param>
/// <param name="Stated">The number those words say.</param>
/// <param name="Actual">What the program says now.</param>
/// <param name="Why">Which value it is, because a row with no reason is a table.</param>
public readonly record struct CriterionCount(
    string About, string Lead, string Phrase, int Stated, Func<int> Actual, string Why);

/// <summary>
/// PP728: the numbers a criterion states about a program, held against that program.
///
/// One criterion of the run's host said "Seven have none, and they are four subsystems" and named
/// all four. Four tasks then wrote those subsystems over four commits, each shortening the census
/// the sentence was copied from, and the sentence did not move. Every one of those commits was
/// green, and after the last one it stated seven where the answer was zero.
///
/// THE SHAPE IS PP690'S, ONE FIELD OVER. That check holds a criterion's BLOCKER claim against the
/// ledger, because a sentence naming a finished task understates the work to zero. This is the same
/// sentence's COUNT, and the same argument: a person deciding what is left reads it.
///
/// THE JOIN IS TWO-SIDED, and both halves are needed. The stated number has to equal the program's,
/// or the criterion is stale; and the PHRASE has to spell that same number in words, or somebody
/// can fix the row and leave the document saying thirteen where the census holds fourteen.
///
/// WHAT THIS DOES NOT DO is find a criterion that gains a number nobody adds a row for. A sweep was
/// measured and not built: the Done-when sections carry fifteen number phrases and most are prose -
/// "one edit", "one trace", "one of them" - so demanding a row for each would file noise against
/// sentences that count nothing. The rows are therefore declared, and the honest limit is that a
/// NEW count is somebody's judgement rather than the gate's.
/// </summary>
public static class CriterionCounts
{
    /// <summary>Where the criteria are.</summary>
    public const string RelativePath = @"docs\ROADMAP.md";

    /// <summary>The roadmap, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Every criterion number this port can check, with what answers it.
    ///
    /// PP295's third is the live one: three counts copied out of PP669's census in one sentence,
    /// and a row added to any of the three groups would make it false with nothing to say so.
    /// </summary>
    public static IReadOnlyList<CriterionCount> All { get; } =
    [
        new(
            "PP295",
            "Every consumer PP638's linker run named has a counterpart",
            "session.c's five",
            5,
            () => FramePathConsumers.Session.Count,
            "The calls session.c makes into the frame path."),
        new(
            "PP295",
            "Every consumer PP638's linker run named has a counterpart",
            "the shim's thirteen",
            13,
            () => FramePathConsumers.Shim.Count,
            "The wrappers the shim exports so a differential could run."),
        new(
            "PP295",
            "Every consumer PP638's linker run named has a counterpart",
            "the suite's four",
            4,
            () => FramePathConsumers.Suite.Count,
            "The C test files the suite links through the frame path."),
    ];

    /// <summary>
    /// The number in words, as a criterion spells it.
    ///
    /// Bounded at twenty because that is what these sentences use, and a number past it should be
    /// a refusal rather than a silent digit: a criterion saying "21" where the prose everywhere
    /// else says a word is a sentence somebody has to look at.
    /// </summary>
    public static string InWords(int value) => value switch
    {
        0 => "zero",
        1 => "one",
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        6 => "six",
        7 => "seven",
        8 => "eight",
        9 => "nine",
        10 => "ten",
        11 => "eleven",
        12 => "twelve",
        13 => "thirteen",
        14 => "fourteen",
        15 => "fifteen",
        16 => "sixteen",
        17 => "seventeen",
        18 => "eighteen",
        19 => "nineteen",
        20 => "twenty",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "a criterion count past twenty needs a word this list does not have"),
    };

    /// <summary>Whether a row's phrase really spells the number the row states.</summary>
    public static bool ThePhraseSpellsTheNumber(CriterionCount row)
        => row.Phrase.Contains(InWords(row.Stated), StringComparison.Ordinal);
}
