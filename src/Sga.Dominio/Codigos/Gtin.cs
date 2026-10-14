using Sga.Dominio.Comun;

namespace Sga.Dominio.Codigos;

/// <summary>
/// Código de artículo GS1 (GTIN). Se guarda siempre como GTIN-13 (el EAN de toda la vida): un GTIN-14 que empieza por
/// 0, como el que va dentro de un GS1-128, es el mismo artículo; un EAN-8 no se usa en este almacén.
/// </summary>
public readonly record struct Gtin
{
    public string Valor { get; }

    private Gtin(string valor) => Valor = valor;

    public static Gtin Crear(string texto) =>
        TryCrear(texto, out var gtin)
            ? gtin
            : throw new ReglaIncumplida("gtin-no-valido", $"«{texto}» no es un código de artículo válido.");

    public static bool TryCrear(string? texto, out Gtin gtin)
    {
        gtin = default;
        var limpio = texto?.Trim() ?? "";
        if (limpio.Length == 14 && limpio[0] == '0')
        {
            limpio = limpio[1..];
        }
        if (limpio.Length != 13 || !limpio.All(char.IsAsciiDigit) || !DigitoDeControl.EsValido(limpio))
        {
            return false;
        }
        gtin = new Gtin(limpio);
        return true;
    }

    public override string ToString() => Valor;
}

/// <summary>El dígito de control de los códigos GS1 (GTIN, SSCC): módulo 10 con pesos 3 y 1 desde la derecha.</summary>
public static class DigitoDeControl
{
    public static int Calcular(ReadOnlySpan<char> sinDigito)
    {
        var suma = 0;
        for (var i = 0; i < sinDigito.Length; i++)
        {
            var cifra = sinDigito[sinDigito.Length - 1 - i] - '0';
            suma += i % 2 == 0 ? cifra * 3 : cifra;
        }
        return (10 - (suma % 10)) % 10;
    }

    public static bool EsValido(string codigo) =>
        codigo.Length > 1 && codigo[^1] - '0' == Calcular(codigo.AsSpan(0, codigo.Length - 1));
}
