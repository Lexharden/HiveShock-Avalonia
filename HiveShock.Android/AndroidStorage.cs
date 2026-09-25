using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace HiveShock.Android;

/// <summary>
/// El selector de archivos (SAF) de Android se pide vía <see cref="Avalonia.Controls.TopLevel"/>,
/// no hay Window como en escritorio. MainView guarda aquí su TopLevel al montarse para que
/// los ViewModels (que no ven la vista) puedan pedir el StorageProvider.
/// </summary>
internal static class AndroidStorage
{
    public static TopLevel? Current { get; set; }

    public static IStorageProvider? Provider => Current?.StorageProvider;
}
