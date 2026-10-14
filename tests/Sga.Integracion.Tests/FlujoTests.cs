using Microsoft.EntityFrameworkCore;
using Sga.Aplicacion.Consultas;
using Sga.Aplicacion.Entradas;
using Sga.Aplicacion.Salidas;
using Sga.Aplicacion.Tareas;
using Sga.Dominio.Almacen;
using Sga.Dominio.Comun;
using Sga.Dominio.Entradas;
using Sga.Dominio.Salidas;
using Sga.Dominio.Stock;

namespace Sga.Integracion.Tests;

public class FlujoTests(ServidorPostgres servidor)
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateOnly Noviembre20 = new(2026, 11, 20);
    private static readonly DateOnly Diciembre1 = new(2026, 12, 1);

    [Fact]
    public async Task De_la_recepcion_a_la_expedicion()
    {
        await using var e = await Escenario.Crear(servidor);

        // Llegan 30 de un lote: el picking (máximo 24) se llena y el resto va a la reserva más cercana.
        await e.Entrar(e.Leche, "L1", Diciembre1, 30);
        // Llegan 12 de un lote que caduca antes: el picking ya está lleno, van a la reserva.
        await e.Entrar(e.Leche, "L0", Noviembre20, 12);

        var stock = await e.Con<Consultas, IReadOnlyList<StockEnUbicacion>>(c => c.StockDe(e.Leche.Id, Ct));
        stock.Select(s => (s.Ubicacion, s.Lote, s.Cantidad)).ShouldBe(
            [("R-02-01-1", "L0", 12), ("P-01-01-1", "L1", 24), ("R-02-01-0", "L1", 6)]);

        // Un pedido de 20: FEFO saca primero las 12 del lote que caduca antes y luego 8 del picking.
        var pedido = await e.Pedir(e.Tienda, (e.Leche, 20));
        var asignacion = await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([pedido.Id], Ct));
        asignacion.ShouldHaveSingleItem().Estado.ShouldBe(EstadoDePedido.Asignado);
        var oleada = await e.Con<ServicioDeSalidas, Oleada>(s => s.LanzarOleada([pedido.Id], Ct));

        // El operario ve las tareas en el orden del recorrido: primero el picking, luego la reserva.
        var tareas = await e.Con<Consultas, IReadOnlyList<TareaParaElTerminal>>(c => c.TareasPara("luis", TipoDeTarea.Picking, Ct));
        tareas.Select(t => (t.Origen, t.Lote, t.Cantidad)).ShouldBe([("P-01-01-1", "L1", 8), ("R-02-01-1", "L0", 12)]);
        tareas.ShouldAllBe(t => t.Documento == oleada.Numero);

        foreach (var tarea in tareas)
        {
            await e.Hacer(tarea.Id, "luis", c => new Confirmacion(c.Origen, Escenario.EanLeche, tarea.Cantidad, null));
        }
        var preparado = await e.Con<Consultas, PedidoDetalle>(c => c.Pedido(pedido.Id, Ct));
        preparado.Resumen.Estado.ShouldBe(EstadoDePedido.Preparado);
        (await e.Leer(c => c.Oleadas.SingleAsync(o => o.Id == oleada.Id))).Estado.ShouldBe(EstadoDeOleada.Terminada);

        // Embalaje: dos lotes del mismo artículo, así que hay que decir cuál.
        await e.Con<ServicioDeSalidas, Bulto>(s => s.AbrirBulto(pedido.Id, Ct));
        var sinLote = await Should.ThrowAsync<ReglaIncumplida>(() =>
            e.Con<ServicioDeSalidas, bool>(async s => { await s.Meter(pedido.Id, Escenario.EanLeche, null, 1, Ct); return true; }));
        sinLote.Codigo.ShouldBe("indica-el-lote");
        // La etiqueta GS1 del palé trae el lote y la cantidad.
        await e.Con<ServicioDeSalidas, bool>(async s => { await s.Meter(pedido.Id, "(01)08410297012150(10)L0(37)12", null, null, Ct); return true; });
        await e.Con<ServicioDeSalidas, bool>(async s => { await s.Meter(pedido.Id, Escenario.EanLeche, "L1", 8, Ct); return true; });
        var bulto = await e.Con<ServicioDeSalidas, Bulto>(s => s.CerrarBulto(pedido.Id, Ct));
        bulto.Sscc.ShouldStartWith("00200000");

        await e.Con<ServicioDeSalidas, bool>(async s => { await s.Expedir(pedido.Id, Ct); return true; });

        var final = await e.Con<Consultas, IReadOnlyList<StockEnUbicacion>>(c => c.StockDe(e.Leche.Id, Ct));
        final.Sum(s => s.Cantidad).ShouldBe(22);
        final.ShouldNotContain(s => s.Ubicacion == "MUELLE");
        (await e.Con<Consultas, PedidoDetalle>(c => c.Pedido(pedido.Id, Ct))).Resumen.Estado.ShouldBe(EstadoDePedido.Expedido);

        // Trazabilidad del lote L0: de qué recepción vino, a quién se ha servido y que ya no queda.
        var traza = await e.Con<Consultas, Trazabilidad>(c => c.Trazabilidad(e.Leche.Id, "L0", Ct));
        traza.Recepciones.ShouldHaveSingleItem();
        traza.Salidas.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            s => s.Cliente.ShouldBe("Cliente 001"), s => s.Cantidad.ShouldBe(12), s => s.Expedido.ShouldNotBeNull());
        traza.DondeEsta.ShouldBeEmpty();
        traza.Movimientos.Select(m => m.Tipo).ShouldBe(
            [TipoDeMovimiento.Recepcion, TipoDeMovimiento.Ubicacion, TipoDeMovimiento.Picking, TipoDeMovimiento.Expedicion]);

        await e.ComprobarLibro();
    }

    [Fact]
    public async Task Al_bajar_del_minimo_el_picking_pide_reposicion_desde_la_reserva()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Sal, "", null, 20); // llena el picking (máximo 20)
        await e.Entrar(e.Sal, "", null, 40); // a la reserva

        var pedido = await e.Pedir(e.Tienda, (e.Sal, 18));
        await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([pedido.Id], Ct));
        await e.Con<ServicioDeSalidas, Oleada>(s => s.LanzarOleada([pedido.Id], Ct));
        var coger = (await e.Con<Consultas, IReadOnlyList<TareaParaElTerminal>>(c => c.TareasPara("luis", TipoDeTarea.Picking, Ct))).Single();
        coger.Origen.ShouldBe("P-01-03-1"); // a igual caducidad (ninguna), del picking
        await e.Hacer(coger.Id, "luis", c => new Confirmacion(c.Origen, Escenario.EanSal, 18, null));

        // Quedan 2 en el picking, por debajo del mínimo (5): se repone hasta el máximo.
        var reponer = (await e.Con<Consultas, IReadOnlyList<TareaParaElTerminal>>(c => c.TareasPara("luis", TipoDeTarea.Reponer, Ct))).Single();
        (reponer.Destino, reponer.Cantidad).ShouldBe(("P-01-03-1", 18));

        // Escanear el hueco equivocado no vale.
        await Should.ThrowAsync<ReglaIncumplida>(() =>
            e.Hacer(reponer.Id, "luis", c => new Confirmacion(c.Origen, Escenario.EanSal, 18, "P-01-04-1")));
        await e.Con<ServicioDeTareas, bool>(async s => { await s.Soltar(reponer.Id, Ct); return true; }, "luis");
        await e.Hacer(reponer.Id, "luis", c => new Confirmacion(c.Origen, Escenario.EanSal, 18, c.Destino));

        var stock = await e.Con<Consultas, IReadOnlyList<StockEnUbicacion>>(c => c.StockDe(e.Sal.Id, Ct));
        stock.Where(s => s.Ubicacion == "P-01-03-1").Sum(s => s.Cantidad).ShouldBe(20);
        await e.ComprobarLibro();
    }

    [Fact]
    public async Task Si_en_el_hueco_falta_mercancia_se_busca_en_otro_sitio_y_queda_como_incidencia()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Leche, "L1", Diciembre1, 40); // 24 al picking, 16 a la reserva

        var pedido = await e.Pedir(e.Tienda, (e.Leche, 10));
        await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([pedido.Id], Ct));
        await e.Con<ServicioDeSalidas, Oleada>(s => s.LanzarOleada([pedido.Id], Ct));
        var coger = (await e.Con<Consultas, IReadOnlyList<TareaParaElTerminal>>(c => c.TareasPara("luis", TipoDeTarea.Picking, Ct))).Single();
        coger.Origen.ShouldBe("P-01-01-1");

        // En el hueco solo hay 4 (alguien se equivocó antes): se confirman 4.
        var hecha = await e.Hacer(coger.Id, "luis", c => new Confirmacion(c.Origen, Escenario.EanLeche, 4, null));
        hecha.Estado.ShouldBe(EstadoDeTarea.ConFalta);

        // Las 6 que faltan salen de la reserva, en una tarea nueva de la misma oleada.
        var otra = (await e.Con<Consultas, IReadOnlyList<TareaParaElTerminal>>(c => c.TareasPara("luis", TipoDeTarea.Picking, Ct))).Single();
        (otra.Origen, otra.Cantidad).ShouldBe(("R-02-01-0", 6));
        await e.Hacer(otra.Id, "luis", c => new Confirmacion(c.Origen, Escenario.EanLeche, 6, null));

        var detalle = await e.Con<Consultas, PedidoDetalle>(c => c.Pedido(pedido.Id, Ct));
        detalle.Resumen.Estado.ShouldBe(EstadoDePedido.Preparado);
        detalle.Lineas.Single().ShouldSatisfyAllConditions(l => l.Preparada.ShouldBe(10), l => l.Rotura.ShouldBe(0));
        var panel = await e.Con<Consultas, Panel>(c => c.Panel(Ct));
        panel.Faltas.ShouldHaveSingleItem().ShouldSatisfyAllConditions(f => f.Ubicacion.ShouldBe("P-01-01-1"), f => f.Unidades.ShouldBe(6));
        await e.ComprobarLibro();
    }

    [Fact]
    public async Task Coger_algo_que_no_es_o_en_otro_hueco_no_vale()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Leche, "L1", Diciembre1, 10);
        var pedido = await e.Pedir(e.Tienda, (e.Leche, 5));
        await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([pedido.Id], Ct));
        await e.Con<ServicioDeSalidas, Oleada>(s => s.LanzarOleada([pedido.Id], Ct));
        var coger = (await e.Con<Consultas, IReadOnlyList<TareaParaElTerminal>>(c => c.TareasPara("luis", TipoDeTarea.Picking, Ct))).Single();

        (await Should.ThrowAsync<ReglaIncumplida>(() =>
            e.Hacer(coger.Id, "luis", c => new Confirmacion(c.Origen, Escenario.EanLecheHacendado, 5, null)))).Codigo.ShouldBe("articulo-equivocado");
        (await Should.ThrowAsync<ReglaIncumplida>(() =>
            e.Hacer(coger.Id, "luis", c => new Confirmacion("P-01-02-1", Escenario.EanLeche, 5, null)))).Codigo.ShouldBe("origen-equivocado");
        (await Should.ThrowAsync<ReglaIncumplida>(() =>
            e.Hacer(coger.Id, "luis", c => new Confirmacion(c.Origen, "(01)08410297012150(10)OTRO", 5, null)))).Codigo.ShouldBe("lote-equivocado");

        await e.Hacer(coger.Id, "luis", c => new Confirmacion(c.Origen, "(01)08410297012150(10)L1", 5, null));
        await e.ComprobarLibro();
    }

    [Fact]
    public async Task Lo_que_llega_danado_va_a_cuarentena_y_no_se_sirve()
    {
        await using var e = await Escenario.Crear(servidor);
        var recepcion = await e.Con<ServicioDeEntradas, Recepcion>(s =>
            s.Prever(e.Proveedor.Id, new DateOnly(2026, 10, 14), [new LineaPrevista(e.Sal.Id, 12)], Ct));
        var descarga = await e.Con<ServicioDeEntradas, ResultadoDeDescarga>(s =>
            s.Recibir(recepcion.Id, new Descargado(Escenario.EanSal, null, null, 12, true), Ct));
        descarga.Tareas.ShouldBeEmpty();
        (await e.Con<ServicioDeEntradas, Recepcion>(s => s.Cerrar(recepcion.Id, "Caja rota", Ct))).Estado.ShouldBe(EstadoDeRecepcion.ConDiferencias);

        var pedido = await e.Pedir(e.Tienda, (e.Sal, 1));
        (await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([pedido.Id], Ct)))
            .Single().Estado.ShouldBe(EstadoDePedido.Pendiente);
        (await e.Con<Consultas, IReadOnlyList<StockEnUbicacion>>(c => c.StockDe(e.Sal.Id, Ct))).Single().Zona.ShouldBe(TipoDeZona.Cuarentena);
    }

    [Fact]
    public async Task Un_cliente_exigente_no_recibe_lo_que_caduca_pronto_ni_nadie_un_lote_bloqueado()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Leche, "PRONTO", new DateOnly(2026, 10, 30), 6); // 15 días el día de servicio
        await e.Entrar(e.Leche, "RETIRADO", Diciembre1, 6);
        await e.Entrar(e.Leche, "BUENO", new DateOnly(2026, 12, 15), 6);
        await e.Con<Sga.Aplicacion.Maestros.ServicioDeMaestros, bool>(async s =>
        {
            await s.BloquearLote(e.Leche.Id, "RETIRADO", "Retirada del fabricante", Ct);
            return true;
        });

        var exigente = await e.Pedir(e.Supermercado, (e.Leche, 10));
        var normal = await e.Pedir(e.Tienda, (e.Leche, 10));
        var asignacion = await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([exigente.Id, normal.Id], Ct));

        // El supermercado (20 días) solo acepta BUENO; la tienda se queda con PRONTO. RETIRADO, para nadie.
        asignacion.Select(a => (a.Asignadas)).ShouldBe([6, 6]);
        var reservas = await e.Leer(c => c.Reservas.Where(r => r.Estado == EstadoDeReserva.Abierta).ToListAsync());
        reservas.Single(r => r.PedidoId == exigente.Id).Lote.ShouldBe("BUENO");
        reservas.Single(r => r.PedidoId == normal.Id).Lote.ShouldBe("PRONTO");
        await e.ComprobarLibro();
    }

    [Fact]
    public async Task Cancelar_un_pedido_libera_su_stock()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Sal, "", null, 10);
        var pedido = await e.Pedir(e.Tienda, (e.Sal, 8));
        await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([pedido.Id], Ct));

        await e.Con<ServicioDeSalidas, bool>(async s => { await s.Cancelar(pedido.Id, Ct); return true; });

        (await e.Con<Consultas, IReadOnlyList<ArticuloResumen>>(c => c.Articulos(Escenario.EanSal, 10, Ct))).Single().Disponible.ShouldBe(10);
        await e.ComprobarLibro();
    }
}
