using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChiakiNg.Session;

namespace ChiakiNg.Views;

/// <summary>
/// PP19: the dialog host.
///
/// The two key bindings are here rather than in the markup because they are not the same as the
/// buttons: Escape closes WITHOUT rejecting, where the back button rejects first, and the Menu key
/// presses the action button through its enabled state rather than around it.
/// </summary>
public partial class DialogHostView : UserControl
{
    public DialogHostView()
    {
        InitializeComponent();

        BackButton.Click += (_, _) => Model?.Back();
        ActionButton.Click += (_, _) => Model?.Accept();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// The screen this host is showing.
    ///
    /// PP316: named Screen and not Content. It used to be Content, which hides
    /// <see cref="ContentControl.Content"/> that this control inherits - and the two are different
    /// objects, because the base one is the whole markup tree in DialogHostView.xaml and this one
    /// is what sits inside the ContentHost within it. Which of them a caller reached depended on
    /// the static type of the reference holding the view, and nothing in the tree read either.
    /// </summary>
    public object? Screen
    {
        get => ContentHost.Content;
        set => ContentHost.Content = value;
    }

    /// <summary>
    /// The two key bindings, as a plain method.
    ///
    /// Separate from the event handler on purpose. Raising a real KeyEventArgs needs a
    /// PresentationSource, which needs a shown Window - and a shown Window in a test run leaks WPF
    /// state into every other STA test in the assembly. Measured: it turned a 0.8-second suite into
    /// three and a half minutes with 46 unrelated failures. So the routing is a method that the
    /// handler calls and a test can call, and what goes untested is one line of event plumbing
    /// rather than the rule.
    /// </summary>
    /// <returns>Whether the key was handled.</returns>
    public bool HandleKey(Key key)
    {
        if (Model is null)
            return false;

        switch (key)
        {
            case Key.Escape:
                // Close, and NOT reject. The back button is the one that rejects.
                Model.Escape();
                return true;

            case Key.Apps:
                // The Menu key, which the QML routes through okButton.enabled - so a screen whose
                // rule refuses the press refuses it from the keyboard too.
                return Model.MenuKey();

            default:
                return false;
        }
    }

    private DialogHostViewModel? Model => DataContext as DialogHostViewModel;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e) => e.Handled = HandleKey(e.Key);
}
