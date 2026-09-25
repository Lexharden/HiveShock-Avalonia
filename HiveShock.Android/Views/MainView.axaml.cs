using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HiveShock.Android.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AndroidStorage.Current = TopLevel.GetTopLevel(this);
    }
}
