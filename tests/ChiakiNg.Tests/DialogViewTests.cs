using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP14: the four dialogs as markup - that they load, and that every binding on them resolves.
///
/// The rules are already asserted without a window, in PP37's view model and PP14's three. What
/// these add is the half a view model cannot answer: whether the screen is wired to it. A binding
/// path that does not exist is not a build error - it is a button that never enables and a field
/// that never fills, and the only way to find one is to let WPF resolve it.
///
/// So each dialog is checked the same way: put a view model behind it, change the thing the rule
/// is about, and read the control the rule shows up on.
/// </summary>
public class DialogViewTests
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
        element.Measure(new Size(800, 600));
        element.Arrange(new Rect(0, 0, 800, 600));
        element.UpdateLayout();
    }

    [Fact]
    public void AllFourLoad() => OnSta(() =>
    {
        Assert.NotNull(new RegistView());
        Assert.NotNull(new ManualHostView());
        Assert.NotNull(new ConsolePinView());
        Assert.NotNull(new ProfileView());
    });

    /// <summary>
    /// Registration: the fields reach the view model, and the button follows CanRegister rather
    /// than any expression in the markup.
    /// </summary>
    [Fact]
    public void RegistrationsButtonFollowsTheViewModel() => OnSta(() =>
    {
        var model = new RegistViewModel();
        var view = new RegistView { DataContext = model };
        Realise(view);

        var button = (Button)view.FindName("RegisterButton");
        Assert.False(button.IsEnabled);

        ((TextBox)view.FindName("HostField")).Text = "10.0.0.5";
        ((TextBox)view.FindName("RemotePlayPinField")).Text = "12345678";
        Realise(view);

        Assert.Equal("10.0.0.5", model.Host);
        Assert.True(button.IsEnabled);
    });

    /// <summary>
    /// The two identifier fields are shown by the view model, and their visibility is part of the
    /// rule: showing one the user has not filled must disable the button, and hiding it again must
    /// enable it. Both of those are repaints, so a silent property here is a stuck button.
    /// </summary>
    [Fact]
    public void ShowingAnIdentifierFieldDisablesTheButtonAndHidingItEnablesIt() => OnSta(() =>
    {
        var model = new RegistViewModel { Host = "10.0.0.5", RemotePlayPin = "12345678" };
        var view = new RegistView { DataContext = model };
        Realise(view);

        var group = (FrameworkElement)view.FindName("AccountIdGroup");
        var button = (Button)view.FindName("RegisterButton");

        Assert.Equal(Visibility.Collapsed, group.Visibility);
        Assert.True(button.IsEnabled);

        model.AccountIdVisible = true;
        Realise(view);

        Assert.Equal(Visibility.Visible, group.Visibility);
        Assert.False(button.IsEnabled);

        model.AccountIdVisible = false;
        Realise(view);

        Assert.Equal(Visibility.Collapsed, group.Visibility);
        Assert.True(button.IsEnabled);
    });

    /// <summary>
    /// The manual host dialog opens with its combo showing something and nothing chosen, and the
    /// button has to be off. That only works because the INDEX is what is bound - an item binding
    /// would read the first row as a choice.
    /// </summary>
    [Fact]
    public void TheManualHostComboStartsAtNothingChosen() => OnSta(() =>
    {
        var model = new ManualHostViewModel();
        var view = new ManualHostView { DataContext = model };
        Realise(view);

        var combo = (ComboBox)view.FindName("ConsoleCombo");
        combo.ItemsSource = new[] { "PS5-385", "PS4-112" };
        Realise(view);

        var button = (Button)view.FindName("AddButton");

        Assert.Equal(ManualHostViewModel.NoConsole, combo.SelectedIndex);
        Assert.False(button.IsEnabled);

        ((TextBox)view.FindName("HostField")).Text = "10.0.0.5";
        Realise(view);
        Assert.False(button.IsEnabled);

        combo.SelectedIndex = 1;
        Realise(view);

        Assert.Equal(1, model.RegisteredIndex);
        Assert.True(button.IsEnabled);
    });

    /// <summary>
    /// The console PIN dialog requires four digits where registration accepts none, and the markup
    /// states neither rule - it asks the view model, which is the point of there being two.
    /// </summary>
    [Fact]
    public void TheConsolePinButtonNeedsFourDigits() => OnSta(() =>
    {
        var model = new ConsolePinViewModel();
        var view = new ConsolePinView { DataContext = model };
        Realise(view);

        var field = (TextBox)view.FindName("PinField");
        var button = (Button)view.FindName("SubmitButton");

        Assert.False(button.IsEnabled);

        field.Text = "123";
        Realise(view);
        Assert.False(button.IsEnabled);

        field.Text = "1234";
        Realise(view);
        Assert.Equal("1234", model.Pin);
        Assert.True(button.IsEnabled);
    });

    /// <summary>
    /// The profile dialog's mode decides what is on screen, and the three modes show three
    /// different things - which is why one dialog rather than three is worth the triggers.
    /// </summary>
    [Fact]
    public void TheProfileDialogsModeDecidesWhatIsShown() => OnSta(() =>
    {
        var model = new ProfileViewModel();
        var view = new ProfileView { DataContext = model };
        Realise(view);

        var name = (FrameworkElement)view.FindName("NameGroup");
        var delete = (FrameworkElement)view.FindName("DeleteBox");

        Assert.Equal(Visibility.Collapsed, name.Visibility);
        Assert.Equal(Visibility.Collapsed, delete.Visibility);

        model.Mode = ProfileDialogMode.Create;
        Realise(view);
        Assert.Equal(Visibility.Visible, name.Visibility);
        Assert.Equal(Visibility.Collapsed, delete.Visibility);

        model.Mode = ProfileDialogMode.Delete;
        Realise(view);
        Assert.Equal(Visibility.Collapsed, name.Visibility);
        Assert.Equal(Visibility.Visible, delete.Visibility);
    });

    /// <summary>
    /// And the profile button follows the mode's rule through the view: ticking the delete box is
    /// the whole of the delete condition, and typing a name is the whole of the create one.
    /// </summary>
    [Fact]
    public void TheProfileButtonFollowsWhicheverRuleTheModeSelects() => OnSta(() =>
    {
        var model = new ProfileViewModel { Mode = ProfileDialogMode.Delete };
        var view = new ProfileView { DataContext = model };
        Realise(view);

        var button = (Button)view.FindName("AcceptButton");
        var box = (CheckBox)view.FindName("DeleteBox");

        Assert.False(button.IsEnabled);

        box.IsChecked = true;
        Realise(view);
        Assert.True(model.DeleteChecked);
        Assert.True(button.IsEnabled);

        // The same tick counts for nothing once the dialog is creating rather than deleting.
        model.Mode = ProfileDialogMode.Create;
        Realise(view);
        Assert.False(button.IsEnabled);

        ((TextBox)view.FindName("NameField")).Text = "couch";
        Realise(view);
        Assert.True(button.IsEnabled);
    });
}
