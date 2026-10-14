using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sga.Aplicacion.Abstracciones;
using Sga.Dominio.Almacen;
using Sga.Dominio.Comun;
using Sga.Dominio.Entradas;
using Sga.Dominio.Inventario;
using Sga.Dominio.Salidas;
using Sga.Dominio.Stock;

namespace Sga.Aplicacion.Consultas;

public sealed record ArticuloResumen(Guid Id, string Ean, string Nombre, string? Marca, string? Formato, int UnidadesPorCaja,
    bool ControlDeLote, bool ControlDeCaducidad, int Stock, int Disponible);

public sealed record StockEnUbicacion(string Ubicacion, TipoDeZona Zona, string Lote, DateOnly? Caducidad, int Cantidad,
    int Reservada, bool LoteBloqueado, bool UbicacionBloqueada);

public sealed record UbicacionResumen(Guid Id, string Codigo, TipoDeZona Zona, int Pasillo, int Modulo, int Nivel,
    bool Bloqueada, string? ArticuloFijo, int? Minimo, int? Maximo, int Unidades);

public sealed record TareaParaElTerminal(Guid Id, TipoDeTarea Tipo, EstadoDeTarea Estado, string? Operario, string Articulo,
    string Ean, string Lote, DateOnly? Caducidad, int Cantidad, string Origen, string Destino, string Documento, int Orden);

public sealed record RecepcionResumen(Guid Id, string Numero, string Proveedor, DateOnly FechaPrevista,
    EstadoDeRecepcion Estado, int Previstas, int Recibidas, int Danadas);

public sealed record LineaDeRecepcionDetalle(int Numero, Guid ArticuloId, string Articulo, string Ean, int Prevista,
    int Recibida, int Danada);

public sealed record RecepcionDetalle(RecepcionResumen Resumen, IReadOnlyList<LineaDeRecepcionDetalle> Lineas,
    string? Observaciones);

public sealed record PedidoResumen(Guid Id, string Numero, string Cliente, DateOnly FechaDeServicio, EstadoDePedido Estado,
    int Lineas, int Pedidas, int Asignadas, int Preparadas, string? Oleada);

public sealed record LineaDePedidoDetalle(int Numero, Guid ArticuloId, string Articulo, string Ean, int Pedida, int Asignada,
    int Preparada, int Rotura);

public sealed record PreparadoDetalle(Guid ArticuloId, string Articulo, string Lote, DateOnly? Caducidad, int Cantidad, int Embalado);

public sealed record ContenidoDetalle(string Articulo, string Lote, int Cantidad);

public sealed record BultoDetalle(int Numero, string Sscc, bool Cerrado, IReadOnlyList<ContenidoDetalle> Contenido);

public sealed record PedidoDetalle(PedidoResumen Resumen, IReadOnlyList<LineaDePedidoDetalle> Lineas,
    IReadOnlyList<PreparadoDetalle> Preparados, IReadOnlyList<BultoDetalle> Bultos);

public sealed record LineaDeConteoDetalle(string Ubicacion, string Articulo, string Ean, string Lote, int? Esperada,
    int? Primera, int? Segunda, int? Diferencia);

public sealed record ConteoDetalle(Guid Id, string Numero, EstadoDeConteo Estado, IReadOnlyList<string> Ubicaciones,
    IReadOnlyList<LineaDeConteoDetalle> Lineas, string? AprobadoPor);

public sealed record PasoDeTrazabilidad(DateTimeOffset Momento, TipoDeMovimiento Tipo, string? Origen, string? Destino,
    int Cantidad, string Documento, string Usuario);

public sealed record SalidaDeLote(string Pedido, string Cliente, int Cantidad, DateTimeOffset? Expedido);

public sealed record Trazabilidad(string Articulo, string Ean, string Lote, DateOnly? Caducidad, bool Bloqueado,
    IReadOnlyList<string> Recepciones, IReadOnlyList<SalidaDeLote> Salidas,
    IReadOnlyList<StockEnUbicacion> DondeEsta, IReadOnlyList<PasoDeTrazabilidad> Movimientos);

public sealed record Panel(IReadOnlyDictionary<EstadoDePedido, int> PedidosPorEstado,
    IReadOnlyDictionary<TipoDeTarea, int> TareasPendientes, int LineasPreparadasHoy, IReadOnlyList<int> LineasPorHora,
    int HuecosDeReserva, int HuecosDeReservaOcupados, IReadOnlyList<CaducaPronto> CaducanPronto, IReadOnlyList<Falta> Faltas,
    IReadOnlyList<Rotura> Roturas, int RecepcionesConDiferencias);

