using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using HiveShock.Avalonia.ViewModels;

namespace HiveShock.Avalonia;

[RequiresUnreferencedCode("ViewLocator uses reflection to map ViewModels to Pages.")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null or MainViewModel)
        {
            return null;
        }

        var typeName = param.GetType().Name;
        if (!typeName.EndsWith("ViewModel", StringComparison.Ordinal))
        {
            return new TextBlock { Text = "No se encontró: " + typeName };
        }

        var pageName = typeName[..^"ViewModel".Length] + "Page";
        var type = Type.GetType($"HiveShock.Avalonia.Views.Pages.{pageName}");
        if (type is null)
        {
            return new TextBlock { Text = "No se encontró la página: " + pageName };
        }

        return (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ViewModelBase && data is not MainViewModel;
}
