using System.Reflection;

namespace ChiakiNg.Session;

/// <summary>One reader whose subject is a file the frame path's deletion took out of the build.</summary>
/// <param name="Type">The class, by its name.</param>
/// <param name="Subjects">Which of the four it names.</param>
/// <param name="Predicates">How many questions it can still ask of that file's text.</param>
public readonly record struct FramePathReader(string Type, int Subjects, int Predicates);

/// <summary>
/// PP697, under PP295: the predicates outlive the C, and this is what says none was deleted with it.
///
/// PP623's third step is prose, and PP634 corrected what that step is. It had said the models drop
/// the first of their two states; the landing showed that wrong. THE PREDICATES ARE THE GUARD - each
/// is a different shape the C could come back in, and a wholesale-return check has a tripwire's
/// granularity rather than a guard's. So the work of turning prose is the tense around them, and the
/// failure to avoid is a reader who takes "the C has gone" as licence to delete the question.
///
/// WHICH IS EASY TO DO AND INVISIBLE. streamconnection.c, videoreceiver.c, frameprocessor.c and
/// fec.c are still in the tree - PP696 took them out of the BUILD, and left the source the way PP33
/// left holepunch.c and PP598 left gui/. Every predicate over them still runs, still reads the same
/// text, and still fails the day upstream's file says something else. Deleting one would cost
/// nothing today and would be discovered by whoever put the file back.
///
/// DERIVED AND NOT LISTED. The readers are found by asking the assembly which classes name one of
/// the four in a constant, which is how a reader declares its subject in this tree - so a class
/// added later is counted without anybody remembering to add it here. What is written down is one
/// number, and it may rise and may not fall: PP38's ratchet, pointed the other way, for the same
/// reason.
/// </summary>
public static class FramePathPredicates
{
    /// <summary>The four, as a reader's constant spells them.</summary>
    public static IReadOnlyList<string> Subjects { get; } =
    [
        @"lib\src\streamconnection.c",
        @"lib\src\videoreceiver.c",
        @"lib\src\frameprocessor.c",
        @"lib\src\fec.c",
    ];

    /// <summary>
    /// How many predicates the four's readers held when PP697 turned the prose.
    ///
    /// It may RISE - a new question about one of these files is a better guard - and it may not
    /// fall. A fall is the thing this exists to catch: a reader deciding a predicate is dead
    /// because the file it reads is no longer compiled, which is exactly the reasoning PP634
    /// corrected and exactly the day the port would stop noticing the file coming back.
    /// </summary>
    /// <remarks>
    /// 101 across 38 readers, measured on the tree PP696 left. The first number written here was a
    /// guess at 96 and the sweep answered 92, because it read only constants - and most of these
    /// subjects are declared in a census's collection rather than in a const. Both halves are why
    /// the number is taken from the sweep and not from a reading.
    /// </remarks>
    public const int Floor = 101;

    /// <summary>Every public class in the assembly that names one of the four in a constant.</summary>
    public static IReadOnlyList<FramePathReader> ReadersIn(Assembly app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var found = new List<FramePathReader>();

        foreach (Type one in app.GetTypes().Where(t => t.IsClass && t.IsPublic))
        {
            int subjects = SubjectsNamedBy(one);
            if (subjects == 0)
                continue;

            found.Add(new FramePathReader(one.Name, subjects, PredicatesOn(one)));
        }

        return [.. found.OrderBy(one => one.Type, StringComparer.Ordinal)];
    }

    /// <summary>
    /// How many of the four a class names in its own string constants.
    ///
    /// Constants and static readonly arrays both, because a reader with one subject writes a const
    /// and a reader with several writes a collection - and counting only the first would miss every
    /// census, which is where most of these questions live.
    /// </summary>
    public static int SubjectsNamedBy(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var named = new HashSet<string>(StringComparer.Ordinal);

        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(string))
                continue;

            if (field.GetRawConstantValue() is string value)
                Note(named, value);
        }

        // And the collections, which is how a census declares several subjects at once. Read rather
        // than skipped because that is where most of these questions live - a sweep that saw only
        // consts would report FecConsumers as being about nothing.
        foreach (PropertyInfo one in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (one.GetIndexParameters().Length > 0 || one.GetMethod is null)
                continue;

            object? value;
            try
            {
                value = one.GetValue(null);
            }
#pragma warning disable CA1031 // the sweep's whole job is to survive one unreadable member
            catch (Exception)
#pragma warning restore CA1031
            {
                // A property that cannot be read tells us nothing about its subjects, and a sweep
                // that threw here would take the whole census down over one of them. Measured
                // rather than assumed: reflection over this assembly finds properties the runtime
                // refuses outright - a ref return among them - and TargetInvocationException is
                // not what it throws for those.
                continue;
            }

            if (value is IEnumerable<string> strings)
            {
                foreach (string each in strings)
                    Note(named, each);
            }
        }

        return named.Count;
    }

    private static void Note(HashSet<string> named, string value)
    {
        foreach (string subject in Subjects)
        {
            if (value.Contains(subject, StringComparison.Ordinal))
                named.Add(subject);
        }
    }

    /// <summary>
    /// The questions a reader can ask: a public static method answering bool about some text.
    ///
    /// A string first parameter is what makes it a question about a FILE rather than about this
    /// side's own model - the C's text is read once and handed in, so a predicate takes it.
    /// </summary>
    public static int PredicatesOn(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Count(one =>
                one.ReturnType == typeof(bool)
                && one.GetParameters() is [{ ParameterType: var first }, ..]
                && first == typeof(string));
    }

    /// <summary>The total, which is the number the floor is about.</summary>
    public static int TotalIn(Assembly app) => ReadersIn(app).Sum(one => one.Predicates);
}
