using Microsoft.EntityFrameworkCore;
using Sga.Aplicacion.Abstracciones;
using Sga.Aplicacion.Stock;
using Sga.Dominio.Almacen;
using Sga.Dominio.Catalogo;
using Sga.Dominio.Codigos;
using Sga.Dominio.Comun;
using Sga.Dominio.Entradas;
using Sga.Dominio.Stock;

namespace Sga.Aplicacion.Entradas;

public sealed record LineaPrevista(Guid ArticuloId, int Cantidad);

/// <summary>
/// Lo que manda el terminal al descargar: lo escaneado (un EAN o una etiqueta GS1-128) y lo que la etiqueta no trae y
/// el operario escribe a mano (lote, caducidad, cantidad).
/// </summary>
public sealed record Descargado(string Codigo, string? Lote, DateOnly? Caducidad, int? Cantidad, bool Danado);

public sealed record ResultadoDeDescarga(Descarga Descarga, IReadOnlyList<Tarea> Tareas);

/// <summary>Entradas: prever lo que llega, recibirlo escaneando y mandarlo a ubicar.</summary>
public sealed class ServicioDeEntradas(IContexto contexto, ServicioDeStock stock, Huecos huecos, IUsuarioActual usuario,
    IAvisos avisos, TimeProvider reloj)
{
    public async Task<Recepcion> Prever(Guid proveedorId, DateOnly fechaPrevista, IReadOnlyList<LineaPrevista> lineas,
        CancellationToken ct)
    {
        if (!await contexto.Proveedores.AnyAsync(p => p.Id == proveedorId, ct))
        {
            throw new NoEncontrado("No existe ese proveedor.");
        }
        var articulos = lineas.Select(l => l.ArticuloId).Distinct().ToList();
        if (await contexto.Articulos.CountAsync(a => articulos.Contains(a.Id), ct) != articulos.Count)
        {
            throw new NoEncontrado("Alguno de los artículos no existe.");
        }
        var recepcion = await contexto.EnTransaccion(async () =>
        {
            var numero = await contexto.Siguiente("REC", ct);
            var nueva = Recepcion.Prever(numero, proveedorId, fechaPrevista, lineas.Select(l => (l.ArticuloId, l.Cantidad)));
            contexto.Recepciones.Add(nueva);
            return nueva;
        }, ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.RecepcionesCambiadas, recepcion.Numero), ct);
        return recepcion;
    }

    /// <summary>
    /// Anota lo descargado y lo deja en la playa de recepción (o en cuarentena si viene dañado). Lo bueno sale ya
    /// con su tarea de ubicar, al hueco que sugiere el almacén.
    /// </summary>
    public async Task<ResultadoDeDescarga> Recibir(Guid recepcionId, Descargado descargado, CancellationToken ct)
    {
        var resultado = await contexto.EnTransaccion(async () =>
        {
            var recepcion = await contexto.Recepciones.SingleOrDefaultAsync(r => r.Id == recepcionId, ct)
                            ?? throw new NoEncontrado("No existe esa recepción.");
            var lectura = Lectura.Interpretar(descargado.Codigo);
            var articulo = await ArticuloDe(lectura, ct);
            var (lote, caducidad) = articulo.ValidarLote(lectura.Lote ?? descargado.Lote, lectura.Caducidad ?? descargado.Caducidad);
            var cantidad = descargado.Cantidad ?? lectura.Cantidad
                           ?? throw new ReglaIncumplida("cantidad-obligatoria", "Indica cuántas unidades son.");
            var ahora = reloj.GetUtcNow();
            var descarga = recepcion.Registrar(articulo.Id, lote, caducidad, cantidad, descargado.Danado, usuario.Nombre, ahora);

            var playa = await huecos.Playa(descargado.Danado ? TipoDeZona.Cuarentena : TipoDeZona.Recepcion, ct);
            await stock.Mover(descargado.Danado ? TipoDeMovimiento.Cuarentena : TipoDeMovimiento.Recepcion, articulo.Id,
                lote, caducidad, null, playa.Id, cantidad, recepcion.Numero, ct);

            var tareas = new List<Tarea>();
            if (!descargado.Danado)
            {
                foreach (var destino in SugerenciaDeUbicacion.Sugerir(articulo.Id, cantidad, await huecos.ParaUbicar(articulo.Id, ct)))
                {
                    tareas.Add(Tarea.Ubicar(articulo.Id, lote, caducidad, destino.Cantidad, playa.Id, destino.Ubicacion,
                        recepcion.Numero, ahora));
                }
                contexto.Tareas.AddRange(tareas);
            }
            return new ResultadoDeDescarga(descarga, tareas);
        }, ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.TareasCambiadas), ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.RecepcionesCambiadas), ct);
        return resultado;
    }

    public async Task<Recepcion> Cerrar(Guid recepcionId, string? observaciones, CancellationToken ct)
    {
        var recepcion = await contexto.Recepciones.SingleOrDefaultAsync(r => r.Id == recepcionId, ct)
                        ?? throw new NoEncontrado("No existe esa recepción.");
        recepcion.Cerrar(observaciones);
        await contexto.SaveChangesAsync(ct);
        await avisos.Publicar(new Aviso(TipoDeAviso.RecepcionesCambiadas, recepcion.Numero), ct);
        return recepcion;
    }

    private async Task<Articulo> ArticuloDe(Lectura lectura, CancellationToken ct)
    {
        if (lectura.Gtin is not { } gtin)
        {
            throw new ReglaIncumplida("no-es-un-articulo", $"«{lectura.Texto}» no es el código de un artículo.");
        }
        return await contexto.Articulos.SingleOrDefaultAsync(a => a.Ean == gtin, ct)
               ?? throw new ReglaIncumplida("articulo-desconocido", $"No hay ningún artículo con el código {gtin}.");
    }
}
