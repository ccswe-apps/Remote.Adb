using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// A short, scrollable list of wireless-debugging services discovered on the network, shared by the dialog's Pair and
/// Connect tabs: each tab passes its own services, what clicking one does, and the text to show when there are none.
/// </summary>
public partial class DiscoveredServicesView : UserControl
{
    public static readonly StyledProperty<string?> EmptyTextProperty =
        AvaloniaProperty.Register<DiscoveredServicesView, string?>(nameof(EmptyText));

    public static readonly StyledProperty<bool> IsUnavailableProperty =
        AvaloniaProperty.Register<DiscoveredServicesView, bool>(nameof(IsUnavailable));

    public static readonly StyledProperty<ICommand?> SelectCommandProperty =
        AvaloniaProperty.Register<DiscoveredServicesView, ICommand?>(nameof(SelectCommand));

    public static readonly StyledProperty<IEnumerable?> ServicesProperty =
        AvaloniaProperty.Register<DiscoveredServicesView, IEnumerable?>(nameof(Services));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DiscoveredServicesView, string?>(nameof(Title));

    public DiscoveredServicesView()
    {
        InitializeComponent();
    }

    /// <summary>Shown while discovery works but has found nothing.</summary>
    public string? EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    /// <summary>When set, a "discovery unavailable" hint replaces the list.</summary>
    public bool IsUnavailable
    {
        get => GetValue(IsUnavailableProperty);
        set => SetValue(IsUnavailableProperty, value);
    }

    /// <summary>Invoked with the clicked <c>MdnsService</c>.</summary>
    public ICommand? SelectCommand
    {
        get => GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public IEnumerable? Services
    {
        get => GetValue(ServicesProperty);
        set => SetValue(ServicesProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
