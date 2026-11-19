using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace HiveShock.Avalonia.Views.Controls;

/// <summary>
/// Icono de trazo (estilo «outline») dibujado desde una geometría en un lienzo de 24×24.
/// Hereda el color del texto que lo rodea (<see cref="TextElement.ForegroundProperty"/>), así que dentro de
/// un botón toma el color del botón y cambia con sus estados (hover, activo, deshabilitado) sin estilos extra.
/// Multiplataforma: no depende de fuentes de iconos del sistema (Segoe Fluent solo existe en Windows).
/// </summary>
public sealed class Icon : Control
{
    private const double Canvas = 24;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(Size), 18);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeThickness), 1.8);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, StrokeThicknessProperty, ForegroundProperty);
        AffectsMeasure<Icon>(SizeProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data || Foreground is not { } brush)
        {
            return;
        }

        var scale = Size / Canvas;
        var offsetX = (Bounds.Width - Size) / 2;
        var offsetY = (Bounds.Height - Size) / 2;
        // El grosor se compensa con la escala para que el trazo se vea igual a cualquier tamaño.
        var pen = new Pen(brush, StrokeThickness / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            context.DrawGeometry(null, pen, data);
        }
    }
}
