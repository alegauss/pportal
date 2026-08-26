using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP352, under PP294: no ctrl handler reads a payload byte without looking at the size first.
/// </summary>
public class CtrlPayloadChecksTests
{
    /// <summary>
    /// THE CHECK, over every handler rather than the two that were wrong.
    ///
    /// A handler that indexes payload and never names payload_size is the defect, wherever it is
    /// written - which is what notices the twelfth handler added without one.
    /// </summary>
    [Fact]
    public void NoHandlerIndexesItsPayloadWithoutCheckingTheSize()
    {
        string? path = CtrlPayloadChecks.Locate();
        if (path is null)
            return;

        IReadOnlyList<string> unchecked_ =
            CtrlPayloadChecks.HandlersThatIndexWithoutChecking(File.ReadAllText(path));

        Assert.True(
            unchecked_.Count == 0,
            "these handlers index a payload they never sized:\n  " + string.Join("\n  ", unchecked_));
    }

    /// <summary>
    /// And the reader finds one where there is one, so the check above means something.
    ///
    /// This is DisplayA as it was: one indexed read, no mention of the size, and the parameter
    /// present in the signature and unused - which is the shape of a check meant to be there.
    /// </summary>
    [Fact]
    public void TheReaderFindsAnUncheckedHandler()
    {
        const string asItWas = """
            static void ctrl_message_received_displaya(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
            {
            	if(payload[0] == 0x1)
            	{
            		ctrl->cant_displaya = true;
            	}
            }
            """;

        Assert.Equal(
            ["ctrl_message_received_displaya"],
            CtrlPayloadChecks.HandlersThatIndexWithoutChecking(asItWas));
    }

    /// <summary>And ignores one that checks, however the check is written.</summary>
    [Theory]
    [InlineData("\tif(payload_size < 1)\n\t\treturn;\n\tif(payload[0] == 0x1) { }")]
    [InlineData("\tif(payload_size != 1)\n\t\treturn;\n\tuint8_t s = payload[0];")]
    public void TheReaderIgnoresAHandlerThatChecks(string body)
    {
        string source =
            "static void ctrl_message_received_thing(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)\n{\n"
            + body + "\n}\n";

        Assert.Empty(CtrlPayloadChecks.HandlersThatIndexWithoutChecking(source));
    }

    /// <summary>A handler that never indexes at all is not asked to check.</summary>
    [Fact]
    public void AHandlerThatNeverIndexesIsNotAsked()
    {
        const string source = """
            static void ctrl_message_received_heartbeat_req(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
            {
            	send_reply();
            }
            """;

        Assert.Empty(CtrlPayloadChecks.HandlersThatIndexWithoutChecking(source));
    }

    /// <summary>
    /// A forward declaration is not a handler, which is the trap PP343 gave one reader for.
    ///
    /// ctrl.c declares all eleven at the top of the file. A check that read a body from the first
    /// occurrence of a name would bound one belonging to something else.
    /// </summary>
    [Fact]
    public void AForwardDeclarationIsNotAHandler()
    {
        const string source = """
            static void ctrl_message_received_displaya(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);
            static void ctrl_message_received_displayb(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);

            static void ctrl_message_received_displaya(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
            {
            	if(payload_size < 1)
            		return;
            	if(payload[0] == 0x1) { }
            }

            static void ctrl_message_received_displayb(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
            {
            	if(payload_size < 2)
            		return;
            	if(payload[0] == 0x01 && payload[1] == 0xff) { }
            }
            """;

        Assert.Empty(CtrlPayloadChecks.HandlersThatIndexWithoutChecking(source));
    }
}
