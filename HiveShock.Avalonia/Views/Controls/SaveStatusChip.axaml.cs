using Avalonia.Controls;

namespace HiveShock.Avalonia.Views.Controls;

/// <summary>Etiqueta de estado de guardado (verde = guardado, dorado = pendiente, rojo = error) + casilla de automático.</summary>
public partial class SaveStatusChip : UserControl
{
    public SaveStatusChip()
    {
        InitializeComponent();
    }
}
