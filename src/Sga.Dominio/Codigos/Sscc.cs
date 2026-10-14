using System.Globalization;
using Sga.Dominio.Comun;

namespace Sga.Dominio.Codigos;

/// <summary>
/// Código de bulto (SSCC, 18 cifras): extensión, prefijo de empresa GS1, número de serie y dígito de control.
/// </summary>
public readonly record struct Sscc
{
    public string Valor { get; }

    private Sscc(string valor) => Valor = valor;

    /// <summary>
    /// Genera el SSCC de un bulto. <paramref name="prefijo"/> es el prefijo de empresa (de 7 a 10 cifras); sin uno
    /// propio de GS1 se usa uno de circulación restringida (020-029), que solo vale dentro de la empresa.
    /// </summary>
    public static Sscc Generar(string prefijo, int extension, long serie)
    {
        if (prefijo.Length is < 7 or > 10 || !prefijo.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("El prefijo de empresa GS1 tiene de 7 a 10 cifras.", nameof(prefijo));
        }
        if (extension is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(extension), "La extensión es una cifra.");
        }
        var cifrasDeSerie = 16 - prefijo.Length;
        var maximo = (long)Math.Pow(10, cifrasDeSerie);
        if (serie < 0 || serie >= maximo)
        {
            throw new ReglaIncumplida("sscc-agotado", "No quedan números de bulto con este prefijo.");
        }
        var sinDigito = extension.ToString(CultureInfo.InvariantCulture) + prefijo +
                        serie.ToString(CultureInfo.InvariantCulture).PadLeft(cifrasDeSerie, '0');
        return new Sscc(sinDigito + DigitoDeControl.Calcular(sinDigito).ToString(CultureInfo.InvariantCulture));
    }

    public static bool TryCrear(string? texto, out Sscc sscc)
    {
        sscc = default;
        var limpio = texto?.Trim() ?? "";
        if (limpio.Length != 18 || !limpio.All(char.IsAsciiDigit) || !DigitoDeControl.EsValido(limpio))
        {
            return false;
        }
        sscc = new Sscc(limpio);
        return true;
    }

    /// <summary>El contenido de la etiqueta GS1-128: identificador de aplicación 00 y el código.</summary>
    public string ParaEtiqueta() => "00" + Valor;

    public override string ToString() => Valor;
}
