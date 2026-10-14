namespace Sga.Dominio.Stock;

/// <summary>
/// Aplica un movimiento a las existencias de su origen y su destino. Es el único sitio donde cambia una cantidad: la
/// aplicación carga (y bloquea) las existencias, llama aquí y guarda el movimiento y las existencias juntos.
/// </summary>
public static class Libro
{
    /// <param name="origen">La existencia de donde sale; obligatoria si el movimiento tiene origen.</param>
    /// <param name="destino">La existencia a donde va (vacía si no había nada); obligatoria si tiene destino.</param>
    /// <param name="reserva">Si lo que sale estaba reservado para un pedido, su reserva.</param>
    public static void Aplicar(Movimiento movimiento, Existencia? origen, Existencia? destino, Reserva? reserva = null)
    {
        if (movimiento.OrigenId is { } origenId)
        {
            Comprobar(origen, movimiento, origenId);
            if (reserva is null)
            {
                origen!.SalirLibre(movimiento.Cantidad);
            }
            else
            {
                if (reserva.Clave != origen!.Clave)
                {
                    throw new ArgumentException("La reserva no es de esta existencia.", nameof(reserva));
                }
                // Antes de tocar nada: si falla a medias, la existencia y la reserva no cuadrarían.
                if (movimiento.Cantidad > reserva.Pendiente)
                {
                    throw new Comun.ReglaIncumplida("reserva-insuficiente",
                        $"Solo quedan {reserva.Pendiente} por coger de esta reserva.");
                }
                origen.SalirReservado(movimiento.Cantidad);
                reserva.Consumir(movimiento.Cantidad);
            }
        }
        else if (reserva is not null)
        {
            throw new ArgumentException("Una entrada no consume reservas.", nameof(reserva));
        }

        if (movimiento.DestinoId is { } destinoId)
        {
            Comprobar(destino, movimiento, destinoId);
            destino!.Entrar(movimiento.Cantidad);
        }
    }

    private static void Comprobar(Existencia? existencia, Movimiento movimiento, Guid ubicacionId)
    {
        if (existencia is null || existencia.Clave != new ClaveDeExistencia(movimiento.ArticuloId, ubicacionId, movimiento.Lote))
        {
            throw new ArgumentException("La existencia no corresponde al movimiento.");
        }
    }
}