public sealed record CaducaPronto(string Articulo, string Lote, DateOnly Caducidad, int Cantidad);

public sealed record Falta(string Ubicacion, string Articulo, int Unidades, DateTimeOffset Momento);

public sealed record Rotura(string Pedido, string Cliente, int Unidades);

public sealed record ConteoResumen(Guid Id, string Numero, EstadoDeConteo Estado, int Ubicaciones, DateTimeOffset Abierto);

/// <summary>Lecturas para la oficina y el terminal. No cambian nada (sin seguimiento de cambios).</summary>
public sealed class Consultas(IContexto contexto, IOptions<OpcionesDelAlmacen> opciones, TimeProvider reloj)
{
    public async Task<IReadOnlyList<ArticuloResumen>> Articulos(string? buscar, int maximo, CancellationToken ct)
    {
        var consulta = contexto.Articulos.AsNoTracking().Where(a => a.Activo);
        if (Dominio.Codigos.Gtin.TryCrear(buscar, out var ean))
        {
            consulta = consulta.Where(a => a.Ean == ean);
        }
        else if (!string.IsNullOrWhiteSpace(buscar))
        {
            consulta = contexto.ArticulosQueContienen(buscar.Trim()).AsNoTracking().Where(a => a.Activo);
        }
        var articulos = await consulta.OrderBy(a => a.Nombre).Take(maximo).ToListAsync(ct);
        var ids = articulos.Select(a => a.Id).ToList();
        var stock = await contexto.Existencias.AsNoTracking().Where(e => ids.Contains(e.ArticuloId))
            .GroupBy(e => e.ArticuloId)
            .Select(g => new { g.Key, Cantidad = g.Sum(e => e.Cantidad), Reservada = g.Sum(e => e.Reservada) })
            .ToDictionaryAsync(x => x.Key, ct);
        return articulos.Select(a =>
        {
            var s = stock.GetValueOrDefault(a.Id);
            return new ArticuloResumen(a.Id, a.Ean.Valor, a.Nombre, a.Marca, a.Formato, a.UnidadesPorCaja, a.ControlDeLote,
                a.ControlDeCaducidad, s?.Cantidad ?? 0, (s?.Cantidad ?? 0) - (s?.Reservada ?? 0));
        }).ToList();
    }

