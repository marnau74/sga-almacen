using Sga.Dominio.Comun;

namespace Sga.Dominio.Stock;

/// <summary>
/// Lo que hay de un artículo y un lote en una ubicación. Solo cambia a través de <see cref="Movimiento"/> (cantidad)
/// y de <see cref="Reserva"/> (reservada); nunca se escribe a mano.
/// </summary>
public sealed class Existencia
{
    public Guid ArticuloId { get; private set; }
    public Guid UbicacionId { get; private set; }

    /// <summary>Vacío si el artículo no lleva lote.</summary>
    public string Lote { get; private set; } = "";

    public DateOnly? Caducidad { get; private set; }
    public int Cantidad { get; private set; }

    /// <summary>Lo que ya está prometido a pedidos y aún no se ha cogido.</summary>
    public int Reservada { get; private set; }

    public int Disponible => Cantidad - Reservada;

    private Existencia()
    {
    }

    public static Existencia Vacia(Guid articuloId, Guid ubicacionId, string lote, DateOnly? caducidad) => new()
    {
        ArticuloId = articuloId,
        UbicacionId = ubicacionId,
        Lote = lote,
        Caducidad = caducidad,
    };

    public ClaveDeExistencia Clave => new(ArticuloId, UbicacionId, Lote);

    internal void Entrar(int cantidad) => Cantidad += cantidad;

    /// <summary>Sale stock libre (un traslado, un ajuste a la baja): no puede tocar lo reservado.</summary>
    internal void SalirLibre(int cantidad)
    {
        if (cantidad > Disponible)
        {
            throw new ReglaIncumplida("sin-stock", $"Solo hay {Disponible} disponibles en esta ubicación.");
        }
        Cantidad -= cantidad;
    }

    /// <summary>Sale stock que estaba reservado (el picking de un pedido).</summary>
    internal void SalirReservado(int cantidad)
    {
        if (cantidad > Reservada || cantidad > Cantidad)
        {
            throw new ReglaIncumplida("reserva-insuficiente", "No hay tanto reservado en esta ubicación.");
        }
        Cantidad -= cantidad;
        Reservada -= cantidad;
    }

    internal void Reservar(int cantidad)
    {
        if (cantidad <= 0 || cantidad > Disponible)
        {
            throw new ReglaIncumplida("sin-stock", $"Solo hay {Disponible} disponibles para reservar.");
        }
        Reservada += cantidad;
    }

    internal void Liberar(int cantidad)
    {
        if (cantidad <= 0 || cantidad > Reservada)
        {
            throw new ReglaIncumplida("reserva-insuficiente", "No hay tanto reservado que liberar.");
        }
        Reservada -= cantidad;
    }

    public bool EstaVacia => Cantidad == 0 && Reservada == 0;
}

public readonly record struct ClaveDeExistencia(Guid ArticuloId, Guid UbicacionId, string Lote);
