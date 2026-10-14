using Microsoft.EntityFrameworkCore;

using Sga.Aplicacion.Abstracciones;
using Sga.Dominio.Almacen;
using Sga.Dominio.Stock;

namespace Sga.Aplicacion.Stock;

/// <summary>
/// Cuando el disponible de un hueco de picking baja de su mínimo, crea una tarea para bajar de la reserva (lo que
/// antes caduca) hasta su máximo. Una sola tarea abierta por hueco.
/// </summary>
public sealed class Reposicion(IContexto contexto, TimeProvider reloj)
{
    public async Task<Tarea?> Revisar(Ubicacion hueco, CancellationToken ct)
    {
        if (hueco is not { Zona: TipoDeZona.Picking, ArticuloFijoId: { } articuloId, Minimo: { } minimo, Maximo: { } maximo }
            || hueco.Bloqueada)
        {
            return null;
        }
        var hay = await contexto.Existencias.Where(e => e.UbicacionId == hueco.Id)
            .SumAsync(e => (int?)(e.Cantidad - e.Reservada), ct) ?? 0;
        var yaViene = await contexto.Tareas.AnyAsync(t => t.Tipo == TipoDeTarea.Reponer && t.DestinoId == hueco.Id &&
            (t.Estado == EstadoDeTarea.Pendiente || t.Estado == EstadoDeTarea.EnCurso), ct)
            || contexto.Tareas.Local.Any(t => t.Tipo == TipoDeTarea.Reponer && t.DestinoId == hueco.Id && t.Abierta);
        if (hay >= minimo || yaViene)
        {
            return null;
        }

        var tiene = await contexto.Existencias.Where(e => e.UbicacionId == hueco.Id).SumAsync(e => (int?)e.Cantidad, ct) ?? 0;
        var cabe = maximo - tiene;
        var bloqueados = contexto.LotesBloqueados.Where(l => l.ArticuloId == articuloId).Select(l => l.Lote);
        var deReserva = await (
                from e in contexto.Existencias
                join u in contexto.Ubicaciones on e.UbicacionId equals u.Id
                where e.ArticuloId == articuloId && u.Zona == TipoDeZona.Reserva && !u.Bloqueada
                      && e.Cantidad - e.Reservada > 0 && !bloqueados.Contains(e.Lote)
                orderby e.Caducidad, u.Orden
                select new { Existencia = e, Ubicacion = u })
            .FirstOrDefaultAsync(ct);
        if (deReserva is null || cabe <= 0)
        {
            return null;
        }
        var cantidad = Math.Min(cabe, deReserva.Existencia.Disponible);
        var tarea = Tarea.Reponer(articuloId, deReserva.Existencia.Lote, deReserva.Existencia.Caducidad, cantidad,
            deReserva.Ubicacion, hueco, reloj.GetUtcNow());
        contexto.Tareas.Add(tarea);
        return tarea;
    }
}
