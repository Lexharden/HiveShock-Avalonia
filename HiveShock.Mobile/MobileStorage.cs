using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace HiveShock.Mobile;

/// <summary>
/// El selector de archivos (SAF en Android, UIDocumentPicker en iOS) se pide vía <see cref="Avalonia.Controls.TopLevel"/>,
/// no hay Window como en escritorio. MainView guarda aquí su TopLevel al montarse para que
/// los ViewModels (que no ven la vista) puedan pedir el StorageProvider.
/// </summary>
public static class MobileStorage
{
    public static TopLevel? Current { get; set; }

    public static IStorageProvider? Provider => Current?.StorageProvider;
}
