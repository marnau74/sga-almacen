using Microsoft.EntityFrameworkCore;
using Sga.Aplicacion.Abstracciones;
using Sga.Aplicacion.Stock;
using Sga.Dominio.Almacen;
using Sga.Dominio.Codigos;
using Sga.Dominio.Comun;
using Sga.Dominio.Inventario;
using Sga.Dominio.Stock;

namespace Sga.Aplicacion.Inventario;

public sealed record Contado(string CodigoDeUbicacion, string CodigoDeArticulo, string? Lote, DateOnly? Caducidad, int Cantidad);

/// <summary>Inventario cíclico: conteo ciego, recuento de lo que no cuadra y ajuste aprobado.</summary>
public sealed class ServicioDeInventario(IContexto contexto, ServicioDeStock stock, Huecos huecos, IUsuarioActual usuario,
    IAvisos avisos, TimeProvider reloj)
{
    /// <summary>
    /// Abre un conteo de unas ubicaciones y las bloquea. No se puede contar donde hay algo a medias (reservas para
    /// pedidos o tareas abiertas): lo que se cuenta tiene que estar quieto.
    /// </summary>
    public async Task<Conteo> Abrir(IReadOnlyCollection<string> codigos, CancellationToken ct)
    {
        var conteo = await contexto.EnTransaccion(async () =>
        {
            var limpios = codigos.Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList();
            var ubicaciones = await contexto.Ubicaciones.Where(u => limpios.Contains(u.Codigo)).ToListAsync(ct);
            if (ubicaciones.Count != limpios.Count)
            {
                throw new ReglaIncumplida("ubicacion-no-existe", "Alguna de esas ubicaciones no existe.");
            }
            if (ubicaciones.Any(u => u.Zona is not (TipoDeZona.Picking or TipoDeZona.Reserva) || u.Bloqueada))
            {
                throw new ReglaIncumplida("no-se-puede-contar", "Solo se cuentan huecos de picking o reserva que no se estén contando ya.");
            }
            var ids = ubicaciones.Select(u => u.Id).ToList();
            if (await contexto.Reservas.AnyAsync(r => ids.Contains(r.UbicacionId) && r.Estado == EstadoDeReserva.Abierta, ct) ||
                await contexto.Tareas.AnyAsync(t => (ids.Contains(t.OrigenId) || ids.Contains(t.DestinoId)) &&
                                                    (t.Estado == EstadoDeTarea.Pendiente || t.Estado == EstadoDeTarea.EnCurso), ct))
            {
                throw new ReglaIncumplida("ubicacion-en-uso", "Alguna ubicación tiene pedidos o tareas pendientes: espera a que terminen.");
            }
            var claves = await contexto.Existencias.Where(e => ids.Contains(e.UbicacionId) && e.Cantidad > 0)
                .Select(e => new { e.ArticuloId, e.UbicacionId, e.Lote }).ToListAsync(ct);
            var existencias = await contexto.BloquearExistencias(
                claves.Select(c => new ClaveDeExistencia(c.ArticuloId, c.UbicacionId, c.Lote)), ct);
            var nuevo = Conteo.Abrir(await contexto.Siguiente("CON", ct), ids,
                existencias.Select(e => (e.UbicacionId, e.ArticuloId, e.Lote, e.Caducidad, e.Cantidad)), reloj.GetUtcNow());
            ubicaciones.ForEach(u => u.Bloquear());
            contexto.Conteos.Add(nuevo);
            return nuevo;
        }, ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.ConteosCambiados, conteo.Numero), ct);
        return conteo;
    }

    public async Task Contar(Guid conteoId, Contado contado, CancellationToken ct)
    {
        var conteo = await Cargar(conteoId, ct);
        var ubicacion = await huecos.PorCodigo(contado.CodigoDeUbicacion, ct);
        var lectura = Lectura.Interpretar(contado.CodigoDeArticulo);
        var articulo = lectura.Gtin is { } gtin ? await contexto.Articulos.SingleOrDefaultAsync(a => a.Ean == gtin, ct) : null;
        if (articulo is null)
        {
            throw new ReglaIncumplida("articulo-desconocido", $"«{contado.CodigoDeArticulo}» no es un artículo.");
        }
        var (lote, caducidad) = articulo.ValidarLote(lectura.Lote ?? contado.Lote, lectura.Caducidad ?? contado.Caducidad);
        conteo.Contar(ubicacion.Id, articulo.Id, lote, caducidad, contado.Cantidad);
        await contexto.SaveChangesAsync(ct);
    }

    public async Task<Conteo> TerminarVuelta(Guid conteoId, CancellationToken ct)
    {
        var conteo = await Cargar(conteoId, ct);
        conteo.TerminarVuelta();
        if (conteo.Terminado)
        {
            await Desbloquear(conteo, ct);
        }
        await contexto.SaveChangesAsync(ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.ConteosCambiados, conteo.Numero), ct);
        return conteo;
    }

    /// <summary>El jefe de almacén aprueba: cada diferencia se convierte en un movimiento de ajuste.</summary>
    public async Task<Conteo> Aprobar(Guid conteoId, CancellationToken ct)
    {
        var conteo = await contexto.EnTransaccion(async () =>
        {
            var conteo = await Cargar(conteoId, ct);
            foreach (var linea in conteo.Aprobar(usuario.Nombre))
            {
                var sobra = linea.Diferencia > 0;
                await stock.Mover(TipoDeMovimiento.Ajuste, linea.ArticuloId, linea.Lote, linea.Caducidad,
                    sobra ? null : linea.UbicacionId, sobra ? linea.UbicacionId : null, Math.Abs(linea.Diferencia),
                    conteo.Numero, ct);
            }
            await Desbloquear(conteo, ct);
            return conteo;
        }, ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.ConteosCambiados, conteo.Numero), ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.StockCambiado), ct);
        return conteo;
    }

    public async Task Anular(Guid conteoId, CancellationToken ct)
    {
        var conteo = await Cargar(conteoId, ct);
        conteo.Anular();
        await Desbloquear(conteo, ct);
        await contexto.SaveChangesAsync(ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.ConteosCambiados, conteo.Numero), ct);
    }

    private async Task Desbloquear(Conteo conteo, CancellationToken ct)
    {
        var ids = conteo.Ubicaciones.ToList();
        foreach (var ubicacion in await contexto.Ubicaciones.Where(u => ids.Contains(u.Id)).ToListAsync(ct))
        {
            ubicacion.Desbloquear();
        }
    }

    private async Task<Conteo> Cargar(Guid id, CancellationToken ct) =>
        await contexto.Conteos.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new NoEncontrado("No existe ese conteo.");
}
