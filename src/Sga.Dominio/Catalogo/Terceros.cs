using Sga.Dominio.Comun;

namespace Sga.Dominio.Catalogo;

public sealed class Proveedor
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Codigo { get; private set; } = "";
    public string Nombre { get; private set; } = "";

    private Proveedor()
    {
    }

    public static Proveedor Crear(string codigo, string nombre) => new()
    {
        Codigo = Obligatorio(codigo, "código"),
        Nombre = Obligatorio(nombre, "nombre"),
    };

    internal static string Obligatorio(string valor, string campo) =>
        string.IsNullOrWhiteSpace(valor)
            ? throw new ReglaIncumplida("dato-obligatorio", $"Falta el {campo}.")
            : valor.Trim();
}

public sealed class Cliente
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Codigo { get; private set; } = "";
    public string Nombre { get; private set; } = "";

    /// <summary>
    /// Días de vida útil que tiene que quedarle a un producto el día que se le sirve. Una cadena de supermercados no
    /// acepta un yogur que caduca pasado mañana.
    /// </summary>
    public int DiasMinimosDeVidaUtil { get; private set; }

    private Cliente()
    {
    }

    public static Cliente Crear(string codigo, string nombre, int diasMinimosDeVidaUtil)
    {
        if (diasMinimosDeVidaUtil is < 0 or > 3650)
        {
            throw new ReglaIncumplida("vida-util-no-valida", "Los días mínimos de vida útil van de 0 a 3650.");
        }
        return new Cliente
        {
            Codigo = Proveedor.Obligatorio(codigo, "código"),
            Nombre = Proveedor.Obligatorio(nombre, "nombre"),
            DiasMinimosDeVidaUtil = diasMinimosDeVidaUtil,
        };
    }
}
