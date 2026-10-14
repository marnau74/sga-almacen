using Sga.Dominio.Comun;

namespace Sga.Dominio.Entradas;

public enum EstadoDeRecepcion
{
    Prevista,
    EnCurso,
    Recibida,

    /// <summary>Cerrada con algo de más, de menos o dañado: alguien de oficina tiene que mirarla.</summary>
    ConDiferencias,
}

public sealed class LineaDeRecepcion
{
    public int Numero { get; internal set; }
    public Guid ArticuloId { get; internal set; }
    public int Prevista { get; internal set; }
    public int Recibida { get; internal set; }
    public int Danada { get; internal set; }
}

/// <summary>Lo que se ha escaneado al descargar: un artículo, un lote, una cantidad.</summary>
public sealed class Descarga
{
    public int Numero { get; internal set; }
    public Guid ArticuloId { get; internal set; }
    public string Lote { get; internal set; } = "";
    public DateOnly? Caducidad { get; internal set; }
    public int Cantidad { get; internal set; }
    public bool Danado { get; internal set; }
    public string Operario { get; internal set; } = "";
    public DateTimeOffset Momento { get; internal set; }
}

/// <summary>Una entrega de un proveedor: lo que se esperaba y lo que de verdad ha llegado.</summary>
public sealed class Recepcion
{
    private readonly List<LineaDeRecepcion> _lineas = [];
    private readonly List<Descarga> _descargas = [];

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Numero { get; private set; } = "";
    public Guid ProveedorId { get; private set; }
    public DateOnly FechaPrevista { get; private set; }
    public EstadoDeRecepcion Estado { get; private set; }
    public string? Observaciones { get; private set; }
    public IReadOnlyList<LineaDeRecepcion> Lineas => _lineas;
    public IReadOnlyList<Descarga> Descargas => _descargas;

    private Recepcion()
    {
    }

    public static Recepcion Prever(string numero, Guid proveedorId, DateOnly fechaPrevista,
        IEnumerable<(Guid ArticuloId, int Cantidad)> lineas)
    {
        var recepcion = new Recepcion { Numero = numero, ProveedorId = proveedorId, FechaPrevista = fechaPrevista };
        foreach (var (articuloId, cantidad) in lineas)
        {
            if (cantidad <= 0)
            {
                throw new ReglaIncumplida("cantidad-no-valida", "Cada línea tiene que pedir al menos una unidad.");
            }
            if (recepcion._lineas.Any(l => l.ArticuloId == articuloId))
            {
                throw new ReglaIncumplida("linea-repetida", "Un artículo va en una sola línea.");
            }
            recepcion._lineas.Add(new LineaDeRecepcion
            {
                Numero = recepcion._lineas.Count + 1,
                ArticuloId = articuloId,
                Prevista = cantidad,
            });
        }
        if (recepcion._lineas.Count == 0)
        {
            throw new ReglaIncumplida("sin-lineas", "La recepción necesita al menos una línea.");
        }
        return recepcion;
    }

    /// <summary>
    /// Anota lo que se acaba de descargar. Se admite recibir de más (el proveedor manda de más y se ve al cerrar),
    /// pero no un artículo que no venía.
    /// </summary>
    public Descarga Registrar(Guid articuloId, string lote, DateOnly? caducidad, int cantidad, bool danado,
        string operario, DateTimeOffset ahora)
    {
        if (Estado is EstadoDeRecepcion.Recibida or EstadoDeRecepcion.ConDiferencias)
        {
            throw new ReglaIncumplida("recepcion-cerrada", "Esta recepción ya está cerrada.");
        }
        if (cantidad <= 0)
        {
            throw new ReglaIncumplida("cantidad-no-valida", "La cantidad tiene que ser mayor que cero.");
        }
        var linea = _lineas.FirstOrDefault(l => l.ArticuloId == articuloId)
                    ?? throw new ReglaIncumplida("articulo-no-previsto", "Este artículo no viene en esta recepción.");
        if (danado)
        {
            linea.Danada += cantidad;
        }
        else
        {
            linea.Recibida += cantidad;
        }
        var descarga = new Descarga
        {
            Numero = _descargas.Count + 1,
            ArticuloId = articuloId,
            Lote = lote,
            Caducidad = caducidad,
            Cantidad = cantidad,
            Danado = danado,
            Operario = operario,
            Momento = ahora,
        };
        _descargas.Add(descarga);
        Estado = EstadoDeRecepcion.EnCurso;
        return descarga;
    }

    public bool TieneDiferencias => _lineas.Any(l => l.Recibida != l.Prevista || l.Danada > 0);

    public void Cerrar(string? observaciones)
    {
        if (Estado != EstadoDeRecepcion.EnCurso)
        {
            throw new ReglaIncumplida("recepcion-sin-empezar",
                Estado == EstadoDeRecepcion.Prevista ? "Todavía no se ha recibido nada." : "Ya está cerrada.");
        }
        Observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
        Estado = TieneDiferencias ? EstadoDeRecepcion.ConDiferencias : EstadoDeRecepcion.Recibida;
    }
}
