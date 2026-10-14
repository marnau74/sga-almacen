using Sga.Dominio.Comun;

namespace Sga.Dominio.Stock;

public enum TipoDeMovimiento
{
    /// <summary>Llega del proveedor: sin origen.</summary>
    Recepcion,

    /// <summary>De la playa de recepción a su hueco.</summary>
    Ubicacion,

    /// <summary>De un hueco a otro, a mano.</summary>
    Traslado,

    /// <summary>De la reserva al hueco de picking.</summary>
    Reposicion,

    /// <summary>Del hueco al muelle para un pedido.</summary>
    Picking,

    /// <summary>Del muelle al camión: sin destino.</summary>
    Expedicion,

    /// <summary>Corrección tras un recuento: con origen (falta) o con destino (sobra).</summary>
    Ajuste,

    /// <summary>De cualquier sitio a cuarentena.</summary>
    Cuarentena,
}

/// <summary>
/// Una línea del libro de stock. Nunca se modifica ni se borra: un error se corrige con otro movimiento que lo
/// explica. La existencia de cada artículo, ubicación y lote es siempre la suma de sus movimientos.
/// </summary>
public sealed class Movimiento
{
    public long Id { get; private set; }
    public DateTimeOffset Momento { get; private set; }
    public TipoDeMovimiento Tipo { get; private set; }
    public Guid ArticuloId { get; private set; }
    public string Lote { get; private set; } = "";
    public DateOnly? Caducidad { get; private set; }

    /// <summary>De dónde sale. Nulo cuando el stock entra en el almacén.</summary>
    public Guid? OrigenId { get; private set; }

    /// <summary>A dónde va. Nulo cuando el stock sale del almacén.</summary>
    public Guid? DestinoId { get; private set; }

    public int Cantidad { get; private set; }

    /// <summary>Quién lo hizo (el usuario de la sesión).</summary>
    public string Usuario { get; private set; } = "";

    /// <summary>El documento que lo explica: REC-000012, PED-000340, CON-000007...</summary>
    public string Documento { get; private set; } = "";

    private Movimiento()
    {
    }

    public static Movimiento Crear(TipoDeMovimiento tipo, Guid articuloId, string lote, DateOnly? caducidad,
        Guid? origenId, Guid? destinoId, int cantidad, string usuario, string documento, DateTimeOffset momento)
    {
        if (cantidad <= 0)
        {
            throw new ReglaIncumplida("cantidad-no-valida", "La cantidad tiene que ser mayor que cero.");
        }
        if (origenId is null && destinoId is null)
        {
            throw new ArgumentException("Un movimiento tiene origen, destino o los dos.");
        }
        if (origenId is not null && origenId == destinoId)
        {
            throw new ReglaIncumplida("mismo-sitio", "El origen y el destino son la misma ubicación.");
        }
        return new Movimiento
        {
            Tipo = tipo,
            ArticuloId = articuloId,
            Lote = lote,
            Caducidad = caducidad,
            OrigenId = origenId,
            DestinoId = destinoId,
            Cantidad = cantidad,
            Usuario = usuario,
            Documento = documento,
            Momento = momento,
        };
    }
}
