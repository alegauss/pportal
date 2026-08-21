using System.Windows.Controls;

namespace ChiakiNg.Views;

/// <summary>
/// PP18: the controller mapping screen. A constructor, by the rule PP13 set.
///
/// The grid binds a collection the view model refills in place, so there is no ItemsSource to
/// assign. What is deliberately NOT here is any handling of a press: the screen does not read the
/// device, it asks the input path to capture and is told what arrived.
/// </summary>
public partial class ControllerMappingView : UserControl
{
    public ControllerMappingView() => InitializeComponent();
}
