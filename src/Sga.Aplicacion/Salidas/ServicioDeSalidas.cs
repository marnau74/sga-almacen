using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Sga.Aplicacion.Abstracciones;
using Sga.Aplicacion.Stock;
using Sga.Dominio.Almacen;
using Sga.Dominio.Catalogo;
using Sga.Dominio.Codigos;
using Sga.Dominio.Comun;
using Sga.Dominio.Salidas;
using Sga.Dominio.Stock;

namespace Sga.Aplicacion.Salidas;

public sealed record LineaPedida(Guid ArticuloId, int Cantidad);

public sealed record ResultadoDeAsignacion(string Pedido, EstadoDePedido Estado, int Pedidas, int Asignadas);

/// <summary>Salidas: pedidos, reparto de stock, oleadas, embalaje y expedición.</summary>
public sealed class ServicioDeSalidas(IContexto contexto, Reparto reparto, ServicioDeStock stock, Huecos huecos,
    IOptions<OpcionesDelAlmacen> opciones, IAvisos avisos, TimeProvider reloj)
{
    public async Task<Pedido> Crear(Guid clienteId, DateOnly fechaDeServicio, IReadOnlyList<LineaPedida> lineas,
        CancellationToken ct)
    {
        if (!await contexto.Clientes.AnyAsync(c => c.Id == clienteId, ct))
        {
            throw new NoEncontrado("No existe ese cliente.");
        }
        var articulos = lineas.Select(l => l.ArticuloId).Distinct().ToList();
        if (await contexto.Articulos.CountAsync(a => articulos.Contains(a.Id) && a.Activo, ct) != articulos.Count)
        {
            throw new NoEncontrado("Alguno de los artículos no existe o está dado de baja.");
        }
        var pedido = await contexto.EnTransaccion(async () =>
        {
            var nuevo = Pedido.Crear(await contexto.Siguiente("PED", ct), clienteId, fechaDeServicio,
                lineas.Select(l => (l.ArticuloId, l.Cantidad)), reloj.GetUtcNow());
            contexto.Pedidos.Add(nuevo);
            return nuevo;
        }, ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.PedidosCambiados, pedido.Numero), ct);
        return pedido;
    }

    /// <summary>
    /// Reparte el stock entre los pedidos (los que antes se sirven, primero) y lo reserva. Lo que no hay se queda
    /// sin asignar: se puede volver a intentar cuando entre mercancía.
    /// </summary>
    public async Task<IReadOnlyList<ResultadoDeAsignacion>> Asignar(IReadOnlyCollection<Guid> pedidoIds, CancellationToken ct)
    {
        var resultado = await contexto.EnTransaccion(async () =>
        {
            // Dos repartos del mismo pedido a la vez: el segundo espera y ve lo que ya se ha asignado.
            await contexto.BloquearPedidos(pedidoIds, ct);
            var pedidos = await contexto.Pedidos
                .Where(p => pedidoIds.Contains(p.Id) &&
                            (p.Estado == EstadoDePedido.Pendiente || p.Estado == EstadoDePedido.AsignadoParcial))
                .OrderBy(p => p.FechaDeServicio).ThenBy(p => p.Numero)
                .ToListAsync(ct);
            var clientes = await DiasDeVidaUtil(pedidos.Select(p => p.ClienteId), ct);
            await reparto.Preparar(pedidos.SelectMany(p => p.Lineas).Select(l => l.ArticuloId), ct);
            foreach (var pedido in pedidos)
            {
                foreach (var linea in pedido.Lineas.Where(l => l.Asignada < l.Pedida))
                {
                    reparto.Asignar(pedido, linea, linea.Pedida - linea.Asignada, pedido.FechaDeServicio, clientes[pedido.ClienteId]);
                }
                pedido.TerminarAsignacion();
            }
            return pedidos.Select(p => new ResultadoDeAsignacion(p.Numero, p.Estado, p.Lineas.Sum(l => l.Pedida),
                p.Lineas.Sum(l => l.Asignada))).ToList();
        }, ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.PedidosCambiados), ct);
        return resultado;
    }

    /// <summary>
    /// Lanza una oleada: los pedidos asignados pasan a preparación y sale una tarea de picking por cada reserva,
    /// ordenadas por el recorrido.
    /// </summary>
    public async Task<Oleada> LanzarOleada(IReadOnlyCollection<Guid> pedidoIds, CancellationToken ct)
    {
        var oleada = await contexto.EnTransaccion(async () =>
        {
            var pedidos = await contexto.Pedidos.Where(p => pedidoIds.Contains(p.Id)).ToListAsync(ct);
            if (pedidos.Count == 0 || pedidos.Any(p => p.Estado is not (EstadoDePedido.Asignado or EstadoDePedido.AsignadoParcial)))
            {
                throw new ReglaIncumplida("pedidos-no-asignados", "Solo se lanzan pedidos con stock asignado.");
            }
            var nueva = Oleada.Lanzar(await contexto.Siguiente("OLE", ct), reloj.GetUtcNow());
            contexto.Oleadas.Add(nueva);
            var muelle = await huecos.Playa(TipoDeZona.Expedicion, ct);
            var ids = pedidos.Select(p => p.Id).ToList();
            var reservas = await contexto.Reservas.Where(r => ids.Contains(r.PedidoId) && r.Estado == EstadoDeReserva.Abierta)
                .ToListAsync(ct);
            var ubicaciones = reservas.Select(r => r.UbicacionId).Distinct().ToList();
            var porId = await contexto.Ubicaciones.Where(u => ubicaciones.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
            foreach (var reserva in reservas)
            {
                contexto.Tareas.Add(Tarea.Coger(reserva, porId[reserva.UbicacionId], muelle.Id, nueva.Id, nueva.Numero,
                    reloj.GetUtcNow()));
            }
            foreach (var pedido in pedidos)
            {
                pedido.Lanzar(nueva.Id);
            }
            return nueva;
        }, ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.TareasCambiadas, oleada.Numero), ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.PedidosCambiados), ct);
        return oleada;
    }

    public async Task Cancelar(Guid pedidoId, CancellationToken ct)
    {
        await contexto.EnTransaccion(async () =>
        {
            var pedido = await Cargar(pedidoId, ct);
            if (!pedido.SePuedeCancelar)
            {
                pedido.Cancelar(); // lanza la regla con su mensaje
            }
            var reservas = await contexto.Reservas.Where(r => r.PedidoId == pedidoId && r.Estado == EstadoDeReserva.Abierta)
                .ToListAsync(ct);
            var existencias = await contexto.BloquearExistencias(reservas.Select(r => r.Clave), ct);
            foreach (var reserva in reservas)
            {
                reserva.Liberar(existencias.Single(e => e.Clave == reserva.Clave));
            }
            pedido.Cancelar();
            return true;
        }, ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.PedidosCambiados), ct);
    }

    // Embalaje

    public async Task<Bulto> AbrirBulto(Guid pedidoId, CancellationToken ct)
    {
        return await contexto.EnTransaccion(async () =>
        {
            var pedido = await Cargar(pedidoId, ct);
            var serie = await contexto.SiguienteSscc(ct);
            return pedido.AbrirBulto(Sscc.Generar(opciones.Value.PrefijoGs1, 0, serie));
        }, ct);
    }

    /// <summary>
    /// Mete en el bulto abierto lo escaneado. Si el artículo se ha preparado de un solo lote, no hace falta decirlo;
    /// si de varios, sí (o escanear la etiqueta GS1 que lo trae).
    /// </summary>
    public async Task Meter(Guid pedidoId, string codigo, string? lote, int? cantidad, CancellationToken ct)
    {
        var pedido = await Cargar(pedidoId, ct);
        var lectura = Lectura.Interpretar(codigo);
        var articulo = lectura.Gtin is { } gtin
            ? await contexto.Articulos.SingleOrDefaultAsync(a => a.Ean == gtin, ct)
            : null;
        if (articulo is null)
        {
            throw new ReglaIncumplida("articulo-desconocido", $"«{codigo}» no es un artículo de este pedido.");
        }
        var lotes = pedido.Preparados.Where(p => p.ArticuloId == articulo.Id && p.Embalado < p.Cantidad)
            .Select(p => p.Lote).ToList();
        var elegido = lectura.Lote ?? lote ?? (lotes.Count == 1 ? lotes[0] : null)
                      ?? throw new ReglaIncumplida("indica-el-lote", "Este artículo va de varios lotes: indica cuál.");
        pedido.Meter(articulo.Id, elegido, cantidad ?? lectura.Cantidad ?? 1);
        await contexto.SaveChangesAsync(ct);
    }

    public async Task<Bulto> CerrarBulto(Guid pedidoId, CancellationToken ct)
    {
        var pedido = await Cargar(pedidoId, ct);
        var bulto = pedido.CerrarBulto();
        await contexto.SaveChangesAsync(ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.PedidosCambiados, pedido.Numero), ct);
        return bulto;
    }

    /// <summary>Sale en el camión: el stock deja el muelle y el almacén.</summary>
    public async Task Expedir(Guid pedidoId, CancellationToken ct)
    {
        await contexto.EnTransaccion(async () =>
        {
            var pedido = await Cargar(pedidoId, ct);
            pedido.Expedir(reloj.GetUtcNow());
            var muelle = await huecos.Playa(TipoDeZona.Expedicion, ct);
            foreach (var preparado in pedido.Preparados)
            {
                await stock.Mover(TipoDeMovimiento.Expedicion, preparado.ArticuloId, preparado.Lote, preparado.Caducidad,
                    muelle.Id, null, preparado.Cantidad, pedido.Numero, ct);
            }
            return true;
        }, ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.PedidosCambiados), ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.StockCambiado), ct);
    }

    private async Task<Pedido> Cargar(Guid pedidoId, CancellationToken ct) =>
        await contexto.Pedidos.SingleOrDefaultAsync(p => p.Id == pedidoId, ct)
        ?? throw new NoEncontrado("No existe ese pedido.");

    private async Task<Dictionary<Guid, int>> DiasDeVidaUtil(IEnumerable<Guid> clientes, CancellationToken ct)
    {
        var ids = clientes.Distinct().ToList();
        return await contexto.Clientes.Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.DiasMinimosDeVidaUtil, ct);
    }
}
