using Microsoft.EntityFrameworkCore;

using Sga.Aplicacion.Abstracciones;
using Sga.Aplicacion.Maestros;
using Sga.Dominio.Almacen;
using Sga.Dominio.Comun;

namespace Sga.Aplicacion.Stock;

/// <summary>Consultas del mapa que usan varios servicios.</summary>
public sealed class Huecos(IContexto contexto)
{
    public async Task<Ubicacion> Playa(TipoDeZona zona, CancellationToken ct)
    {
        var codigo = ServicioDeMaestros.Playas[zona];
        return await contexto.Ubicaciones.SingleOrDefaultAsync(u => u.Codigo == codigo, ct)
               ?? throw new InvalidOperationException($"Falta la playa {codigo}: el almacén no está inicializado.");
    }

    public async Task<Ubicacion> PorCodigo(string codigo, CancellationToken ct)
    {
        var limpio = codigo.Trim().ToUpperInvariant();
        return await contexto.Ubicaciones.SingleOrDefaultAsync(u => u.Codigo == limpio, ct)
               ?? throw new ReglaIncumplida("ubicacion-no-existe", $"No hay ninguna ubicación «{codigo}».");
    }

    /// <summary>
    /// Los huecos donde podría ir un artículo, con lo que tienen y lo que ya va de camino (tareas de ubicar o reponer
    /// abiertas): así dos palés no acaban sugeridos al mismo hueco vacío.
    /// </summary>
    public async Task<IReadOnlyList<HuecoOcupado>> ParaUbicar(Guid articuloId, CancellationToken ct)
    {
        var huecos = await contexto.Ubicaciones
            .Where(u => u.Zona == TipoDeZona.Reserva || (u.Zona == TipoDeZona.Picking && u.ArticuloFijoId == articuloId))
            .ToListAsync(ct);
        var ids = huecos.Select(h => h.Id).ToList();
        var stock = await contexto.Existencias.Where(e => ids.Contains(e.UbicacionId) && e.Cantidad > 0)
            .GroupBy(e => e.UbicacionId)
            .Select(g => new { g.Key, Unidades = g.Sum(e => e.Cantidad) })
            .ToDictionaryAsync(x => x.Key, x => x.Unidades, ct);
        var deCamino = await contexto.Tareas
            .Where(t => (t.Estado == EstadoDeTarea.Pendiente || t.Estado == EstadoDeTarea.EnCurso) &&
                        t.Tipo != TipoDeTarea.Picking && ids.Contains(t.DestinoId))
            .GroupBy(t => t.DestinoId)
            .Select(g => new { g.Key, Unidades = g.Sum(t => t.Cantidad) })
            .ToDictionaryAsync(x => x.Key, x => x.Unidades, ct);
        return huecos
            .Select(h => new HuecoOcupado(h, stock.GetValueOrDefault(h.Id) + deCamino.GetValueOrDefault(h.Id)))
            .ToList();
    }
}
