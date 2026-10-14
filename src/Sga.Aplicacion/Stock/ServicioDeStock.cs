using Microsoft.EntityFrameworkCore;

using Sga.Aplicacion.Abstracciones;
using Sga.Dominio.Stock;

namespace Sga.Aplicacion.Stock;

/// <summary>
/// Mueve stock: bloquea las existencias del origen y el destino, aplica el movimiento con las reglas del libro y lo
/// anota. Tiene que llamarse dentro de una transacción (<see cref="IContexto.EnTransaccion{T}"/>).
/// </summary>
public sealed class ServicioDeStock(IContexto contexto, IUsuarioActual usuario, TimeProvider reloj)
{
    public async Task<Movimiento> Mover(TipoDeMovimiento tipo, Guid articuloId, string lote, DateOnly? caducidad,
        Guid? origenId, Guid? destinoId, int cantidad, string documento, CancellationToken ct, Reserva? reserva = null)
    {
        var movimiento = Movimiento.Crear(tipo, articuloId, lote, caducidad, origenId, destinoId, cantidad,
            usuario.Nombre, documento, reloj.GetUtcNow());

        var claves = new List<ClaveDeExistencia>();
        if (origenId is { } o)
        {
            claves.Add(new ClaveDeExistencia(articuloId, o, lote));
        }
        if (destinoId is { } d)
        {
            claves.Add(new ClaveDeExistencia(articuloId, d, lote));
        }
        if (destinoId is { } destinoAsegurado)
        {
            // El destino puede no tener aún nada de este artículo y lote: se crea antes de bloquear para que dos
            // movimientos a la vez hacia el mismo sitio no intenten crearla los dos.
            var caducidadDeDestino = caducidad;
            if (caducidadDeDestino is null && origenId is { } desde)
            {
                caducidadDeDestino = await contexto.Existencias.Where(e => e.ArticuloId == articuloId && e.UbicacionId == desde && e.Lote == lote)
                    .Select(e => e.Caducidad).FirstOrDefaultAsync(ct);
            }
            await contexto.AsegurarExistencia(new ClaveDeExistencia(articuloId, destinoAsegurado, lote), caducidadDeDestino, ct);
        }
        var bloqueadas = await contexto.BloquearExistencias(claves, ct);

        var origen = origenId is null ? null : Buscar(bloqueadas, articuloId, origenId.Value, lote)
            ?? throw new Dominio.Comun.ReglaIncumplida("sin-stock", "No hay nada de ese artículo y lote en el origen.");
        Existencia? destino = null;
        if (destinoId is not null)
        {
            destino = Buscar(bloqueadas, articuloId, destinoId.Value, lote)
                      ?? throw new InvalidOperationException("La existencia del destino tendría que estar creada.");
        }

        Libro.Aplicar(movimiento, origen, destino, reserva);
        contexto.Movimientos.Add(movimiento);
        return movimiento;
    }

    private static Existencia? Buscar(IEnumerable<Existencia> existencias, Guid articuloId, Guid ubicacionId, string lote) =>
        existencias.FirstOrDefault(e => e.ArticuloId == articuloId && e.UbicacionId == ubicacionId && e.Lote == lote);
}
