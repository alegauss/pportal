using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP343: the one reader for a C function's body, and the trap it exists to avoid.
///
/// Two copies of this lived in the tree - one behind a class about the reorder queue, one private
/// to the message tap - and a third was written before either was found. Both times the trap was
/// caught by a failing test rather than by review, which is the argument for the reader having a
/// name that says what it reads.
/// </summary>
public class CFunctionTests
{
    /// <summary>
    /// THE PROTOTYPE IS SKIPPED, which is the whole point.
    ///
    /// Every static handler in this tree's C is forward-declared at the top of its file. A reader
    /// that took the first occurrence of the name would bound a body starting at a semicolon and
    /// ending in whatever function came next - and would compare two positions in neither.
    /// </summary>
    [Fact]
    public void AForwardDeclarationIsNotTheDefinition()
    {
        const string source = """
            static void handler(ChiakiCtrl *ctrl, uint8_t *payload);
            static void other(ChiakiCtrl *ctrl);

            static void other(ChiakiCtrl *ctrl)
            {
            	the_wrong_body();
            }

            static void handler(ChiakiCtrl *ctrl, uint8_t *payload)
            {
            	the_right_body();
            }
            """;

        string? body = CFunction.Body(source, "handler");

        Assert.NotNull(body);
        Assert.Contains("the_right_body", body, StringComparison.Ordinal);
        Assert.DoesNotContain("the_wrong_body", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// BRACES ARE COUNTED, not matched to the first one in column zero.
    ///
    /// This is what the crude reader got wrong the moment a function contained a brace at the start
    /// of a line - a thing no author would think to check, and which would silently shorten the
    /// body so a statement past it read as absent.
    /// </summary>
    [Fact]
    public void ABraceAtTheStartOfALineDoesNotEndTheBody()
    {
        const string source = """
            static void handler(void)
            {
            	if(x)
            {
            		nested();
            }
            	the_tail();
            }
            """;

        string? body = CFunction.Body(source, "handler");

        Assert.NotNull(body);
        Assert.Contains("nested()", body, StringComparison.Ordinal);
        Assert.Contains("the_tail()", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The signature is not in what comes back, so a caller reading for a statement cannot match
    /// the declaration it came from.
    /// </summary>
    [Fact]
    public void TheSignatureIsNotPartOfTheBody()
    {
        const string source = """
            static void ctrl_message_send(uint16_t type)
            {
            	encrypt();
            }
            """;

        string? body = CFunction.Body(source, "ctrl_message_send");

        Assert.NotNull(body);
        Assert.DoesNotContain("ctrl_message_send", body, StringComparison.Ordinal);
        Assert.Contains("encrypt()", body, StringComparison.Ordinal);
    }

    /// <summary>A name that only ever appears as a call has no body here.</summary>
    [Fact]
    public void ANameThatIsOnlyCalledHasNoBody()
    {
        const string source = """
            static void caller(void)
            {
            	somewhere_else(1, 2);
            }
            """;

        Assert.Null(CFunction.Body(source, "somewhere_else"));
    }

    /// <summary>And a name the file does not have at all is null rather than an exception.</summary>
    [Fact]
    public void AnAbsentFunctionIsNull()
    {
        Assert.Null(CFunction.Body("static void a(void)\n{\n}\n", "b"));
    }

    /// <summary>A full signature narrows it, which is how two similar names are told apart.</summary>
    [Fact]
    public void AFullSignatureNarrowsTheMatch()
    {
        const string source = """
            static void send(uint8_t a)
            {
            	one();
            }

            static void send_more(uint8_t a)
            {
            	two();
            }
            """;

        string? body = CFunction.Body(source, "static void send_more(");

        Assert.NotNull(body);
        Assert.Contains("two()", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// And it reads the real tree: a handler ctrl.c both declares and defines.
    ///
    /// The file-reading overload is the one the drift checks call, so it is the one worth exercising
    /// against a file that actually has the shape this is about.
    /// </summary>
    [Fact]
    public void ItReadsARealHandlerOutOfCtrl()
    {
        string? path = SanitizerSource.LocateRelative(@"lib\src\ctrl.c");
        if (path is null)
            return;

        string? body = CFunction.BodyIn(path, "ctrl_message_received_heartbeat_req");

        Assert.NotNull(body);
        Assert.Contains("HEARTBEAT_REP", body, StringComparison.Ordinal);

        // The definition and not the prototype: a prototype's "body" would carry neither of these.
        Assert.DoesNotContain("static void ctrl_message_received_login", body, StringComparison.Ordinal);
    }

    /// <summary>A file that is not there is null, which is what a published build gets.</summary>
    [Fact]
    public void AnAbsentFileIsNull()
    {
        Assert.Null(CFunction.BodyIn(Path.Combine(Path.GetTempPath(), "no-such-file.c"), "anything"));
    }
}
