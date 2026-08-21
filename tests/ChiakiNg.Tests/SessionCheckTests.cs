using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP233: the session check, and the two failures that are not treated alike.
///
/// The pair worth reading together is <see cref="ATokenerThatCannotBeAllocatedReportsSuccess"/> and
/// <see cref="AParseFailureIsReported"/>. They are neighbouring branches in the same function, both
/// mean "this response was never read", and only one of them says so.
/// </summary>
public class SessionCheckTests
{
    /// <summary>False is the create URL, which is where an ordinary check goes.</summary>
    [Fact]
    public void OneBoolChoosesBetweenTwoEndpoints()
    {
        Assert.Equal(PsnEndpoints.SessionCreateUrl, SessionCheck.UrlFor(viewUrl: false));
        Assert.Equal(PsnEndpoints.SessionViewUrl, SessionCheck.UrlFor(viewUrl: true));

        Assert.NotEqual(SessionCheck.UrlFor(false), SessionCheck.UrlFor(true));
    }

    /// <summary>The view URL is the create one with a view parameter, not a different path.</summary>
    [Fact]
    public void TheViewUrlIsTheCreateOneWithAParameter()
        => Assert.StartsWith(
            PsnEndpoints.SessionCreateUrl, PsnEndpoints.SessionViewUrl, StringComparison.Ordinal);

    /// <summary>A body that parsed is the whole of what this call checks.</summary>
    [Fact]
    public void AnAnswerThatParsedIsTheWholeCheck()
    {
        SessionCheckOutcome outcome =
            SessionCheck.Result(transferred: true, httpOk: true, tokener: true, parsed: true);

        Assert.Equal(SessionCheckOutcome.Ok, outcome);
        Assert.False(SessionCheck.IsFailure(outcome));
    }

    /// <summary>An error status is a failure, because CURLOPT_FAILONERROR makes it one.</summary>
    [Fact]
    public void AnErrorStatusIsAFailure()
    {
        SessionCheckOutcome outcome =
            SessionCheck.Result(transferred: false, httpOk: false, tokener: true, parsed: true);

        Assert.Equal(SessionCheckOutcome.HttpNotOk, outcome);
        Assert.True(SessionCheck.IsFailure(outcome));
    }

    /// <summary>And so is a transfer that did not complete.</summary>
    [Fact]
    public void ATransferThatFailedIsAFailure()
    {
        SessionCheckOutcome outcome =
            SessionCheck.Result(transferred: false, httpOk: true, tokener: true, parsed: true);

        Assert.Equal(SessionCheckOutcome.Network, outcome);
        Assert.True(SessionCheck.IsFailure(outcome));
    }

    /// <summary>A body that will not parse is reported. This branch sets an error before it goes.</summary>
    [Fact]
    public void AParseFailureIsReported()
    {
        SessionCheckOutcome outcome =
            SessionCheck.Result(transferred: true, httpOk: true, tokener: true, parsed: false);

        Assert.Equal(SessionCheckOutcome.Unreadable, outcome);
        Assert.True(SessionCheck.IsFailure(outcome));
    }

    /// <summary>
    /// And the one beside it does not. A tokener that could not be allocated jumps to a cleanup
    /// that returns the error variable, and nothing on that path ever sets it - so a check that
    /// read nothing at all is answered with success.
    /// </summary>
    [Fact]
    public void ATokenerThatCannotBeAllocatedReportsSuccess()
    {
        SessionCheckOutcome outcome =
            SessionCheck.Result(transferred: true, httpOk: true, tokener: false, parsed: false);

        Assert.Equal(SessionCheckOutcome.NoTokener, outcome);

        // The defect, as a value rather than as prose.
        Assert.False(SessionCheck.IsFailure(outcome));
    }

    /// <summary>
    /// The two together, which is what makes it an asymmetry rather than a convention: both mean
    /// the response was never read, and they are answered oppositely.
    /// </summary>
    [Fact]
    public void TwoWaysOfReadingNothingAreAnsweredOppositely()
    {
        SessionCheckOutcome noTokener =
            SessionCheck.Result(transferred: true, httpOk: true, tokener: false, parsed: false);
        SessionCheckOutcome noParse =
            SessionCheck.Result(transferred: true, httpOk: true, tokener: true, parsed: false);

        Assert.NotEqual(SessionCheck.IsFailure(noTokener), SessionCheck.IsFailure(noParse));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheCheckIsStillTheCores()
    {
        string? file = SessionCheckSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(SessionCheckSource.OneBoolStillChoosesTheUrl(core), "one bool, two URLs");
        Assert.True(SessionCheckSource.TheBodyIsStillOnlyLogged(core), "logged then released");
        Assert.True(
            SessionCheckSource.ANoTokenerStillReturnsSuccess(core),
            "and a tokener it cannot allocate still leaves without an error");
        Assert.True(
            SessionCheckSource.TheMessagesStillSayCreating(core),
            "while its messages still say Creating");
    }
}
