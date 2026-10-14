using Sga.Dominio.Comun;

namespace Sga.Dominio.Stock;

public enum EstadoDeReserva
{
    Abierta,
    Consumida,
    Liberada,
}

/// <summary>
/// Stock de una ubicación y un lote prometido a una línea de pedido. Se consume al cogerlo (picking) y se libera si
/// el pedido se cancela o falta mercancía en el hueco.
/// </summary>
public sealed class Reserva
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid PedidoId { get; private set; }
    public int Linea { get; private set; }
    public Guid ArticuloId { get; private set; }
    public Guid UbicacionId { get; private set; }
    public string Lote { get; private set; } = "";
    public DateOnly? Caducidad { get; private set; }
    public int Cantidad { get; private set; }
    public int Consumida { get; private set; }
    public EstadoDeReserva Estado { get; private set; }

    public int Pendiente => Estado == EstadoDeReserva.Abierta ? Cantidad - Consumida : 0;

    private Reserva()
    {
    }

    /// <summary>Reserva <paramref name="cantidad"/> de la existencia para una línea de pedido.</summary>
    public static Reserva Sobre(Existencia existencia, Guid pedidoId, int linea, int cantidad)
    {
        existencia.Reservar(cantidad);
        return new Reserva
        {
            PedidoId = pedidoId,
            Linea = linea,
            ArticuloId = existencia.ArticuloId,
            UbicacionId = existencia.UbicacionId,
            Lote = existencia.Lote,
            Caducidad = existencia.Caducidad,
            Cantidad = cantidad,
        };
    }

    public ClaveDeExistencia Clave => new(ArticuloId, UbicacionId, Lote);

    /// <summary>Se ha cogido <paramref name="cantidad"/>. La existencia ya ha descontado lo reservado.</summary>
    internal void Consumir(int cantidad)
    {
        if (cantidad <= 0 || cantidad > Pendiente)
        {
            throw new ReglaIncumplida("reserva-insuficiente", $"Solo quedan {Pendiente} por coger de esta reserva.");
        }
        Consumida += cantidad;
        if (Consumida == Cantidad)
        {
            Estado = EstadoDeReserva.Consumida;
        }
    }

    /// <summary>Devuelve a disponible lo que no se va a coger.</summary>
    public int Liberar(Existencia existencia)
    {
        if (existencia.Clave != Clave)
        {
            throw new ArgumentException("La existencia no es la de la reserva.", nameof(existencia));
        }
        var resto = Pendiente;
        if (resto > 0)
        {
            existencia.Liberar(resto);
        }
        Estado = Consumida > 0 ? EstadoDeReserva.Consumida : EstadoDeReserva.Liberada;
        return resto;
    }
}

/// <summary>Un lote que no se puede servir (retirada, problema de calidad).</summary>
public sealed class LoteBloqueado
{
    public Guid ArticuloId { get; private set; }
    public string Lote { get; private set; } = "";
    public string Motivo { get; private set; } = "";
    public DateTimeOffset Desde { get; private set; }

    private LoteBloqueado()
    {
    }

    public static LoteBloqueado Crear(Guid articuloId, string lote, string motivo, DateTimeOffset desde) =>
        string.IsNullOrWhiteSpace(lote)
            ? throw new ReglaIncumplida("lote-obligatorio", "Se bloquea un lote concreto.")
            : new LoteBloqueado { ArticuloId = articuloId, Lote = lote, Motivo = motivo.Trim(), Desde = desde };
}
