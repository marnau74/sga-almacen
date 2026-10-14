using Sga.Dominio.Codigos;
using Sga.Dominio.Comun;

namespace Sga.Dominio.Catalogo;

/// <summary>Un artículo del almacén. Las cantidades del stock van siempre en unidades; las cajas solo en el borde.</summary>
public sealed class Articulo
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Gtin Ean { get; private set; }
    public string Nombre { get; private set; } = "";
    public string? Marca { get; private set; }

    /// <summary>Formato de venta tal como lo describe el fabricante («1 l», «6 x 200 ml»).</summary>
    public string? Formato { get; private set; }

    public int UnidadesPorCaja { get; private set; } = 1;

    /// <summary>Si cada entrada lleva lote (y por tanto se puede retirar un lote concreto).</summary>
    public bool ControlDeLote { get; private set; }

    /// <summary>Si cada entrada lleva fecha de caducidad (y se sirve por FEFO).</summary>
    public bool ControlDeCaducidad { get; private set; }

    public bool Activo { get; private set; } = true;

    private Articulo()
    {
    }

    public static Articulo Crear(Gtin ean, string nombre, string? marca, string? formato, int unidadesPorCaja,
        bool controlDeLote, bool controlDeCaducidad)
    {
        var articulo = new Articulo { Ean = ean };
        articulo.Cambiar(nombre, marca, formato, unidadesPorCaja, controlDeLote, controlDeCaducidad);
        return articulo;
    }

    public void Cambiar(string nombre, string? marca, string? formato, int unidadesPorCaja, bool controlDeLote,
        bool controlDeCaducidad)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ReglaIncumplida("nombre-obligatorio", "El artículo necesita un nombre.");
        }
        if (unidadesPorCaja < 1)
        {
            throw new ReglaIncumplida("unidades-por-caja", "Una caja tiene al menos una unidad.");
        }
        // Una caducidad sin lote no se puede retirar ni separar: si hay caducidad, hay lote.
        if (controlDeCaducidad && !controlDeLote)
        {
            throw new ReglaIncumplida("caducidad-sin-lote", "Un artículo con caducidad tiene que llevar lote.");
        }
        Nombre = nombre.Trim();
        Marca = string.IsNullOrWhiteSpace(marca) ? null : marca.Trim();
        Formato = string.IsNullOrWhiteSpace(formato) ? null : formato.Trim();
        UnidadesPorCaja = unidadesPorCaja;
        ControlDeLote = controlDeLote;
        ControlDeCaducidad = controlDeCaducidad;
    }

    public void Desactivar() => Activo = false;

    /// <summary>
    /// Comprueba lo que trae una entrada: lote y caducidad cuando el artículo los lleva, y nada de eso cuando no.
    /// </summary>
    public (string Lote, DateOnly? Caducidad) ValidarLote(string? lote, DateOnly? caducidad)
    {
        var limpio = lote?.Trim() ?? "";
        if (ControlDeLote && limpio.Length == 0)
        {
            throw new ReglaIncumplida("lote-obligatorio", $"{Nombre} necesita el lote.");
        }
        if (ControlDeCaducidad && caducidad is null)
        {
            throw new ReglaIncumplida("caducidad-obligatoria", $"{Nombre} necesita la fecha de caducidad.");
        }
        if (limpio.Length > 20)
        {
            throw new ReglaIncumplida("lote-no-valido", "Un lote tiene como mucho 20 caracteres.");
        }
        return (ControlDeLote ? limpio : "", ControlDeCaducidad ? caducidad : null);
    }
}
