using Microsoft.EntityFrameworkCore;

using Sga.Aplicacion.Abstracciones;
using Sga.Aplicacion.Salidas;
using Sga.Aplicacion.Stock;
using Sga.Dominio.Almacen;
using Sga.Dominio.Codigos;
using Sga.Dominio.Comun;
using Sga.Dominio.Salidas;
using Sga.Dominio.Stock;

namespace Sga.Aplicacion.Tareas;

/// <summary>
/// Lo que el operario escanea para confirmar una tarea. El origen se comprueba en picking y reposición (que está
/// delante del hueco correcto); el destino, al ubicar y reponer.
/// </summary>
public sealed record Confirmacion(string? CodigoDeOrigen, string CodigoDeArticulo, int Cantidad, string? CodigoDeDestino);

/// <summary>El trabajo de los operarios en el terminal.</summary>
public sealed class ServicioDeTareas(IContexto contexto, ServicioDeStock stock, Reparto reparto, Reposicion reposicion,
    Huecos huecos, IUsuarioActual usuario, IAvisos avisos, TimeProvider reloj)
{
    public async Task<Tarea> Tomar(Guid tareaId, CancellationToken ct)
    {
        var tarea = await Cargar(tareaId, ct);
        tarea.Tomar(usuario.Nombre);
        await contexto.SaveChangesAsync(ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.TareasCambiadas), ct);
        return tarea;
    }

    public async Task Soltar(Guid tareaId, CancellationToken ct)
    {
        var tarea = await Cargar(tareaId, ct);
        tarea.Soltar(usuario.Nombre);
        await contexto.SaveChangesAsync(ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.TareasCambiadas), ct);
    }

    public async Task<Tarea> Confirmar(Guid tareaId, Confirmacion confirmacion, CancellationToken ct)
    {
        var tarea = await contexto.EnTransaccion(async () =>
        {
            var tarea = await Cargar(tareaId, ct);
            var origen = await contexto.Ubicaciones.SingleAsync(u => u.Id == tarea.OrigenId, ct);
            if (tarea.Tipo != TipoDeTarea.Ubicar)
            {
                ComprobarUbicacion(confirmacion.CodigoDeOrigen, origen, "origen");
            }
            if (confirmacion.Cantidad > 0)
            {
                await ComprobarArticulo(tarea, confirmacion.CodigoDeArticulo, ct);
            }

            switch (tarea.Tipo)
            {
                case TipoDeTarea.Ubicar:
                    await Ubicar(tarea, confirmacion, ct);
                    break;
                case TipoDeTarea.Reponer:
                    var picking = await contexto.Ubicaciones.SingleAsync(u => u.Id == tarea.DestinoId, ct);
                    ComprobarUbicacion(confirmacion.CodigoDeDestino, picking, "destino");
                    tarea.Terminar(usuario.Nombre, confirmacion.Cantidad, picking.Id, reloj.GetUtcNow());
                    await stock.Mover(TipoDeMovimiento.Reposicion, tarea.ArticuloId, tarea.Lote, tarea.Caducidad, origen.Id,
                        picking.Id, tarea.Cantidad, tarea.Documento, ct);
                    break;
                case TipoDeTarea.Picking:
                    await Coger(tarea, origen, confirmacion.Cantidad, ct);
                    break;
            }
            return tarea;
        }, ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.TareasCambiadas), ct);
        if (tarea.Tipo == TipoDeTarea.Picking)
        {
            await avisos.Publicar(new Aviso(TipoDeAviso.PedidosCambiados), ct);
        }
        return tarea;
    }

    private async Task Ubicar(Tarea tarea, Confirmacion confirmacion, CancellationToken ct)
    {
        var destino = await huecos.PorCodigo(confirmacion.CodigoDeDestino
                                             ?? throw new ReglaIncumplida("escanea-el-destino", "Escanea el hueco donde lo dejas."), ct);
        if (destino.Id != tarea.DestinoId)
        {
            await ComprobarOtroHueco(tarea, destino, ct);
        }
        tarea.Terminar(usuario.Nombre, confirmacion.Cantidad, destino.Id, reloj.GetUtcNow());
        await stock.Mover(TipoDeMovimiento.Ubicacion, tarea.ArticuloId, tarea.Lote, tarea.Caducidad, tarea.OrigenId,
            destino.Id, tarea.Cantidad, tarea.Documento, ct);
    }

    /// <summary>
    /// El operario deja la mercancía en un hueco distinto del sugerido (el sugerido estaba ocupado, por ejemplo). Vale
    /// si es el picking de ese artículo o un hueco de reserva vacío o con ese mismo artículo y lote.
    /// </summary>
    private async Task ComprobarOtroHueco(Tarea tarea, Ubicacion destino, CancellationToken ct)
    {
        var valido = destino switch
        {
            { Bloqueada: true } => false,
            { Zona: TipoDeZona.Picking } => destino.ArticuloFijoId == tarea.ArticuloId,
            { Zona: TipoDeZona.Reserva } => !await contexto.Existencias.AnyAsync(e => e.UbicacionId == destino.Id &&
                e.Cantidad > 0 && (e.ArticuloId != tarea.ArticuloId || e.Lote != tarea.Lote), ct),
            _ => false,
        };
        if (!valido)
        {
            throw new ReglaIncumplida("destino-no-valido", $"No se puede dejar aquí: {destino.Codigo} es de otra cosa.");
        }
    }

    private async Task Coger(Tarea tarea, Ubicacion hueco, int cantidad, CancellationToken ct)
    {
        var reserva = await contexto.Reservas.SingleAsync(r => r.Id == tarea.ReservaId, ct);
        var pedido = await contexto.Pedidos.SingleAsync(p => p.Id == tarea.PedidoId, ct);
        tarea.Terminar(usuario.Nombre, cantidad, tarea.DestinoId, reloj.GetUtcNow());
        if (cantidad > 0)
        {
            await stock.Mover(TipoDeMovimiento.Picking, tarea.ArticuloId, tarea.Lote, tarea.Caducidad, hueco.Id,
                tarea.DestinoId, cantidad, pedido.Numero, ct, reserva);
            pedido.Cogido(reserva.Linea, tarea.Lote, tarea.Caducidad, cantidad);
        }

        var falta = tarea.Cantidad - cantidad;
        if (falta > 0)
        {
            // En el hueco no había lo que decía el sistema: lo que falta se libera y se busca en otro sitio.
            var existencia = (await contexto.BloquearExistencias([reserva.Clave], ct)).Single();
            reserva.Liberar(existencia);
            pedido.Falta(reserva.Linea, falta);
            await Reasignar(pedido, reserva.Linea, falta, hueco, tarea, ct);
        }

        // Lo de arriba se guarda antes de mirar si hay que reponer o si el pedido está terminado.
        await contexto.SaveChangesAsync(ct);
        await reposicion.Revisar(hueco, ct);
        var quedan = await contexto.Tareas.AnyAsync(t => t.PedidoId == pedido.Id && t.Tipo == TipoDeTarea.Picking &&
            (t.Estado == EstadoDeTarea.Pendiente || t.Estado == EstadoDeTarea.EnCurso), ct);
        if (!quedan)
        {
            pedido.TerminarPreparacion();
            var oleadaAbierta = await contexto.Tareas.AnyAsync(t => t.OleadaId == tarea.OleadaId &&
                (t.Estado == EstadoDeTarea.Pendiente || t.Estado == EstadoDeTarea.EnCurso), ct);
            if (!oleadaAbierta && await contexto.Oleadas.SingleOrDefaultAsync(o => o.Id == tarea.OleadaId, ct) is { } oleada)
            {
                oleada.Terminar(reloj.GetUtcNow());
            }
        }
    }

    private async Task Reasignar(Pedido pedido, int linea, int falta, Ubicacion huecoConFalta, Tarea tarea, CancellationToken ct)
    {
        var lineaDePedido = pedido.Linea(linea);
        var dias = await contexto.Clientes.Where(c => c.Id == pedido.ClienteId).Select(c => c.DiasMinimosDeVidaUtil)
            .SingleAsync(ct);
        await reparto.Preparar([lineaDePedido.ArticuloId], ct);
        var muelle = tarea.DestinoId;
        foreach (var nueva in reparto.Asignar(pedido, lineaDePedido, falta, pedido.FechaDeServicio, dias,
                     new HashSet<Guid> { huecoConFalta.Id }))
        {
            contexto.Tareas.Add(Tarea.Coger(nueva, reparto.UbicacionDe(nueva), muelle, tarea.OleadaId!.Value, tarea.Documento,
                reloj.GetUtcNow()));
        }
    }

    private async Task ComprobarArticulo(Tarea tarea, string codigo, CancellationToken ct)
    {
        var lectura = Lectura.Interpretar(codigo);
        var ean = await contexto.Articulos.Where(a => a.Id == tarea.ArticuloId).Select(a => a.Ean).SingleAsync(ct);
        if (lectura.Gtin != ean)
        {
            throw new ReglaIncumplida("articulo-equivocado", "Ese no es el artículo de la tarea.");
        }
        if (lectura.Lote is { } lote && tarea.Lote.Length > 0 && lote != tarea.Lote)
        {
            throw new ReglaIncumplida("lote-equivocado", $"Ese es el lote {lote}; la tarea es del lote {tarea.Lote}.");
        }
    }

    private static void ComprobarUbicacion(string? codigo, Ubicacion esperada, string cual)
    {
        if (!string.Equals(codigo?.Trim(), esperada.Codigo, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReglaIncumplida($"{cual}-equivocado", $"Ese no es el {cual}: tienes que estar en {esperada.Codigo}.");
        }
    }

    private async Task<Tarea> Cargar(Guid id, CancellationToken ct) =>
        await contexto.Tareas.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new NoEncontrado("No existe esa tarea.");
}