    public async Task<IReadOnlyList<StockEnUbicacion>> StockDe(Guid articuloId, CancellationToken ct)
    {
        var bloqueados = contexto.LotesBloqueados.Where(l => l.ArticuloId == articuloId).Select(l => l.Lote);
        return await (
                from e in contexto.Existencias.AsNoTracking()
                join u in contexto.Ubicaciones on e.UbicacionId equals u.Id
                where e.ArticuloId == articuloId && e.Cantidad > 0
                orderby e.Caducidad, u.Orden
                select new StockEnUbicacion(u.Codigo, u.Zona, e.Lote, e.Caducidad, e.Cantidad, e.Reservada,
                    bloqueados.Contains(e.Lote), u.Bloqueada))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UbicacionResumen>> Ubicaciones(TipoDeZona? zona, CancellationToken ct)
    {
        var consulta = contexto.Ubicaciones.AsNoTracking();
        if (zona is not null)
        {
            consulta = consulta.Where(u => u.Zona == zona);
        }
        return await (
                from u in consulta
                join a in contexto.Articulos on u.ArticuloFijoId equals a.Id into fijos
                from a in fijos.DefaultIfEmpty()
                orderby u.Orden
                select new UbicacionResumen(u.Id, u.Codigo, u.Zona, u.Pasillo, u.Modulo, u.Nivel, u.Bloqueada,
                    a == null ? null : a.Nombre, u.Minimo, u.Maximo,
                    contexto.Existencias.Where(e => e.UbicacionId == u.Id).Sum(e => (int?)e.Cantidad) ?? 0))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Las tareas para el terminal: primero las que ya tiene el operario y luego las libres, por el orden del recorrido.
    /// </summary>
    public async Task<IReadOnlyList<TareaParaElTerminal>> TareasPara(string operario, TipoDeTarea? tipo, CancellationToken ct)
    {
        var consulta = contexto.Tareas.AsNoTracking().Where(t =>
            (t.Estado == EstadoDeTarea.EnCurso && t.Operario == operario) || t.Estado == EstadoDeTarea.Pendiente);
        if (tipo is not null)
        {
            consulta = consulta.Where(t => t.Tipo == tipo);
        }
        return (await (
                from t in consulta
                join a in contexto.Articulos on t.ArticuloId equals a.Id
                join o in contexto.Ubicaciones on t.OrigenId equals o.Id
                join d in contexto.Ubicaciones on t.DestinoId equals d.Id
                orderby t.Estado == EstadoDeTarea.EnCurso ? 0 : 1, t.Orden, t.Creada
                select new { t, a.Nombre, a.Ean, Origen = o.Codigo, Destino = d.Codigo })
            .Take(200)
            .ToListAsync(ct))
            .Select(x => new TareaParaElTerminal(x.t.Id, x.t.Tipo, x.t.Estado, x.t.Operario, x.Nombre, x.Ean.Valor, x.t.Lote,
                x.t.Caducidad, x.t.Cantidad, x.Origen, x.Destino, x.t.Documento, x.t.Orden))
            .ToList();
    }

    public async Task<IReadOnlyList<RecepcionResumen>> Recepciones(EstadoDeRecepcion? estado, CancellationToken ct)
    {
        var consulta = contexto.Recepciones.AsNoTracking();
        if (estado is not null)
        {
            consulta = consulta.Where(r => r.Estado == estado);
        }
        var recepciones = await consulta.OrderByDescending(r => r.FechaPrevista).ThenByDescending(r => r.Numero).Take(200)
            .ToListAsync(ct);
        var proveedores = await contexto.Proveedores.Select(p => new { p.Id, p.Nombre }).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
        return recepciones.Select(r => Resumen(r, proveedores)).ToList();
    }

    public async Task<RecepcionDetalle> Recepcion(Guid id, CancellationToken ct)
    {
        var r = await contexto.Recepciones.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NoEncontrado("No existe esa recepción.");
        var proveedor = await contexto.Proveedores.Where(p => p.Id == r.ProveedorId).Select(p => p.Nombre).SingleAsync(ct);
        var articulos = await NombresDeArticulos(r.Lineas.Select(l => l.ArticuloId), ct);
        return new RecepcionDetalle(Resumen(r, new Dictionary<Guid, string> { [r.ProveedorId] = proveedor }),
            r.Lineas.Select(l => new LineaDeRecepcionDetalle(l.Numero, l.ArticuloId, articulos[l.ArticuloId].Nombre,
                articulos[l.ArticuloId].Ean, l.Prevista, l.Recibida, l.Danada)).ToList(), r.Observaciones);
    }

    public async Task<IReadOnlyList<PedidoResumen>> Pedidos(EstadoDePedido? estado, CancellationToken ct)
    {
        var consulta = contexto.Pedidos.AsNoTracking();
        if (estado is not null)
        {
            consulta = consulta.Where(p => p.Estado == estado);
        }
        var pedidos = await consulta.OrderBy(p => p.FechaDeServicio).ThenBy(p => p.Numero).Take(500).ToListAsync(ct);
        var clientes = await contexto.Clientes.Select(c => new { c.Id, c.Nombre }).ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var oleadas = await contexto.Oleadas.Select(o => new { o.Id, o.Numero }).ToDictionaryAsync(o => o.Id, o => o.Numero, ct);
        return pedidos.Select(p => Resumen(p, clientes, oleadas)).ToList();
    }

    public async Task<PedidoDetalle> Pedido(Guid id, CancellationToken ct)
    {
        var p = await contexto.Pedidos.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NoEncontrado("No existe ese pedido.");
        var cliente = await contexto.Clientes.Where(c => c.Id == p.ClienteId).Select(c => c.Nombre).SingleAsync(ct);
        var oleada = p.OleadaId is null ? null : await contexto.Oleadas.Where(o => o.Id == p.OleadaId).Select(o => o.Numero).SingleAsync(ct);
        var articulos = await NombresDeArticulos(p.Lineas.Select(l => l.ArticuloId), ct);
        return new PedidoDetalle(
            Resumen(p, new Dictionary<Guid, string> { [p.ClienteId] = cliente },
                oleada is null ? [] : new Dictionary<Guid, string> { [p.OleadaId!.Value] = oleada }),
            p.Lineas.Select(l => new LineaDePedidoDetalle(l.Numero, l.ArticuloId, articulos[l.ArticuloId].Nombre,
                articulos[l.ArticuloId].Ean, l.Pedida, l.Asignada, l.Preparada, l.Rotura)).ToList(),
            p.Preparados.Select(x => new PreparadoDetalle(x.ArticuloId, articulos[x.ArticuloId].Nombre, x.Lote, x.Caducidad,
                x.Cantidad, x.Embalado)).ToList(),
            p.Bultos.Select(b => new BultoDetalle(b.Numero, b.Sscc, b.Cerrado,
                b.Contenido.Select(c => new ContenidoDetalle(articulos[c.ArticuloId].Nombre, c.Lote, c.Cantidad)).ToList())).ToList());
    }

    /// <param name="conLoEsperado">Falso para quien cuenta: el conteo es ciego.</param>
    public async Task<ConteoDetalle> Conteo(Guid id, bool conLoEsperado, CancellationToken ct)
    {
        var c = await contexto.Conteos.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NoEncontrado("No existe ese conteo.");
        var ids = c.Ubicaciones.ToList();
        var codigos = await contexto.Ubicaciones.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Codigo, ct);
        var articulos = await NombresDeArticulos(c.Lineas.Select(l => l.ArticuloId), ct);
        // A quien cuenta tampoco se le enseña qué artículos se esperan: solo lo que ya ha contado él.
        var lineas = c.Lineas.Where(l => conLoEsperado || l.Primera is not null || l.Segunda is not null).Select(l =>
            new LineaDeConteoDetalle(codigos[l.UbicacionId], articulos[l.ArticuloId].Nombre, articulos[l.ArticuloId].Ean, l.Lote,
                conLoEsperado ? l.Esperada : null, l.Primera, l.Segunda, conLoEsperado ? l.Diferencia : null)).ToList();
        return new ConteoDetalle(c.Id, c.Numero, c.Estado, ids.Select(i => codigos[i]).ToList(), lineas, c.AprobadoPor);
    }

    public async Task<IReadOnlyList<ConteoResumen>> Conteos(CancellationToken ct) =>
        (await contexto.Conteos.AsNoTracking().OrderByDescending(c => c.Abierto).Take(100).ToListAsync(ct))
        .Select(c => new ConteoResumen(c.Id, c.Numero, c.Estado, c.Ubicaciones.Count, c.Abierto)).ToList();

    /// <summary>Todo lo de un lote: de dónde vino, por dónde ha pasado, a quién se ha servido y dónde queda.</summary>
    public async Task<Trazabilidad> Trazabilidad(Guid articuloId, string lote, CancellationToken ct)
    {
        var articulo = await contexto.Articulos.AsNoTracking().SingleOrDefaultAsync(a => a.Id == articuloId, ct)
                       ?? throw new NoEncontrado("No existe ese artículo.");
        var codigos = await contexto.Ubicaciones.ToDictionaryAsync(u => u.Id, u => u.Codigo, ct);
        var movimientos = await contexto.Movimientos.AsNoTracking()
            .Where(m => m.ArticuloId == articuloId && m.Lote == lote).OrderBy(m => m.Momento).ThenBy(m => m.Id).ToListAsync(ct);
        var pedidos = await contexto.Pedidos.AsNoTracking()
            .Where(p => p.Preparados.Any(x => x.ArticuloId == articuloId && x.Lote == lote))
            .ToListAsync(ct);
        var clientes = await contexto.Clientes.ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var recepciones = await contexto.Recepciones.AsNoTracking()
            .Where(r => r.Descargas.Any(d => d.ArticuloId == articuloId && d.Lote == lote)).Select(r => r.Numero).ToListAsync(ct);
        return new Trazabilidad(articulo.Nombre, articulo.Ean.Valor, lote,
            movimientos.FirstOrDefault()?.Caducidad,
            await contexto.LotesBloqueados.AnyAsync(l => l.ArticuloId == articuloId && l.Lote == lote, ct),
            recepciones,
            pedidos.Select(p => new SalidaDeLote(p.Numero, clientes[p.ClienteId],
                p.Preparados.Where(x => x.ArticuloId == articuloId && x.Lote == lote).Sum(x => x.Cantidad), p.Expedido)).ToList(),
            (await StockDe(articuloId, ct)).Where(s => s.Lote == lote).ToList(),
            movimientos.Select(m => new PasoDeTrazabilidad(m.Momento, m.Tipo,
                m.OrigenId is null ? null : codigos[m.OrigenId.Value], m.DestinoId is null ? null : codigos[m.DestinoId.Value],
                m.Cantidad, m.Documento, m.Usuario)).ToList());
    }

    public async Task<Panel> Panel(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var hoy = new DateTimeOffset(ahora.UtcDateTime.Date, TimeSpan.Zero);
        var pedidos = await contexto.Pedidos.GroupBy(p => p.Estado).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var tareas = await contexto.Tareas.Where(t => t.Estado == EstadoDeTarea.Pendiente || t.Estado == EstadoDeTarea.EnCurso)
            .GroupBy(t => t.Tipo).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var picking = await contexto.Movimientos.Where(m => m.Tipo == TipoDeMovimiento.Picking && m.Momento >= hoy)
            .Select(m => m.Momento).ToListAsync(ct);
        var porHora = Enumerable.Range(0, 24).Select(h => picking.Count(m => m.UtcDateTime.Hour == h)).ToList();
        var reserva = contexto.Ubicaciones.Where(u => u.Zona == TipoDeZona.Reserva);
        var huecos = await reserva.CountAsync(ct);
        var ocupados = await reserva.CountAsync(u => contexto.Existencias.Any(e => e.UbicacionId == u.Id && e.Cantidad > 0), ct);
        var limite = DateOnly.FromDateTime(ahora.UtcDateTime).AddDays(opciones.Value.DiasDeAvisoDeCaducidad);
        var caducan = await (
                from e in contexto.Existencias
                join a in contexto.Articulos on e.ArticuloId equals a.Id
                where e.Cantidad > 0 && e.Caducidad != null && e.Caducidad <= limite
                group e by new { a.Nombre, e.Lote, e.Caducidad } into g
                orderby g.Key.Caducidad
                select new { g.Key.Nombre, g.Key.Lote, Caducidad = g.Key.Caducidad!.Value, Cantidad = g.Sum(e => e.Cantidad) })
            .Take(20).ToListAsync(ct);
        var desde = ahora.AddDays(-1);
        var faltas = await (
                from t in contexto.Tareas
                join u in contexto.Ubicaciones on t.OrigenId equals u.Id
                join a in contexto.Articulos on t.ArticuloId equals a.Id
                where t.Estado == EstadoDeTarea.ConFalta && t.Terminada >= desde
                orderby t.Terminada descending
                select new { u.Codigo, a.Nombre, Falta = t.Cantidad - t.Hecho, t.Terminada })
            .Take(20).ToListAsync(ct);
        var preparados = await contexto.Pedidos.AsNoTracking()
            .Where(p => p.Estado == EstadoDePedido.Preparado || p.Estado == EstadoDePedido.Embalado ||
                        (p.Estado == EstadoDePedido.Expedido && p.Expedido >= desde))
            .ToListAsync(ct);
        var clientes = await contexto.Clientes.ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        return new Panel(pedidos, tareas, picking.Count, porHora, huecos, ocupados,
            caducan.Select(c => new CaducaPronto(c.Nombre, c.Lote, c.Caducidad, c.Cantidad)).ToList(),
            faltas.Select(f => new Falta(f.Codigo, f.Nombre, f.Falta, f.Terminada!.Value)).ToList(),
            preparados.Where(p => p.Lineas.Any(l => l.Rotura > 0))
                .Select(p => new Rotura(p.Numero, clientes[p.ClienteId], p.Lineas.Sum(l => l.Rotura))).ToList(),
            await contexto.Recepciones.CountAsync(r => r.Estado == EstadoDeRecepcion.ConDiferencias, ct));
    }

    private static RecepcionResumen Resumen(Recepcion r, Dictionary<Guid, string> proveedores) =>
        new(r.Id, r.Numero, proveedores[r.ProveedorId], r.FechaPrevista, r.Estado, r.Lineas.Sum(l => l.Prevista),
            r.Lineas.Sum(l => l.Recibida), r.Lineas.Sum(l => l.Danada));

    private static PedidoResumen Resumen(Pedido p, Dictionary<Guid, string> clientes, Dictionary<Guid, string> oleadas) =>
        new(p.Id, p.Numero, clientes[p.ClienteId], p.FechaDeServicio, p.Estado, p.Lineas.Count, p.Lineas.Sum(l => l.Pedida),
            p.Lineas.Sum(l => l.Asignada), p.Lineas.Sum(l => l.Preparada),
            p.OleadaId is { } o ? oleadas.GetValueOrDefault(o) : null);

    private async Task<Dictionary<Guid, (string Nombre, string Ean)>> NombresDeArticulos(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return (await contexto.Articulos.AsNoTracking().Where(a => lista.Contains(a.Id)).ToListAsync(ct))
            .ToDictionary(a => a.Id, a => (a.Nombre, a.Ean.Valor));
    }
}
