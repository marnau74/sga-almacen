using Microsoft.EntityFrameworkCore;

using Sga.Aplicacion.Abstracciones;
using Sga.Dominio.Almacen;
using Sga.Dominio.Salidas;
using Sga.Dominio.Stock;

namespace Sga.Aplicacion.Salidas;

/// <summary>
/// Reparte stock entre líneas de pedido con FEFO y lo reserva. Bloquea antes las existencias de los artículos, así
/// que dos repartos a la vez se esperan el uno al otro en vez de prometer las mismas unidades.
/// </summary>
public sealed class Reparto(IContexto contexto)
{
    private Dictionary<Guid, List<Existencia>> _existencias = [];
    private Dictionary<Guid, Ubicacion> _ubicaciones = [];
    private HashSet<(Guid, string)> _lotesBloqueados = [];

    /// <summary>Bloquea y carga lo que hace falta para repartir estos artículos.</summary>
    public async Task Preparar(IEnumerable<Guid> articulos, CancellationToken ct)
    {
        var lista = articulos.Distinct().ToList();
        var existencias = await contexto.BloquearExistenciasDe(lista, ct);
        _existencias = existencias.GroupBy(e => e.ArticuloId).ToDictionary(g => g.Key, g => g.ToList());
        var ubicaciones = existencias.Select(e => e.UbicacionId).Distinct().ToList();
        _ubicaciones = await contexto.Ubicaciones.Where(u => ubicaciones.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        _lotesBloqueados = (await contexto.LotesBloqueados.Where(l => lista.Contains(l.ArticuloId))
                .Select(l => new { l.ArticuloId, l.Lote }).ToListAsync(ct))
            .Select(l => (l.ArticuloId, l.Lote)).ToHashSet();
    }

    /// <summary>
    /// Reserva para la línea lo que pueda (hasta <paramref name="cantidad"/>) y lo anota en el pedido.
    /// </summary>
    /// <param name="excepto">Ubicaciones que no cuentan (la del hueco donde acaba de faltar mercancía).</param>
    public IReadOnlyList<Reserva> Asignar(Pedido pedido, LineaDePedido linea, int cantidad, DateOnly fechaDeServicio,
        int diasMinimosDeVidaUtil, ISet<Guid>? excepto = null)
    {
        var candidatas = _existencias.GetValueOrDefault(linea.ArticuloId, [])
            .Where(e => excepto is null || !excepto.Contains(e.UbicacionId))
            .Select(e =>
            {
                var u = _ubicaciones[e.UbicacionId];
                return new Candidata(e, u.Zona, u.Orden, u.Bloqueada, _lotesBloqueados.Contains((e.ArticuloId, e.Lote)));
            });
        var reservas = new List<Reserva>();
        foreach (var asignada in AsignacionFefo.Asignar(cantidad, candidatas, fechaDeServicio, diasMinimosDeVidaUtil))
        {
            var reserva = Reserva.Sobre(asignada.Existencia, pedido.Id, linea.Numero, asignada.Cantidad);
            pedido.Asignado(linea.Numero, asignada.Cantidad);
            contexto.Reservas.Add(reserva);
            reservas.Add(reserva);
        }
        return reservas;
    }

    public Ubicacion UbicacionDe(Reserva reserva) => _ubicaciones[reserva.UbicacionId];
}
