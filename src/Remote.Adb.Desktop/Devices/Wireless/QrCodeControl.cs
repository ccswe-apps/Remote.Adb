using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using QRCoder;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// Draws <see cref="Text"/> as a QR code: black modules on white, whatever the app theme — a phone camera needs that
/// contrast, and QRCoder's module matrix already includes the quiet zone. The modules are snapped to whole pixels so the
/// code stays crisp.
/// </summary>
public sealed class QrCodeControl : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<QrCodeControl, string?>(nameof(Text));

    private List<BitArray>? _modules;

    static QrCodeControl()
    {
        AffectsRender<QrCodeControl>(TextProperty);
    }

    /// <summary>The text to encode.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static List<BitArray>? Encode(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return data.ModuleMatrix.Select(row => new BitArray(row)).ToList();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            _modules = Encode(Text);
        }
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        context.FillRectangle(Brushes.White, new Rect(0, 0, size, size));

        if (_modules is not { Count: > 0 } modules)
        {
            return;
        }

        var count = modules.Count;
        var module = Math.Floor(size / count);
        if (module < 1)
        {
            return;
        }

        // Center the whole-pixel grid inside the square.
        var offset = Math.Floor((size - module * count) / 2);
        for (var row = 0; row < count; row++)
        {
            for (var column = 0; column < count; column++)
            {
                if (modules[row][column])
                {
                    context.FillRectangle(Brushes.Black, new Rect(offset + column * module, offset + row * module, module, module));
                }
            }
        }
    }
}
