namespace EstudIAnte.Services;

public class IAException : Exception
{
    public bool CuotaAgotada { get; }
    public IAException(string mensaje, bool cuotaAgotada = false) : base(mensaje) => CuotaAgotada = cuotaAgotada;
}

/// <summary>
/// Contrato de un proveedor de IA (Gemini, Groq, OpenRouter, etc.).
/// </summary>
public interface IAIProvider
{
    /// <summary>Nombre visible, ej. "Google Gemini (gemini-2.0-flash)".</summary>
    string Nombre { get; }

    /// <summary>Indica si tiene una clave configurada.</summary>
    bool Configurado { get; }

    /// <summary>Envía un prompt y devuelve el texto de respuesta.</summary>
    Task<string> GenerarAsync(string prompt, CancellationToken ct = default);
}
