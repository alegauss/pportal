using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP19: the two prompts as markup. Small screens, so the only thing worth asserting is that they
/// load, that the message reaches them, and that the three buttons really are three - a port that
/// drew two would lose the deferral without any rule looking wrong.
/// </summary>
public class PromptDialogViewTests
{
    private static void OnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void Realise(FrameworkElement element)
    {
        element.Measure(new Size(600, 400));
        element.Arrange(new Rect(0, 0, 600, 400));
        element.UpdateLayout();
    }

    [Fact]
    public void BothLoad() => OnSta(() =>
    {
        Assert.NotNull(new ConfirmDialogView());
        Assert.NotNull(new RemindDialogView());
    });

    /// <summary>Two buttons on the confirm prompt, three on the remind one.</summary>
    [Fact]
    public void TheRemindPromptHasAThirdButton() => OnSta(() =>
    {
        var confirm = new ConfirmDialogView { DataContext = new ConfirmDialogViewModel() };
        Realise(confirm);
        Assert.NotNull(confirm.FindName("YesButton"));
        Assert.NotNull(confirm.FindName("NoButton"));

        var remind = new RemindDialogView { DataContext = new RemindDialogViewModel() };
        Realise(remind);
        Assert.NotNull(remind.FindName("YesButton"));
        Assert.NotNull(remind.FindName("NoButton"));
        Assert.NotNull(remind.FindName("LaterButton"));
    });

    /// <summary>The message reaches the screen, and answering takes every button out.</summary>
    [Fact]
    public void AnsweringDisablesAllThree() => OnSta(() =>
    {
        var model = new RemindDialogViewModel { Text = "Would you like to connect to PSN?" };
        var view = new RemindDialogView { DataContext = model };
        Realise(view);

        Assert.Equal("Would you like to connect to PSN?", ((TextBlock)view.FindName("MessageLabel")).Text);

        var yes = (Button)view.FindName("YesButton");
        var no = (Button)view.FindName("NoButton");
        var later = (Button)view.FindName("LaterButton");

        Assert.True(yes.IsEnabled);
        Assert.True(no.IsEnabled);
        Assert.True(later.IsEnabled);

        model.Later();
        Realise(view);

        Assert.False(yes.IsEnabled);
        Assert.False(no.IsEnabled);
        Assert.False(later.IsEnabled);
    });

    /// <summary>And the confirm prompt's buttons follow the same "not yet answered" rule.</summary>
    [Fact]
    public void TheConfirmPromptsButtonsFollowTheSameRule() => OnSta(() =>
    {
        var model = new ConfirmDialogViewModel { Text = "Are you sure?" };
        var view = new ConfirmDialogView { DataContext = model };
        Realise(view);

        var yes = (Button)view.FindName("YesButton");
        Assert.True(yes.IsEnabled);

        model.Accept();
        Realise(view);
        Assert.False(yes.IsEnabled);
    });
}
