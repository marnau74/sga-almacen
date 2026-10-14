using System.Globalization;

using Sga.Dominio.Comun;

namespace Sga.Dominio.Codigos;

/// <summary>
/// Lo que trae un escaneo. Un lector puede devolver un EAN suelto, una etiqueta GS1-128 con artículo, lote, caducidad
/// y cantidad, un SSCC de bulto o cualquier otro texto (el código de una ubicación).
/// </summary>
public sealed record Lectura(string Texto)
{
    public Gtin? Gtin { get; private init; }
    public string? Lote { get; private init; }
    public DateOnly? Caducidad { get; private init; }
    public int? Cantidad { get; private init; }
    public Sscc? Sscc { get; private init; }

    public bool EsGs1 => Gtin is not null || Sscc is not null;

    /// <summary>Separador de campos de longitud variable en GS1-128 (FNC1, que el lector envía como GS).</summary>
    public const char Separador = '\u001D';

    // Identificador de aplicación → longitud fija (o null si es variable, con su máximo).
    private static readonly Dictionary<string, (int? Fija, int Maxima)> Identificadores = new()
    {
        ["00"] = (18, 18), // SSCC
        ["01"] = (14, 14), // GTIN
        ["02"] = (14, 14), // GTIN de lo que contiene
        ["10"] = (null, 20), // lote
        ["11"] = (6, 6), // fecha de fabricación
        ["15"] = (6, 6), // consumo preferente
        ["17"] = (6, 6), // caducidad
        ["37"] = (null, 8), // cantidad
    };

    public static Lectura Interpretar(string texto)
    {
        var limpio = (texto ?? "").Trim();
        // Prefijo de simbología que añaden algunos lectores (]C1 = GS1-128, ]E0 = EAN-13).
        if (limpio.Length > 3 && limpio[0] == ']')
        {
            limpio = limpio[3..];
        }
        if (limpio.Length is 13 or 14 && Codigos.Gtin.TryCrear(limpio, out var gtin))
        {
            return new Lectura(texto!) { Gtin = gtin };
        }
        if (limpio.StartsWith('('))
        {
            return new Lectura(texto!).ConCampos(CamposEntreParentesis(limpio));
        }
        if (limpio.Length == 18 && Codigos.Sscc.TryCrear(limpio, out var sscc))
        {
            return new Lectura(texto!) { Sscc = sscc };
        }
        // GS1-128 sin paréntesis: empieza por un identificador de bulto o de artículo y los campos variables acaban
        // en el separador.
        if (limpio.Length >= 16 && limpio[..2] is "00" or "01" or "02" &&
            limpio.All(c => char.IsAsciiLetterOrDigit(c) || c == Separador))
        {
            return new Lectura(texto!).ConCampos(CamposSeguidos(limpio));
        }
        return new Lectura(limpio);
    }

    private static IEnumerable<(string Ia, string Valor)> CamposEntreParentesis(string texto)
    {
        var resto = texto;
        while (resto.Length > 0)
        {
            var cierre = resto.IndexOf(')', StringComparison.Ordinal);
            if (resto[0] != '(' || cierre < 0)
            {
                throw NoValida(texto);
            }
            var ia = resto[1..cierre];
            var siguiente = resto.IndexOf('(', cierre);
            var valor = siguiente < 0 ? resto[(cierre + 1)..] : resto[(cierre + 1)..siguiente];
            yield return (ia, valor);
            resto = siguiente < 0 ? "" : resto[siguiente..];
        }
    }

    private static IEnumerable<(string Ia, string Valor)> CamposSeguidos(string texto)
    {
        var i = 0;
        while (i < texto.Length)
        {
            if (texto[i] == Separador)
            {
                i++;
                continue;
            }
            if (i + 2 > texto.Length || !Identificadores.TryGetValue(texto.Substring(i, 2), out var formato))
            {
                throw NoValida(texto);
            }
            var ia = texto.Substring(i, 2);
            i += 2;
            int longitud;
            if (formato.Fija is { } fija)
            {
                longitud = fija;
            }
            else
            {
                var fin = texto.IndexOf(Separador, i);
                longitud = (fin < 0 ? texto.Length : fin) - i;
            }
            if (longitud > formato.Maxima || i + longitud > texto.Length)
            {
                throw NoValida(texto);
            }
            yield return (ia, texto.Substring(i, longitud));
            i += longitud;
        }
    }

    private Lectura ConCampos(IEnumerable<(string Ia, string Valor)> campos)
    {
        var lectura = this;
        foreach (var (ia, valor) in campos)
        {
            lectura = ia switch
            {
                "00" => lectura with { Sscc = Codigos.Sscc.TryCrear(valor, out var s) ? s : throw NoValida(Texto) },
                "01" or "02" => lectura with { Gtin = Codigos.Gtin.Crear(valor) },
                "10" when valor.Length is > 0 and <= 20 => lectura with { Lote = valor },
                "17" => lectura with { Caducidad = Fecha(valor) },
                // Si no hay caducidad, el consumo preferente hace sus veces.
                "15" => lectura with { Caducidad = lectura.Caducidad ?? Fecha(valor) },
                "11" => lectura,
                "37" when int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 =>
                    lectura with { Cantidad = n },
                _ => throw NoValida(Texto),
            };
        }
        return lectura;
    }

    /// <summary>AAMMDD. Un día 00 significa el último día del mes.</summary>
    private DateOnly Fecha(string valor)
    {
        if (valor.Length != 6 || !valor.All(char.IsAsciiDigit))
        {
            throw NoValida(Texto);
        }
        var anio = 2000 + int.Parse(valor[..2], CultureInfo.InvariantCulture);
        var mes = int.Parse(valor[2..4], CultureInfo.InvariantCulture);
        var dia = int.Parse(valor[4..], CultureInfo.InvariantCulture);
        if (mes is < 1 or > 12)
        {
            throw NoValida(Texto);
        }
        var ultimo = DateTime.DaysInMonth(anio, mes);
        if (dia > ultimo)
        {
            throw NoValida(Texto);
        }
        return new DateOnly(anio, mes, dia == 0 ? ultimo : dia);
    }

    private static ReglaIncumplida NoValida(string texto) =>
        new("etiqueta-no-valida", $"No se entiende la etiqueta «{texto}».");
}
