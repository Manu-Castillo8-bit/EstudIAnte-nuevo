namespace EstudIAnte.Services;

/// <summary>
/// Guarda un archivo en el equipo con un diálogo "Guardar como" (Windows).
/// En otras plataformas lo guarda en la carpeta "Exportados" de la app y
/// devuelve esa ruta. Devuelve null si el usuario canceló.
/// </summary>
public static class FileSaveHelper
{
    public static async Task<string?> GuardarAsync(string nombreArchivo, byte[] bytes)
    {
#if WINDOWS
        var ventana = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            as Microsoft.UI.Xaml.Window;

        if (ventana is null)
            throw new InvalidOperationException("No hay una ventana activa para mostrar el diálogo de guardado.");

        var picker = new Windows.Storage.Pickers.FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(ventana));

        var extension = Path.GetExtension(nombreArchivo);
        if (string.IsNullOrEmpty(extension))
            extension = ".txt";

        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(nombreArchivo);
        picker.FileTypeChoices.Add("Archivo " + extension.TrimStart('.').ToUpperInvariant(),
            new List<string> { extension });
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;

        var archivo = await picker.PickSaveFileAsync();
        if (archivo is null)
            return null; // cancelado por el usuario

        await Windows.Storage.FileIO.WriteBytesAsync(archivo, bytes);
        return archivo.Path;
#else
        var carpeta = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "Exportados");
        Directory.CreateDirectory(carpeta);

        var ruta = Path.Combine(carpeta, nombreArchivo);
        await File.WriteAllBytesAsync(ruta, bytes);
        return ruta;
#endif
    }
}
