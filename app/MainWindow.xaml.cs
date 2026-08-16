namespace ChiakiNg;

/// <summary>
/// The window that opens empty (PP1). Its code-behind is empty too, and that is a decision
/// rather than an omission: PP37 files the case that a screen ported into code-behind can only
/// be exercised by opening a window, so the first thing to go in here should be a binding to a
/// view model rather than the first event handler.
/// </summary>
public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
