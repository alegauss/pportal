using System.Windows.Controls;

namespace ChiakiNg.Views;

/// <summary>
/// PP10: the in-stream menu. A constructor, by the rule PP13 set.
///
/// In the Qt client this is a Window; here it is a control over the video, because PP9 made the
/// video part of the visual tree and a second window would only be a way of drawing over something
/// that is already drawable.
/// </summary>
public partial class StreamMenuView : UserControl
{
    public StreamMenuView() => InitializeComponent();
}
