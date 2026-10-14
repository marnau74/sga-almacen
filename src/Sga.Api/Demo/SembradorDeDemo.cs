using System.Text.Json;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Sga.Api.Infraestructura;
using Sga.Aplicacion.Abstracciones;
using Sga.Aplicacion.Entradas;
using Sga.Aplicacion.Maestros;
using Sga.Aplicacion.Salidas;
using Sga.Aplicacion.Tareas;
using Sga.Dominio.Almacen;
using Sga.Dominio.Catalogo;
using Sga.Dominio.Salidas;
using Sga.Infraestructura.Identidad;
using Sga.Infraestructura.Persistencia;

namespace Sga.Api.Demo;

public sealed class OpcionesDeDemo
{
    public bool Activa { get; set; }

    /// <summary>La contraseña de los usuarios de demostración (solo para la demo local).</summary>
    public string Contrasena { get; set; } = "";

    public string Fichero { get; set; } = "Demo/articulos.json";
}

internal sealed record ArticuloDeOpenFoodFacts(string Ean, string Nombre, string? Marca, string Formato, string Categoria,
    bool ControlDeLote, bool ControlDeCaducidad);

/// <summary>
/// La base de demostración: artículos reales de Open Food Facts y un almacén de ejemplo (mapa, proveedores, clientes,
/// usuarios, entradas y pedidos). Todo pasa por los mismos servicios que usa la API, así que cumple las mismas reglas.
/// Se hace una sola vez: si ya hay artículos, no hace nada.
/// </summary>
internal sealed class SembradorDeDemo(IServiceScopeFactory scopes, IOptions<OpcionesDeDemo> opciones, IWebHostEnvironment entorno,
    TimeProvider reloj, ILogger<SembradorDeDemo> registro)
{
    // Configuración de ejemplo por categoría: unidades por caja. No es un dato de Open Food Facts.
    private static readonly Dictionary<string, int> UnidadesPorCaja = new()
    {
        ["Leche"] = 6,
        ["Yogures"] = 6,
        ["Quesos"] = 8,
        ["Aceite de oliva"] = 12,
        ["Pasta"] = 20,
        ["Arroz"] = 12,
        ["Conservas de atún"] = 24,
        ["Cereales"] = 10,
        ["Galletas"] = 12,
        ["Café"] = 12,
        ["Legumbres"] = 12,
        ["Tomate frito"] = 12,
        ["Sal"] = 12,
        ["Azúcar"] = 10,
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Random _azar = new(2026);
    private static readonly CancellationToken Ct = CancellationToken.None;

    public async Task Sembrar(CancellationToken ct)
    {
        if (await En(sp => sp.GetRequiredService<IContexto>().Articulos.AnyAsync(ct)))
        {
            return;
        }
        var contrasena = opciones.Value.Contrasena;
        if (contrasena.Length < 10)
        {
            throw new InvalidOperationException("La demo necesita Demo:Contrasena (10 caracteres como mínimo).");
        }
        await CrearUsuarios(contrasena);
        var articulos = await Catalogo();
        await Mapa(articulos);
        var (proveedores, clientes) = await Terceros();
        await Entradas(articulos, proveedores);
        await Salidas(articulos, clientes);
        Registro.ArticulosDeDemo(registro, articulos.Count);
    }

    private Task<bool> CrearUsuarios(string contrasena) => En(async sp =>
    {
        var usuarios = sp.GetRequiredService<UserManager<Usuario>>();
        await Usuarios.Asegurar(usuarios, "admin", "Administración", contrasena, Roles.Administrador);
        await Usuarios.Asegurar(usuarios, "jefa", "Jefa de almacén", contrasena, Roles.JefeDeAlmacen);
        await Usuarios.Asegurar(usuarios, "operario1", "Operario 1", contrasena, Roles.Operario);
        await Usuarios.Asegurar(usuarios, "operario2", "Operario 2", contrasena, Roles.Operario);
        await Usuarios.Asegurar(usuarios, "oficina", "Oficina", contrasena, Roles.Administracion);
        return true;
    });

    private async Task<List<Articulo>> Catalogo()
    {
        var ruta = Path.Combine(entorno.ContentRootPath, opciones.Value.Fichero);
        if (!File.Exists(ruta))
        {
            ruta = Path.Combine(AppContext.BaseDirectory, opciones.Value.Fichero);
        }
        await using var fichero = File.OpenRead(ruta);
        var lista = await JsonSerializer.DeserializeAsync<List<ArticuloDeOpenFoodFacts>>(fichero,
                        Json, Ct)
                    ?? [];
        var creados = new List<Articulo>();
        foreach (var a in lista)
        {
            creados.Add(await En(sp => sp.GetRequiredService<ServicioDeMaestros>().CrearArticulo(new NuevoArticulo(a.Ean,
                a.Nombre, a.Marca, a.Formato, UnidadesPorCaja.GetValueOrDefault(a.Categoria, 6), a.ControlDeLote,
                a.ControlDeCaducidad), Ct)));
        }
        return creados;
    }

    /// <summary>Picking: pasillos 1 a 3 (20 módulos, un nivel). Reserva: pasillos 4 a 9 (20 módulos, tres niveles).</summary>
    private Task<bool> Mapa(List<Articulo> articulos) => En(async sp =>
    {
        var maestros = sp.GetRequiredService<ServicioDeMaestros>();
        var picking = new List<Ubicacion>();
        for (var pasillo = 1; pasillo <= 3; pasillo++)
        {
            picking.AddRange(await maestros.CrearEstanteria(new NuevaEstanteria(TipoDeZona.Picking, pasillo, 20, 1), Ct));
        }
        for (var pasillo = 4; pasillo <= 9; pasillo++)
        {
            await maestros.CrearEstanteria(new NuevaEstanteria(TipoDeZona.Reserva, pasillo, 20, 3), Ct);
        }
        foreach (var (hueco, articulo) in picking.OrderBy(h => h.Orden).Zip(articulos.Where((_, i) => i % 3 == 0)))
        {
            var caja = articulo.UnidadesPorCaja;
            await maestros.AsignarPicking(hueco.Codigo, articulo.Id, caja * 2, caja * 8, Ct);
        }
        return true;
    });

    private Task<(List<Proveedor>, List<Cliente>)> Terceros() => En(async sp =>
    {
        var maestros = sp.GetRequiredService<ServicioDeMaestros>();
        var proveedores = new List<Proveedor>();
        for (var i = 1; i <= 4; i++)
        {
            proveedores.Add(await maestros.CrearProveedor($"PRV-{i:000}", $"Proveedor {i:000}", Ct));
        }
        var clientes = new List<Cliente>();
        int[] dias = [0, 5, 10, 20, 7, 0];
        for (var i = 1; i <= dias.Length; i++)
        {
            clientes.Add(await maestros.CrearCliente($"CLI-{i:000}", $"Cliente {i:000}", dias[i - 1], Ct));
        }
        return (proveedores, clientes);
    });

    /// <summary>Mercancía recibida y ubicada (en uno o dos lotes) y algunas recepciones pendientes para el terminal.</summary>
    private async Task Entradas(List<Articulo> articulos, List<Proveedor> proveedores)
    {
        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        var conStock = articulos.Where((_, i) => i % 3 == 0 || i % 5 == 0).ToList();
        foreach (var (grupo, n) in conStock.Chunk(12).Select((g, n) => (g, n)))
        {
            var proveedor = proveedores[n % proveedores.Count];
            var lineas = grupo.Select(a => new LineaPrevista(a.Id, a.UnidadesPorCaja * _azar.Next(6, 20))).ToList();
            var recepcion = await En(sp => sp.GetRequiredService<ServicioDeEntradas>().Prever(proveedor.Id, hoy, lineas, Ct));
            foreach (var linea in lineas)
            {
                var articulo = grupo.Single(a => a.Id == linea.ArticuloId);
                var lotes = articulo.ControlDeLote && _azar.Next(3) == 0 ? 2 : 1;
                var resto = linea.Cantidad;
                for (var l = 0; l < lotes; l++)
                {
                    var cantidad = l == lotes - 1 ? resto : resto / 2;
                    resto -= cantidad;
                    DateOnly? caducidad = articulo.ControlDeCaducidad ? hoy.AddDays(_azar.Next(8, 240)) : null;
                    var lote = articulo.ControlDeLote ? $"L{hoy:yyMMdd}{_azar.Next(10, 99)}" : null;
                    var descarga = await En(sp => sp.GetRequiredService<ServicioDeEntradas>().Recibir(recepcion.Id,
                        new Descargado(articulo.Ean.Valor, lote, caducidad, cantidad, false), Ct), "operario1");
                    foreach (var tarea in descarga.Tareas)
                    {
                        await Hacer(tarea.Id, "operario1", (_, destino) =>
                            new Confirmacion(null, articulo.Ean.Valor, tarea.Cantidad, destino));
                    }
                }
            }
            await En(sp => sp.GetRequiredService<ServicioDeEntradas>().Cerrar(recepcion.Id, null, Ct), "operario1");
        }

        // Dos recepciones previstas para hoy, sin recibir: para probarlas en el terminal.
        var sinStock = articulos.Except(conStock).ToList();
        for (var i = 0; i < 2; i++)
        {
            var lineas = sinStock.Skip(i * 6).Take(6).Select(a => new LineaPrevista(a.Id, a.UnidadesPorCaja * _azar.Next(4, 10))).ToList();
            await En(sp => sp.GetRequiredService<ServicioDeEntradas>().Prever(proveedores[i].Id, hoy, lineas, Ct));
        }
    }

    /// <summary>
    /// Pedidos para mañana: unos ya preparados y expedidos (para el panel), una oleada en curso (tareas en el terminal),
    /// otros asignados esperando oleada y otros pendientes.
    /// </summary>
    private async Task Salidas(List<Articulo> articulos, List<Cliente> clientes)
    {
        var manana = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime).AddDays(1);
        var conStock = await En(sp => sp.GetRequiredService<IContexto>().Existencias.Where(e => e.Cantidad > 0)
            .Select(e => e.ArticuloId).Distinct().ToListAsync(Ct));
        var pedidos = new List<Pedido>();
        for (var i = 0; i < 16; i++)
        {
            var lineas = conStock.OrderBy(_ => _azar.Next()).Take(_azar.Next(3, 9))
                .Select(id => new LineaPedida(id, articulos.Single(a => a.Id == id).UnidadesPorCaja * _azar.Next(1, 4)))
                .ToList();
            pedidos.Add(await En(sp => sp.GetRequiredService<ServicioDeSalidas>().Crear(clientes[i % clientes.Count].Id, manana, lineas, Ct)));
        }

        var asignados = pedidos.Take(12).Select(p => p.Id).ToList();
        await En(sp => sp.GetRequiredService<ServicioDeSalidas>().Asignar(asignados, Ct), "jefa");

        // Los tres primeros, de principio a fin.
        var completos = asignados.Take(3).ToList();
        await En(sp => sp.GetRequiredService<ServicioDeSalidas>().LanzarOleada(completos, Ct), "jefa");
        var tareas = await En(sp => sp.GetRequiredService<IContexto>().Tareas
            .Where(t => t.Tipo == TipoDeTarea.Picking && t.PedidoId != null && completos.Contains(t.PedidoId.Value))
            .OrderBy(t => t.Orden).ToListAsync(Ct));
        foreach (var tarea in tareas)
        {
            var ean = await En(sp => sp.GetRequiredService<IContexto>().Articulos.Where(a => a.Id == tarea.ArticuloId)
                .Select(a => a.Ean).SingleAsync(Ct));
            await Hacer(tarea.Id, "operario2", (origen, _) => new Confirmacion(origen, ean.Valor, tarea.Cantidad, null));
        }
        foreach (var id in completos)
        {
            await Embalar(id);
            await En(async sp => { await sp.GetRequiredService<ServicioDeSalidas>().Expedir(id, Ct); return true; }, "operario2");
        }

        // Una oleada lanzada, con sus tareas esperando en el terminal.
        await En(sp => sp.GetRequiredService<ServicioDeSalidas>().LanzarOleada(asignados.Skip(3).Take(4).ToList(), Ct), "jefa");
    }

    private async Task Embalar(Guid pedidoId)
    {
        var preparados = await En(sp => sp.GetRequiredService<IContexto>().Pedidos.AsNoTracking().Where(p => p.Id == pedidoId)
            .SelectMany(p => p.Preparados).ToListAsync(Ct));
        var eans = await En(sp => sp.GetRequiredService<IContexto>().Articulos.ToDictionaryAsync(a => a.Id, a => a.Ean.Valor, Ct));
        await En(sp => sp.GetRequiredService<ServicioDeSalidas>().AbrirBulto(pedidoId, Ct), "operario2");
        foreach (var preparado in preparados)
        {
            await En(async sp =>
            {
                await sp.GetRequiredService<ServicioDeSalidas>().Meter(pedidoId, eans[preparado.ArticuloId], preparado.Lote,
                    preparado.Cantidad, Ct);
                return true;
            }, "operario2");
        }
        await En(sp => sp.GetRequiredService<ServicioDeSalidas>().CerrarBulto(pedidoId, Ct), "operario2");
    }

    private async Task Hacer(Guid tareaId, string operario, Func<string, string, Confirmacion> confirmacion)
    {
        await En(sp => sp.GetRequiredService<ServicioDeTareas>().Tomar(tareaId, Ct), operario);
        var codigos = await En(async sp =>
        {
            var contexto = sp.GetRequiredService<SgaContexto>();
            var tarea = await contexto.Tareas.SingleAsync(t => t.Id == tareaId, Ct);
            var origen = await contexto.Ubicaciones.Where(u => u.Id == tarea.OrigenId).Select(u => u.Codigo).SingleAsync(Ct);
            var destino = await contexto.Ubicaciones.Where(u => u.Id == tarea.DestinoId).Select(u => u.Codigo).SingleAsync(Ct);
            return (origen, destino);
        });
        await En(sp => sp.GetRequiredService<ServicioDeTareas>().Confirmar(tareaId, confirmacion(codigos.origen, codigos.destino), Ct), operario);
    }

    /// <summary>Cada paso en su propio contexto, como una petición de la API, con el usuario que lo hace.</summary>
    private async Task<T> En<T>(Func<IServiceProvider, Task<T>> trabajo, string usuario = "sistema")
    {
        await using var scope = scopes.CreateAsyncScope();
        UsuarioDeDemo.Actual.Value = usuario;
        try
        {
            return await trabajo(scope.ServiceProvider);
        }
        finally
        {
            UsuarioDeDemo.Actual.Value = null;
        }
    }
}

/// <summary>Durante la siembra no hay petición HTTP: el usuario de cada paso va aquí.</summary>
internal static class UsuarioDeDemo
{
    public static readonly AsyncLocal<string?> Actual = new();
}
