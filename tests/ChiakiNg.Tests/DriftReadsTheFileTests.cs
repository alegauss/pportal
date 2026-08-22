using System.Reflection;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP272: whether a drift check reads the file it was handed.
///
/// A check extracts the stretch it cares about and asks whether the text is still there. When the
/// anchor bounding that stretch moves, the extraction answers with NOTHING - and a check written as
/// an absence is then true, because nothing contains nothing. The positive ones fail loudly in that
/// case, which is why the shape stays invisible: most go red and a few quietly go green.
///
/// The property asked here is weaker than "this check is correct" and much stronger than what
/// existed before, which was nothing: a check that reads a file must answer FALSE when handed an
/// empty one. Anything answering true for empty input has an answer that does not depend on what it
/// was given.
///
/// Reflected rather than listed, for the reason <see cref="DriftCorpusTests"/> and
/// <see cref="LocatorCorpusTests"/> both give.
/// </summary>
public class DriftReadsTheFileTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every predicate on a Source class that takes text and answers yes or no.
    /// </summary>
    public static IReadOnlyList<MethodInfo> Predicates()
    {
        var found = new List<MethodInfo>();

        foreach (Type type in typeof(SanitizerSource).Assembly.GetTypes())
        {
            if (!type.Name.EndsWith("Source", StringComparison.Ordinal))
                continue;

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.ReturnType != typeof(bool))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters.Any(p => p.ParameterType != typeof(string)))
                    continue;

                found.Add(method);
            }
        }

        return found;
    }

    /// <summary>
    /// THE PROPERTY. Handed nothing, every one of them says no.
    /// </summary>
    [Fact]
    public void EveryDriftCheckAnswersNoToAnEmptyFile()
    {
        IReadOnlyList<MethodInfo> predicates = Predicates();

        // A sweep that finds nothing passes for the wrong reason - PP271's lesson, applied to this
        // sweep rather than to what it sweeps.
        Assert.True(predicates.Count > 40, $"only {predicates.Count} drift predicates were found");

        var wrong = new List<string>();

        foreach (MethodInfo predicate in predicates)
        {
            object?[] nothing = [.. predicate.GetParameters().Select(object? (_) => "")];

            bool answered;
            try
            {
                answered = (bool)predicate.Invoke(null, nothing)!;
            }
            catch (TargetInvocationException ex)
            {
                // Throwing on an empty file is an honest answer: it cannot be mistaken for yes.
                output.WriteLine(
                    $"{predicate.DeclaringType!.Name}.{predicate.Name} threw "
                    + $"{ex.InnerException?.GetType().Name}");
                continue;
            }

            if (answered)
                wrong.Add($"{predicate.DeclaringType!.Name}.{predicate.Name}");
        }

        output.WriteLine($"{predicates.Count} drift predicates, {wrong.Count} answering yes to nothing");

        Assert.True(
            wrong.Count == 0,
            "these say yes about a file with nothing in it, so their answer does not depend on it:\n  "
            + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// And the shape that makes it necessary, shown rather than described: an absence is true of
    /// nothing.
    /// </summary>
    [Fact]
    public void NothingContainsNothing()
    {
        const string extracted = "";

        Assert.False(extracted.Contains("anything at all", StringComparison.Ordinal));

        // Which is what a check written as an absence would report as a pass.
        Assert.True(!extracted.Contains("anything at all", StringComparison.Ordinal));
    }
}
