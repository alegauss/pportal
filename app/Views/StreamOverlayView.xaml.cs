using System.Windows.Controls;

namespace ChiakiNg.Views;

/// <summary>
/// PP10: the stream overlay. A constructor, by the rule PP13 set.
///
/// Nothing to wire, and that is the finding rather than an absence of work: the video is an
/// ImageSource the view model holds, so this screen has no more code behind it than a settings tab
/// does. The whole of PP10's cost was decided by PP9 choosing D3DImage.
/// </summary>
public partial class StreamOverlayView : UserControl
{
    public StreamOverlayView() => InitializeComponent();
}
