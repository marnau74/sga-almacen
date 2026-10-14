using Microsoft.EntityFrameworkCore;
using Sga.Aplicacion.Abstracciones;
using Sga.Dominio.Almacen;
using Sga.Dominio.Catalogo;
using Sga.Dominio.Codigos;
using Sga.Dominio.Comun;
using Sga.Dominio.Stock;

namespace Sga.Aplicacion.Maestros;

public sealed record NuevoArticulo(string Ean, string Nombre, string? Marca, string? Formato, int UnidadesPorCaja,
    bool ControlDeLote, bool ControlDeCaducidad);

public sealed record NuevaEstanteria(TipoDeZona Zona, int Pasillo, int Modulos, int Niveles);

/// <summary>Altas del catálogo y del mapa del almacén.</summary>
public sealed class ServicioDeMaestros(IContexto contexto, TimeProvider reloj)
{
    /// <summary>Los códigos de las playas: lo que lleva su etiqueta en el suelo.</summary>
    public static readonly IReadOnlyDictionary<TipoDeZona, string> Playas = new Dictionary<TipoDeZona, string>
    {
        [TipoDeZona.Recepcion] = "RECEPCION",
        [TipoDeZona.Expedicion] = "MUELLE",
        [TipoDeZona.Cuarentena] = "CUARENTENA",
    };

    /// <summary>Crea las playas de recepción, muelle y cuarentena si no están. Se puede repetir.</summary>
    public async Task AsegurarPlayas(CancellationToken ct)
    {
        var existentes = await contexto.Ubicaciones.Where(u => Playas.Values.Contains(u.Codigo)).Select(u => u.Codigo)
            .ToListAsync(ct);
        var orden = 0;
        foreach (var (zona, codigo) in Playas)
        {
            orden++;
            if (!existentes.Contains(codigo))
            {
                contexto.Ubicaciones.Add(Ubicacion.Playa(zona, codigo, orden));
            }
        }
        await contexto.SaveChangesAsync(ct);
    }

    public async Task<Articulo> CrearArticulo(NuevoArticulo nuevo, CancellationToken ct)
    {
        var ean = Gtin.Crear(nuevo.Ean);
        if (await contexto.Articulos.AnyAsync(a => a.Ean == ean, ct))
        {
            throw new ReglaIncumplida("ean-repetido", $"Ya hay un artículo con el código {ean}.");
        }
        var articulo = Articulo.Crear(ean, nuevo.Nombre, nuevo.Marca, nuevo.Formato, nuevo.UnidadesPorCaja,
            nuevo.ControlDeLote, nuevo.ControlDeCaducidad);
        contexto.Articulos.Add(articulo);
        await contexto.SaveChangesAsync(ct);
        return articulo;
    }

    public async Task<Proveedor> CrearProveedor(string codigo, string nombre, CancellationToken ct)
    {
        if (await contexto.Proveedores.AnyAsync(p => p.Codigo == codigo.Trim(), ct))
        {
            throw new ReglaIncumplida("codigo-repetido", $"Ya hay un proveedor {codigo}.");
        }
        var proveedor = Proveedor.Crear(codigo, nombre);
        contexto.Proveedores.Add(proveedor);
        await contexto.SaveChangesAsync(ct);
        return proveedor;
    }

    public async Task<Cliente> CrearCliente(string codigo, string nombre, int diasMinimosDeVidaUtil, CancellationToken ct)
    {
        if (await contexto.Clientes.AnyAsync(c => c.Codigo == codigo.Trim(), ct))
        {
            throw new ReglaIncumplida("codigo-repetido", $"Ya hay un cliente {codigo}.");
        }
        var cliente = Cliente.Crear(codigo, nombre, diasMinimosDeVidaUtil);
        contexto.Clientes.Add(cliente);
        await contexto.SaveChangesAsync(ct);
        return cliente;
    }

    /// <summary>Da de alta los huecos de una estantería: un pasillo, sus módulos y sus niveles.</summary>
    public async Task<IReadOnlyList<Ubicacion>> CrearEstanteria(NuevaEstanteria nueva, CancellationToken ct)
    {
        if (nueva.Modulos is < 1 or > 99 || nueva.Niveles is < 1 or > 10)
        {
            throw new ReglaIncumplida("estanteria-no-valida", "De 1 a 99 módulos y de 1 a 10 niveles.");
        }
        // El picking se hace a mano: empieza en el nivel 1. La reserva tiene palés también en el suelo (nivel 0).
        var primerNivel = nueva.Zona == TipoDeZona.Picking ? 1 : 0;
        var huecos = new List<Ubicacion>();
        for (var modulo = 1; modulo <= nueva.Modulos; modulo++)
        {
            for (var nivel = primerNivel; nivel < primerNivel + nueva.Niveles; nivel++)
            {
                huecos.Add(Ubicacion.Hueco(nueva.Zona, nueva.Pasillo, modulo, nivel));
            }
        }
        var codigos = huecos.Select(h => h.Codigo).ToList();
        if (await contexto.Ubicaciones.AnyAsync(u => codigos.Contains(u.Codigo), ct))
        {
            throw new ReglaIncumplida("codigo-repetido", "Alguno de esos huecos ya existe.");
        }
        contexto.Ubicaciones.AddRange(huecos);
        await contexto.SaveChangesAsync(ct);
        return huecos;
    }

    /// <summary>Asigna un artículo a un hueco de picking, con su mínimo y su máximo.</summary>
    public async Task AsignarPicking(string codigoDeHueco, Guid articuloId, int minimo, int maximo, CancellationToken ct)
    {
        var hueco = await contexto.Ubicaciones.SingleOrDefaultAsync(u => u.Codigo == codigoDeHueco, ct)
                    ?? throw new NoEncontrado($"No existe el hueco {codigoDeHueco}.");
        if (!await contexto.Articulos.AnyAsync(a => a.Id == articuloId, ct))
        {
            throw new NoEncontrado("No existe ese artículo.");
        }
        if (hueco.ArticuloFijoId is { } actual && actual != articuloId &&
            await contexto.Existencias.AnyAsync(e => e.UbicacionId == hueco.Id && e.Cantidad > 0, ct))
        {
            throw new ReglaIncumplida("hueco-ocupado", "El hueco aún tiene stock de otro artículo.");
        }
        hueco.AsignarArticulo(articuloId, minimo, maximo);
        await contexto.SaveChangesAsync(ct);
    }

    public async Task BloquearLote(Guid articuloId, string lote, string motivo, CancellationToken ct)
    {
        if (await contexto.LotesBloqueados.AnyAsync(l => l.ArticuloId == articuloId && l.Lote == lote, ct))
        {
            return;
        }
        contexto.LotesBloqueados.Add(LoteBloqueado.Crear(articuloId, lote, motivo, reloj.GetUtcNow()));
        await contexto.SaveChangesAsync(ct);
    }

    public async Task DesbloquearLote(Guid articuloId, string lote, CancellationToken ct)
    {
        await contexto.LotesBloqueados.Where(l => l.ArticuloId == articuloId && l.Lote == lote).ExecuteDeleteAsync(ct);
    }
}
