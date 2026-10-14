using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Sga.Aplicacion;
using Sga.Aplicacion.Abstracciones;
using Sga.Aplicacion.Entradas;
using Sga.Aplicacion.Maestros;
using Sga.Aplicacion.Salidas;
using Sga.Aplicacion.Tareas;
using Sga.Dominio.Almacen;
using Sga.Dominio.Catalogo;
using Sga.Infraestructura;
using Sga.Infraestructura.Persistencia;

namespace Sga.Integracion.Tests;

internal sealed class UsuarioDePrueba : IUsuarioActual
{
    public string Nombre { get; set; } = "prueba";
}

internal sealed class AvisosDePrueba : IAvisos
{
    public List<Aviso> Publicados { get; } = [];

    public Task Publicar(Aviso aviso, CancellationToken ct)
    {
        lock (Publicados)
        {
            Publicados.Add(aviso);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Un almacén pequeño en una base de datos propia: playas, un pasillo de picking, uno de reserva, tres artículos
/// reales (con sus EAN de Open Food Facts), un proveedor y dos clientes. Cada llamada a <see cref="En{T}"/> es una
/// petición distinta (su propio contexto), como en la API.
/// </summary>
internal sealed class Escenario : IAsyncDisposable
{
    public const string EanLeche = "8410297012150";
    public const string EanLecheHacendado = "8480000103826";
    public const string EanSal = "8411741104001";

    private readonly ServiceProvider _servicios;

    public FakeTimeProvider Reloj { get; } = new(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));
    public AvisosDePrueba Avisos { get; } = new();
    public Articulo Leche { get; private set; } = null!;
    public Articulo LecheHacendado { get; private set; } = null!;
    public Articulo Sal { get; private set; } = null!;
    public Proveedor Proveedor { get; private set; } = null!;
    public Cliente Tienda { get; private set; } = null!;

    /// <summary>Un cliente exigente: quiere 20 días de vida útil.</summary>
    public Cliente Supermercado { get; private set; } = null!;

    private Escenario(string cadena)
    {
        var servicios = new ServiceCollection();
        servicios.AddInfraestructura(cadena);
        servicios.AddAplicacion();
        servicios.AddSingleton<TimeProvider>(Reloj);
        servicios.AddSingleton<IAvisos>(Avisos);
        servicios.AddScoped<UsuarioDePrueba>();
        servicios.AddScoped<IUsuarioActual>(sp => sp.GetRequiredService<UsuarioDePrueba>());
        servicios.Configure<OpcionesDelAlmacen>(_ => { });
        _servicios = servicios.BuildServiceProvider();
    }

    public static async Task<Escenario> Crear(ServidorPostgres servidor)
    {
        var escenario = new Escenario(await servidor.CrearBaseDeDatos());
        await escenario.Sembrar();
        return escenario;
    }

    private async Task Sembrar()
    {
        var ct = CancellationToken.None;
        await En(async sp =>
        {
            var maestros = sp.GetRequiredService<ServicioDeMaestros>();
            await maestros.AsegurarPlayas(ct);
            Leche = await maestros.CrearArticulo(new NuevoArticulo(EanLeche, "Leche semidesnatada", "Central Lechera Asturiana", "1 l", 6, true, true), ct);
            LecheHacendado = await maestros.CrearArticulo(new NuevoArticulo(EanLecheHacendado, "Leche semidesnatada", "Hacendado", "1 l", 6, true, true), ct);
            Sal = await maestros.CrearArticulo(new NuevoArticulo(EanSal, "Sal fina yodada", "Leda", "1 kg", 12, false, false), ct);
            Proveedor = await maestros.CrearProveedor("PRV-001", "Proveedor 001", ct);
            Tienda = await maestros.CrearCliente("CLI-001", "Cliente 001", 0, ct);
            Supermercado = await maestros.CrearCliente("CLI-002", "Cliente 002", 20, ct);
            // Pasillo 1: picking, 4 módulos de 1 nivel. Pasillo 2: reserva, 4 módulos de 2 niveles.
            await maestros.CrearEstanteria(new NuevaEstanteria(TipoDeZona.Picking, 1, 4, 1), ct);
            await maestros.CrearEstanteria(new NuevaEstanteria(TipoDeZona.Reserva, 2, 4, 2), ct);
            await maestros.AsignarPicking("P-01-01-1", Leche.Id, 6, 24, ct);
            await maestros.AsignarPicking("P-01-02-1", LecheHacendado.Id, 6, 24, ct);
            await maestros.AsignarPicking("P-01-03-1", Sal.Id, 5, 20, ct);
            return true;
        });
    }

    public async Task<T> En<T>(Func<IServiceProvider, Task<T>> trabajo, string operario = "prueba")
    {
        await using var scope = _servicios.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UsuarioDePrueba>().Nombre = operario;
        return await trabajo(scope.ServiceProvider);
    }

    public Task<T> Con<TServicio, T>(Func<TServicio, Task<T>> trabajo, string operario = "prueba") where TServicio : notnull =>
        En(sp => trabajo(sp.GetRequiredService<TServicio>()), operario);

    public Task<T> Leer<T>(Func<SgaContexto, Task<T>> consulta) =>
        En(sp => consulta(sp.GetRequiredService<SgaContexto>()));

    // Atajos para montar situaciones

    /// <summary>Recibe y ubica (en el hueco sugerido) una entrada de un artículo.</summary>
    public async Task Entrar(Articulo articulo, string lote, DateOnly? caducidad, int cantidad)
    {
        var ct = CancellationToken.None;
        var recepcion = await Con<ServicioDeEntradas, Dominio.Entradas.Recepcion>(s =>
            s.Prever(Proveedor.Id, new DateOnly(2026, 10, 14), [new LineaPrevista(articulo.Id, cantidad)], ct));
        var descarga = await Con<ServicioDeEntradas, ResultadoDeDescarga>(s =>
            s.Recibir(recepcion.Id, new Descargado(articulo.Ean.Valor, lote, caducidad, cantidad, false), ct), "ana");
        foreach (var tarea in descarga.Tareas)
        {
            await Hacer(tarea.Id, "ana", c => new Confirmacion(null, articulo.Ean.Valor, tarea.Cantidad, c.Destino));
        }
        await Con<ServicioDeEntradas, Dominio.Entradas.Recepcion>(s => s.Cerrar(recepcion.Id, null, ct));
    }

    /// <summary>Toma una tarea y la confirma con lo que diga <paramref name="confirmacion"/> (que ve los códigos).</summary>
    public async Task<Tarea> Hacer(Guid tareaId, string operario, Func<(string Origen, string Destino), Confirmacion> confirmacion)
    {
        var ct = CancellationToken.None;
        await Con<ServicioDeTareas, Tarea>(s => s.Tomar(tareaId, ct), operario);
        var codigos = await Leer(async c =>
        {
            var tarea = await c.Tareas.SingleAsync(t => t.Id == tareaId);
            var origen = await c.Ubicaciones.Where(u => u.Id == tarea.OrigenId).Select(u => u.Codigo).SingleAsync();
            var destino = await c.Ubicaciones.Where(u => u.Id == tarea.DestinoId).Select(u => u.Codigo).SingleAsync();
            return (origen, destino);
        });
        return await Con<ServicioDeTareas, Tarea>(s => s.Confirmar(tareaId, confirmacion(codigos), ct), operario);
    }

    public Task<Dominio.Salidas.Pedido> Pedir(Cliente cliente, params (Articulo Articulo, int Cantidad)[] lineas) =>
        Con<ServicioDeSalidas, Dominio.Salidas.Pedido>(s => s.Crear(cliente.Id, new DateOnly(2026, 10, 15),
            lineas.Select(l => new LineaPedida(l.Articulo.Id, l.Cantidad)).ToList(), CancellationToken.None));

    /// <summary>
    /// El libro cuadra: cada existencia es la suma de sus movimientos y lo reservado, la suma de sus reservas abiertas.
    /// </summary>
    public Task ComprobarLibro() => Leer(async c =>
    {
        var descuadres = await c.Database.SqlQueryRaw<string>(
            """
            WITH libro AS (
                SELECT articulo_id, destino_id AS ubicacion_id, lote, cantidad FROM stock.movimiento WHERE destino_id IS NOT NULL
                UNION ALL
                SELECT articulo_id, origen_id, lote, -cantidad FROM stock.movimiento WHERE origen_id IS NOT NULL
            ), segun_libro AS (
                SELECT articulo_id, ubicacion_id, lote, sum(cantidad) AS cantidad FROM libro GROUP BY 1, 2, 3
            ), reservado AS (
                SELECT articulo_id, ubicacion_id, lote, sum(cantidad - consumida) AS cantidad
                FROM stock.reserva WHERE estado = 'Abierta' GROUP BY 1, 2, 3
            )
            SELECT coalesce(e.lote, l.lote) AS "Value"
            FROM stock.existencia e
            FULL JOIN segun_libro l USING (articulo_id, ubicacion_id, lote)
            LEFT JOIN reservado r USING (articulo_id, ubicacion_id, lote)
            WHERE coalesce(e.cantidad, 0) <> coalesce(l.cantidad, 0)
               OR coalesce(e.reservada, 0) <> coalesce(r.cantidad, 0)
            """).ToListAsync();
        descuadres.ShouldBeEmpty("el libro de movimientos y las existencias no cuadran");
        return true;
    });

    public ValueTask DisposeAsync() => _servicios.DisposeAsync();
}
