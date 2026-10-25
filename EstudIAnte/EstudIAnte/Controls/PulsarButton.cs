using System.Threading;
using Microsoft.Maui.Controls;

namespace EstudIAnte.Controls;

/// <summary>
/// Botón con animación de pulsación: al presionar se "hunde" (escala 0.92)
/// y al soltar vuelve a su tamaño con un rebote suave.
/// También tiene estado de carga: al poner <see cref="IsBusy"/> en true,
/// se desactiva, muestra un reloj de arena y late suavemente (opacidad)
/// hasta que la operación termina. Luego vuelve a la normalidad.
/// </summary>
public class PulsarButton : Button
{
    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy), typeof(bool), typeof(PulsarButton), false,
        propertyChanged: OnIsBusyChanged);

    /// <summary>Ponlo en true mientras corre una operación larga; false al terminar.</summary>
    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public static readonly BindableProperty TextoCargaProperty = BindableProperty.Create(
        nameof(TextoCarga), typeof(string), typeof(PulsarButton), "⏳  Procesando...");

    /// <summary>Texto que se muestra mientras IsBusy es true (ej. "⏳  Generando...").</summary>
    public string TextoCarga
    {
        get => (string)GetValue(TextoCargaProperty);
        set => SetValue(TextoCargaProperty, value);
    }

    string? _textoOriginal;
    CancellationTokenSource? _latido;

    public PulsarButton()
    {
        Pressed += OnPresionado;
        Released += OnSoltado;
    }

    // ---------- animación de pulsación ----------

    async void OnPresionado(object? sender, EventArgs e)
    {
        if (IsBusy || !IsEnabled)
            return;
        try { await this.ScaleTo(0.92, 60, Easing.CubicOut); } catch { /* animación cancelada */ }
    }

    async void OnSoltado(object? sender, EventArgs e)
    {
        try { await this.ScaleTo(1, 120, Easing.SpringOut); } catch { /* animación cancelada */ }
    }

    // ---------- estado de carga ----------

    static void OnIsBusyChanged(BindableObject bindable, object valorAntes, object valorNuevo)
    {
        if (bindable is not PulsarButton boton)
            return;

        if ((bool)valorNuevo)
            boton.IniciarCarga();
        else
            boton.TerminarCarga();
    }

    void IniciarCarga()
    {
        IsEnabled = false;

        // Muestra el estado de carga conservando el texto original.
        if (_textoOriginal is null)
            _textoOriginal = Text;
        Text = TextoCarga;

        // Latido suave de opacidad hasta que termine la carga.
        _latido?.Cancel();
        _latido = new CancellationTokenSource();
        var token = _latido.Token;

        _ = LatidoAsync(token);
    }

    async Task LatidoAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await this.FadeTo(0.55, 450, Easing.SinInOut);
                if (token.IsCancellationRequested)
                    break;
                await this.FadeTo(1, 450, Easing.SinInOut);
            }
        }
        catch
        {
            // La animación se corta al terminar la carga.
        }
    }

    void TerminarCarga()
    {
        _latido?.Cancel();
        _latido = null;

        // Vuelve a la normalidad con un pequeño rebote.
        _ = this.FadeTo(1, 80);
        _ = this.ScaleTo(1, 120, Easing.SpringOut);

        Text = _textoOriginal ?? Text;
        _textoOriginal = null;
        IsEnabled = true;
    }
}
