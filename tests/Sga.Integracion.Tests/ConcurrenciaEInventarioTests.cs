using Microsoft.EntityFrameworkCore;
using Sga.Aplicacion.Consultas;
using Sga.Aplicacion.Inventario;
using Sga.Aplicacion.Salidas;
using Sga.Aplicacion.Tareas;
using Sga.Dominio.Almacen;
using Sga.Dominio.Comun;
using Sga.Dominio.Inventario;
using Sga.Dominio.Salidas;
using Sga.Dominio.Stock;

namespace Sga.Integracion.Tests;

public class ConcurrenciaEInventarioTests(ServidorPostgres servidor)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    /// <summary>
    /// Diez repartos a la vez, cada uno de un pedido de 5, sobre 30 unidades: se asignan exactamente 30, nunca más, y
    /// nadie se queda bloqueado.
    /// </summary>
    [Fact]
    public async Task Diez_asignaciones_a_la_vez_no_prometen_mas_de_lo_que_hay()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Sal, "", null, 30);
        var pedidos = new List<Pedido>();
        for (var i = 0; i < 10; i++)
        {
            pedidos.Add(await e.Pedir(e.Tienda, (e.Sal, 5)));
        }

        var resultados = await Task.WhenAll(pedidos.Select(p =>
            e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([p.Id], Ct))));

        var asignadas = resultados.SelectMany(r => r).ToList();
        asignadas.Sum(a => a.Asignadas).ShouldBe(30);
        asignadas.Count(a => a.Estado == EstadoDePedido.Asignado).ShouldBe(6);
        asignadas.Count(a => a.Estado == EstadoDePedido.Pendiente).ShouldBe(4);
        await e.ComprobarLibro();
    }

    /// <summary>Diez operarios confirman a la vez el picking de diez pedidos distintos del mismo hueco.</summary>
    [Fact]
    public async Task Diez_operarios_cogiendo_del_mismo_hueco_dejan_el_stock_exacto()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Sal, "", null, 20);
        var pedidos = new List<Pedido>();
        for (var i = 0; i < 10; i++)
        {
            pedidos.Add(await e.Pedir(e.Tienda, (e.Sal, 2)));
        }
        await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar(pedidos.Select(p => p.Id).ToList(), Ct));
        await e.Con<ServicioDeSalidas, Oleada>(s => s.LanzarOleada(pedidos.Select(p => p.Id).ToList(), Ct));
        var tareas = await e.Con<Consultas, IReadOnlyList<TareaParaElTerminal>>(c => c.TareasPara("nadie", TipoDeTarea.Picking, Ct));
        tareas.Count.ShouldBe(10);

        await Task.WhenAll(tareas.Select((t, i) =>
            e.Hacer(t.Id, $"operario{i}", c => new Confirmacion(c.Origen, Escenario.EanSal, 2, null))));

        (await e.Leer(c => c.Pedidos.CountAsync(p => p.Estado == EstadoDePedido.Preparado))).ShouldBe(10);
        var stock = await e.Con<Consultas, IReadOnlyList<StockEnUbicacion>>(c => c.StockDe(e.Sal.Id, Ct));
        stock.Single(s => s.Ubicacion == "MUELLE").Cantidad.ShouldBe(20);
        await e.ComprobarLibro();
    }

    [Fact]
    public async Task Dos_operarios_no_pueden_quedarse_la_misma_tarea()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Sal, "", null, 10);
        var pedido = await e.Pedir(e.Tienda, (e.Sal, 3));
        await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([pedido.Id], Ct));
        await e.Con<ServicioDeSalidas, Oleada>(s => s.LanzarOleada([pedido.Id], Ct));
        var tarea = (await e.Con<Consultas, IReadOnlyList<TareaParaElTerminal>>(c => c.TareasPara("x", TipoDeTarea.Picking, Ct))).Single();

        string[] operarios = ["ana", "luis", "eva", "juan"];
        var intentos = await Task.WhenAll(operarios.Select(async operario =>
        {
            try
            {
                await e.Con<ServicioDeTareas, Tarea>(s => s.Tomar(tarea.Id, Ct), operario);
                return operario;
            }
            catch (Exception ex) when (ex is ReglaIncumplida or DbUpdateConcurrencyException)
            {
                return null;
            }
        }));

        var ganador = intentos.Where(o => o is not null).ShouldHaveSingleItem();
        (await e.Leer(c => c.Tareas.SingleAsync(t => t.Id == tarea.Id))).Operario.ShouldBe(ganador);
    }

    // Inventario

    [Fact]
    public async Task Un_conteo_con_diferencia_se_recuenta_y_al_aprobarlo_ajusta_el_stock()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Sal, "", null, 20); // P-01-03-1

        var conteo = await e.Con<ServicioDeInventario, Conteo>(s => s.Abrir(["P-01-03-1", "R-02-04-0"], Ct), "jefa");
        (await e.Leer(c => c.Ubicaciones.Where(u => u.Bloqueada).Select(u => u.Codigo).ToListAsync()))
            .ShouldBe(["P-01-03-1", "R-02-04-0"], ignoreOrder: true);

        // Quien cuenta no ve lo esperado.
        var ciego = await e.Con<Consultas, ConteoDetalle>(c => c.Conteo(conteo.Id, conLoEsperado: false, Ct));
        ciego.Lineas.ShouldBeEmpty();

        await Contar(e, conteo.Id, "P-01-03-1", 18);
        (await e.Con<ServicioDeInventario, Conteo>(s => s.TerminarVuelta(conteo.Id, Ct))).Estado.ShouldBe(EstadoDeConteo.Recuento);
        await Contar(e, conteo.Id, "P-01-03-1", 18);
        (await e.Con<ServicioDeInventario, Conteo>(s => s.TerminarVuelta(conteo.Id, Ct))).Estado.ShouldBe(EstadoDeConteo.PorAprobar);

        var detalle = await e.Con<Consultas, ConteoDetalle>(c => c.Conteo(conteo.Id, conLoEsperado: true, Ct));
        detalle.Lineas.Single().ShouldSatisfyAllConditions(l => l.Esperada.ShouldBe(20), l => l.Diferencia.ShouldBe(-2));

        await e.Con<ServicioDeInventario, Conteo>(s => s.Aprobar(conteo.Id, Ct), "jefa");

        var stock = await e.Con<Consultas, IReadOnlyList<StockEnUbicacion>>(c => c.StockDe(e.Sal.Id, Ct));
        stock.Single().Cantidad.ShouldBe(18);
        (await e.Leer(c => c.Ubicaciones.AnyAsync(u => u.Bloqueada))).ShouldBeFalse();
        (await e.Leer(c => c.Movimientos.SingleAsync(m => m.Tipo == TipoDeMovimiento.Ajuste))).ShouldSatisfyAllConditions(
            m => m.Cantidad.ShouldBe(2), m => m.DestinoId.ShouldBeNull(), m => m.Documento.ShouldBe(conteo.Numero), m => m.Usuario.ShouldBe("jefa"));
        await e.ComprobarLibro();
    }

    [Fact]
    public async Task No_se_cuenta_donde_hay_pedidos_a_medias_ni_se_sirve_de_lo_que_se_esta_contando()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Sal, "", null, 20);
        await e.Entrar(e.Sal, "", null, 10); // a la reserva
        var pedido = await e.Pedir(e.Tienda, (e.Sal, 2));
        await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([pedido.Id], Ct));

        (await Should.ThrowAsync<ReglaIncumplida>(() =>
            e.Con<ServicioDeInventario, Conteo>(s => s.Abrir(["P-01-03-1"], Ct)))).Codigo.ShouldBe("ubicacion-en-uso");

        // Mientras se cuenta la reserva, un pedido nuevo no toca lo que hay en ella.
        var reserva = (await e.Con<Consultas, IReadOnlyList<StockEnUbicacion>>(c => c.StockDe(e.Sal.Id, Ct)))
            .Single(s => s.Zona == TipoDeZona.Reserva).Ubicacion;
        await e.Con<ServicioDeInventario, Conteo>(s => s.Abrir([reserva], Ct));
        var otro = await e.Pedir(e.Tienda, (e.Sal, 25));
        (await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([otro.Id], Ct)))
            .Single().Asignadas.ShouldBe(18); // solo lo que queda libre en el picking
    }

    private static Task<bool> Contar(Escenario e, Guid conteo, string ubicacion, int cantidad) =>
        e.Con<ServicioDeInventario, bool>(async s =>
        {
            await s.Contar(conteo, new Contado(ubicacion, Escenario.EanSal, null, null, cantidad), Ct);
            return true;
        });
}
