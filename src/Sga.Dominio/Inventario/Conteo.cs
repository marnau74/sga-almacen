using Sga.Dominio.Comun;

namespace Sga.Dominio.Inventario;

public enum EstadoDeConteo
{
    /// <summary>Primera vuelta: el operario cuenta sin ver lo que debería haber.</summary>
    Abierto,

    /// <summary>Segunda vuelta, solo de lo que no cuadró (puede contarlo otro operario).</summary>
    Recuento,

    /// <summary>Hay diferencias: el jefe de almacén tiene que aprobar el ajuste.</summary>
    PorAprobar,

    Cerrado,
    Anulado,
}

public sealed class LineaDeConteo
{
    public Guid UbicacionId { get; internal set; }
    public Guid ArticuloId { get; internal set; }
    public string Lote { get; internal set; } = "";
    public DateOnly? Caducidad { get; internal set; }

    /// <summary>Lo que dice el sistema al abrir el conteo. No se enseña a quien cuenta.</summary>
    public int Esperada { get; internal set; }

    public int? Primera { get; internal set; }
    public int? Segunda { get; internal set; }

    /// <summary>Lo que vale al final: el recuento si lo hubo, si no la primera vuelta.</summary>
    public int Contada => Segunda ?? Primera ?? 0;

    public int Diferencia => Contada - Esperada;
}

/// <summary>
/// Un recuento de unas ubicaciones. Ciego (quien cuenta no ve lo esperado), con segunda vuelta para lo que no cuadra
/// y con aprobación antes de tocar el stock. Mientras dura, sus ubicaciones están bloqueadas.
/// </summary>
public sealed class Conteo
{
    private readonly List<LineaDeConteo> _lineas = [];
    private readonly List<Guid> _ubicaciones = [];

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Numero { get; private set; } = "";
    public EstadoDeConteo Estado { get; private set; }
    public DateTimeOffset Abierto { get; private set; }
    public string? AprobadoPor { get; private set; }
    public IReadOnlyList<LineaDeConteo> Lineas => _lineas;
    public IReadOnlyList<Guid> Ubicaciones => _ubicaciones;

    private Conteo()
    {
    }

    /// <param name="existencias">Lo que hay ahora en esas ubicaciones según el sistema.</param>
    public static Conteo Abrir(string numero, IEnumerable<Guid> ubicaciones,
        IEnumerable<(Guid UbicacionId, Guid ArticuloId, string Lote, DateOnly? Caducidad, int Cantidad)> existencias,
        DateTimeOffset ahora)
    {
        var conteo = new Conteo { Numero = numero, Abierto = ahora };
        conteo._ubicaciones.AddRange(ubicaciones.Distinct());
        if (conteo._ubicaciones.Count == 0)
        {
            throw new ReglaIncumplida("sin-ubicaciones", "Un conteo necesita al menos una ubicación.");
        }
        foreach (var e in existencias.Where(e => e.Cantidad > 0))
        {
            conteo._lineas.Add(new LineaDeConteo
            {
                UbicacionId = e.UbicacionId,
                ArticuloId = e.ArticuloId,
                Lote = e.Lote,
                Caducidad = e.Caducidad,
                Esperada = e.Cantidad,
            });
        }
        return conteo;
    }

    /// <summary>
    /// Anota lo contado de un artículo y lote en una ubicación. Si no se esperaba, se añade (ha aparecido algo).
    /// Contar otra vez lo mismo corrige, no suma.
    /// </summary>
    public void Contar(Guid ubicacionId, Guid articuloId, string lote, DateOnly? caducidad, int cantidad)
    {
        if (!_ubicaciones.Contains(ubicacionId))
        {
            throw new ReglaIncumplida("ubicacion-fuera-del-conteo", "Esta ubicación no está en el conteo.");
        }
        if (cantidad < 0)
        {
            throw new ReglaIncumplida("cantidad-no-valida", "La cantidad no puede ser negativa.");
        }
        var linea = _lineas.FirstOrDefault(l => l.UbicacionId == ubicacionId && l.ArticuloId == articuloId && l.Lote == lote);
        switch (Estado)
        {
            case EstadoDeConteo.Abierto:
                if (linea is null)
                {
                    linea = new LineaDeConteo { UbicacionId = ubicacionId, ArticuloId = articuloId, Lote = lote, Caducidad = caducidad };
                    _lineas.Add(linea);
                }
                linea.Primera = cantidad;
                break;
            case EstadoDeConteo.Recuento:
                if (linea is null || !HayQueRecontar(linea))
                {
                    throw new ReglaIncumplida("no-hay-que-recontar", "Esto cuadró en la primera vuelta.");
                }
                linea.Segunda = cantidad;
                break;
            default:
                throw new ReglaIncumplida("conteo-cerrado", "Este conteo ya no admite cantidades.");
        }
    }

    private static bool HayQueRecontar(LineaDeConteo linea) => (linea.Primera ?? 0) != linea.Esperada;

    /// <summary>Lo que hay que volver a contar en la segunda vuelta.</summary>
    public IEnumerable<LineaDeConteo> PorRecontar => Estado == EstadoDeConteo.Recuento
        ? _lineas.Where(HayQueRecontar)
        : [];

    /// <summary>Termina una vuelta. Lo que no se ha contado se da por cero.</summary>
    public void TerminarVuelta()
    {
        switch (Estado)
        {
            case EstadoDeConteo.Abierto:
                foreach (var linea in _lineas)
                {
                    linea.Primera ??= 0;
                }
                Estado = _lineas.Any(HayQueRecontar) ? EstadoDeConteo.Recuento : EstadoDeConteo.Cerrado;
                break;
            case EstadoDeConteo.Recuento:
                foreach (var linea in _lineas.Where(HayQueRecontar))
                {
                    linea.Segunda ??= 0;
                }
                Estado = _lineas.Any(l => l.Diferencia != 0) ? EstadoDeConteo.PorAprobar : EstadoDeConteo.Cerrado;
                break;
            default:
                throw new ReglaIncumplida("conteo-cerrado", "Este conteo no tiene ninguna vuelta abierta.");
        }
    }

    /// <summary>El jefe de almacén aprueba: devuelve los ajustes que hay que hacer en el stock.</summary>
    public IReadOnlyList<LineaDeConteo> Aprobar(string usuario)
    {
        if (Estado != EstadoDeConteo.PorAprobar)
        {
            throw new ReglaIncumplida("nada-que-aprobar", "Este conteo no tiene ajustes pendientes.");
        }
        Estado = EstadoDeConteo.Cerrado;
        AprobadoPor = usuario;
        return _lineas.Where(l => l.Diferencia != 0).ToList();
    }

    public void Anular()
    {
        if (Estado is EstadoDeConteo.Cerrado or EstadoDeConteo.Anulado)
        {
            throw new ReglaIncumplida("conteo-cerrado", "Este conteo ya está cerrado.");
        }
        Estado = EstadoDeConteo.Anulado;
    }

    public bool Terminado => Estado is EstadoDeConteo.Cerrado or EstadoDeConteo.Anulado;
}
