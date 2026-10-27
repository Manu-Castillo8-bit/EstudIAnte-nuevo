using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using EstudIAnte.Services;

namespace EstudIAnte
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "content", DataMimeType = "application/pdf")]
    [IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "content", DataMimeType = "application/octet-stream")]
    [IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "content", DataMimeType = "application/x-pdf")]
    [IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "file", DataMimeType = "application/pdf")]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            if (Intent?.Action == Intent.ActionView)
                ExternalDocumentRequest.Begin();
            base.OnCreate(savedInstanceState);
            if (Intent?.Action == Intent.ActionView)
                _ = RecibirDocumentoAsync(Intent, entregarAlInicio: true);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            if (intent is not null)
            {
                ExternalDocumentRequest.Begin();
                _ = RecibirDocumentoAsync(intent, entregarAlInicio: false);
            }
        }

        async Task RecibirDocumentoAsync(Intent? intent, bool entregarAlInicio)
        {
            if (intent?.Action != Intent.ActionView || intent.Data is null)
                return;

            var uri = intent.Data;
            try
            {
                var nombre = ObtenerNombre(uri) ?? "documento.pdf";
                if (!Path.GetExtension(nombre).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    nombre += ".pdf";

                using var stream = ContentResolver?.OpenInputStream(uri);
                if (stream is null)
                {
                    ExternalDocumentRequest.Set(null, "No se pudo leer el archivo seleccionado.");
                    return;
                }

                var ruta = Path.Combine(CacheDir?.AbsolutePath ?? FilesDir?.AbsolutePath ?? string.Empty,
                    $"{Guid.NewGuid():N}.pdf");
                await using (var salida = File.Create(ruta))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int leidos;
                    while ((leidos = await stream.ReadAsync(buffer)) > 0)
                    {
                        total += leidos;
                        if (total > 20 * 1024 * 1024)
                        {
                            File.Delete(ruta);
                            ExternalDocumentRequest.Set(null, "El archivo supera el límite de 20 MB.");
                            return;
                        }
                        await salida.WriteAsync(buffer.AsMemory(0, leidos));
                    }
                }

                await using (var validacion = File.OpenRead(ruta))
                {
                    var firma = new byte[5];
                    var leidos = await validacion.ReadAsync(firma);
                    if (leidos != firma.Length || !firma.AsSpan().SequenceEqual("%PDF-"u8))
                    {
                        File.Delete(ruta);
                        ExternalDocumentRequest.Set(null, "El archivo seleccionado no es un PDF compatible.");
                        return;
                    }
                }

                var archivo = new ExternalDocumentFile(ruta, nombre);
                if (!entregarAlInicio && MainPage.Actual is { } pagina)
                {
                    await MainThread.InvokeOnMainThreadAsync(
                        () => pagina.AbrirDocumentoExternoAsync(archivo.Path, archivo.FileName));
                }
                else
                {
                    ExternalDocumentRequest.Set(archivo);
                }
            }
            catch (Exception ex)
            {
                if (!entregarAlInicio && MainPage.Actual is { } pagina)
                    await MainThread.InvokeOnMainThreadAsync(
                        () => pagina.DisplayAlertAsync("No se pudo abrir el documento", ex.Message, "OK"));
                else
                    ExternalDocumentRequest.Set(null, "No se pudo leer el archivo: " + ex.Message);
            }
        }

        string? ObtenerNombre(Android.Net.Uri uri)
        {
            using var cursor = ContentResolver?.Query(uri, null, null, null, null);
            var columna = cursor?.GetColumnIndex(OpenableColumns.DisplayName) ?? -1;
            return columna >= 0 && cursor?.MoveToFirst() == true
                ? cursor.GetString(columna)
                : null;
        }
    }
}
