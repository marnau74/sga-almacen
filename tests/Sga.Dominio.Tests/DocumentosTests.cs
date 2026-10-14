using Sga.Dominio.Codigos;
using Sga.Dominio.Comun;
using Sga.Dominio.Entradas;
using Sga.Dominio.Inventario;
using Sga.Dominio.Salidas;

namespace Sga.Dominio.Tests;

public class DocumentosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Leche = Guid.NewGuid();
    private static readonly Guid Aceite = Guid.NewGuid();

    // Recepción

    [Fact]
    public void Una_recepcion_que_cuadra_se_cierra_como_recibida()
    {
        var recepcion = Recepcion.Prever("REC-000001", Guid.NewGuid(), new DateOnly(2026, 10, 14), [(Leche, 12), (Aceite, 6)]);

        recepcion.Registrar(Leche, "L1", new DateOnly(2026, 12, 1), 6, false, "ana", Ahora);
        recepcion.Registrar(Leche, "L2", new DateOnly(2026, 12, 8), 6, false, "ana", Ahora);
        recepcion.Registrar(Aceite, "", null, 6, false, "ana", Ahora);
        recepcion.Cerrar(null);

        recepcion.Estado.ShouldBe(EstadoDeRecepcion.Recibida);
        recepcion.Descargas.Count.ShouldBe(3);
    }

    [Fact]
    public void De_mas_de_menos_o_danado_se_cierra_con_diferencias()
    {
        var recepcion = Recepcion.Prever("REC-000002", Guid.NewGuid(), new DateOnly(2026, 10, 14), [(Leche, 12)]);
        recepcion.Registrar(Leche, "L1", null, 10, false, "ana", Ahora);
        recepcion.Registrar(Leche, "L1", null, 2, true, "ana", Ahora);

        recepcion.Cerrar("Dos cajas aplastadas");

        recepcion.Estado.ShouldBe(EstadoDeRecepcion.ConDiferencias);
        recepcion.Lineas[0].Recibida.ShouldBe(10);
        recepcion.Lineas[0].Danada.ShouldBe(2);
    }

    [Fact]
    public void No_se_recibe_lo_que_no_viene_ni_nada_despues_de_cerrar()
    {
        var recepcion = Recepcion.Prever("REC-000003", Guid.NewGuid(), new DateOnly(2026, 10, 14), [(Leche, 1)]);
        Should.Throw<ReglaIncumplida>(() => recepcion.Registrar(Aceite, "", null, 1, false, "ana", Ahora)).Codigo.ShouldBe("articulo-no-previsto");
        Should.Throw<ReglaIncumplida>(() => recepcion.Cerrar(null)).Codigo.ShouldBe("recepcion-sin-empezar");

        recepcion.Registrar(Leche, "L1", null, 1, false, "ana", Ahora);
        recepcion.Cerrar(null);

        Should.Throw<ReglaIncumplida>(() => recepcion.Registrar(Leche, "L1", null, 1, false, "ana", Ahora)).Codigo.ShouldBe("recepcion-cerrada");
    }

    // Pedido

    private static Pedido UnPedido() =>
        Pedido.Crear("PED-000001", Guid.NewGuid(), new DateOnly(2026, 10, 15), [(Leche, 10), (Aceite, 4)], Ahora);

    [Fact]
    public void Un_pedido_de_principio_a_fin()
    {
        var pedido = UnPedido();
        pedido.Asignado(1, 10);
        pedido.Asignado(2, 4);
        pedido.TerminarAsignacion();
        pedido.Estado.ShouldBe(EstadoDePedido.Asignado);

        pedido.Lanzar(Guid.NewGuid());
        pedido.Cogido(1, "L1", new DateOnly(2026, 12, 1), 6);
        pedido.Cogido(1, "L2", new DateOnly(2026, 12, 8), 4);
        pedido.Cogido(2, "", null, 4);
        pedido.TerminarPreparacion();
        pedido.Estado.ShouldBe(EstadoDePedido.Preparado);

        pedido.AbrirBulto(Sscc.Generar("0200000", 0, 1));
        pedido.Meter(Leche, "L1", 6);
        pedido.Meter(Leche, "L2", 4);
        pedido.CerrarBulto();
        pedido.Estado.ShouldBe(EstadoDePedido.Preparado);
        pedido.AbrirBulto(Sscc.Generar("0200000", 0, 2));
        pedido.Meter(Aceite, "", 4);
        pedido.CerrarBulto();
        pedido.Estado.ShouldBe(EstadoDePedido.Embalado);

        pedido.Expedir(Ahora);
        pedido.Estado.ShouldBe(EstadoDePedido.Expedido);
        pedido.Bultos.Count.ShouldBe(2);
    }

    [Fact]
    public void Sin_stock_para_todo_queda_parcial_y_la_falta_se_ve_como_rotura()
    {
        var pedido = UnPedido();
        pedido.Asignado(1, 7);
        pedido.TerminarAsignacion();
        pedido.Estado.ShouldBe(EstadoDePedido.AsignadoParcial);

        pedido.Lanzar(Guid.NewGuid());
        pedido.Cogido(1, "L1", null, 5);
        pedido.Falta(1, 2); // en el hueco solo había 5
        pedido.TerminarPreparacion();

        pedido.Lineas[0].Rotura.ShouldBe(5);
        pedido.Lineas[1].Rotura.ShouldBe(4);
    }

    [Fact]
    public void Sin_nada_asignado_sigue_pendiente()
    {
        var pedido = UnPedido();
        pedido.TerminarAsignacion();
        pedido.Estado.ShouldBe(EstadoDePedido.Pendiente);
    }

    [Fact]
    public void No_se_coge_mas_de_lo_asignado_ni_se_embala_lo_que_no_esta_preparado()
    {
        var pedido = UnPedido();
        pedido.Asignado(1, 10);
        pedido.TerminarAsignacion();
        pedido.Lanzar(Guid.NewGuid());
        Should.Throw<ReglaIncumplida>(() => pedido.Cogido(1, "L1", null, 11));
        Should.Throw<ReglaIncumplida>(() => pedido.TerminarPreparacion()).Codigo.ShouldBe("preparacion-pendiente");
        pedido.Cogido(1, "L1", null, 10);
        pedido.TerminarPreparacion();

        pedido.AbrirBulto(Sscc.Generar("0200000", 0, 3));
        Should.Throw<ReglaIncumplida>(() => pedido.Meter(Leche, "OTRO", 1)).Codigo.ShouldBe("no-esta-preparado");
        Should.Throw<ReglaIncumplida>(() => pedido.Meter(Leche, "L1", 11)).Codigo.ShouldBe("no-esta-preparado");
        Should.Throw<ReglaIncumplida>(() => pedido.AbrirBulto(Sscc.Generar("0200000", 0, 4))).Codigo.ShouldBe("bulto-abierto");
    }

    [Fact]
    public void Solo_se_cancela_antes_de_prepararlo()
    {
        var pedido = UnPedido();
        pedido.Asignado(1, 10);
        pedido.TerminarAsignacion();
        pedido.Cancelar();
        pedido.Estado.ShouldBe(EstadoDePedido.Cancelado);
        pedido.Lineas[0].Asignada.ShouldBe(0);

        var otro = UnPedido();
        otro.Asignado(1, 10);
        otro.TerminarAsignacion();
        otro.Lanzar(Guid.NewGuid());
        Should.Throw<ReglaIncumplida>(otro.Cancelar).Codigo.ShouldBe("no-se-puede-cancelar");
    }

    [Fact]
    public void Un_pedido_no_se_sirve_en_el_pasado()
    {
        Should.Throw<ReglaIncumplida>(() =>
            Pedido.Crear("PED-9", Guid.NewGuid(), new DateOnly(2026, 10, 13), [(Leche, 1)], Ahora)).Codigo.ShouldBe("fecha-pasada");
    }

    // Conteo

    private static readonly Guid Hueco = Guid.NewGuid();

    private static Conteo UnConteo() => Conteo.Abrir("CON-000001", [Hueco],
        [(Hueco, Leche, "L1", new DateOnly(2026, 12, 1), 10), (Hueco, Aceite, "", null, 5)], Ahora);

    [Fact]
    public void Si_todo_cuadra_a_la_primera_se_cierra_sin_ajustes()
    {
        var conteo = UnConteo();
        conteo.Contar(Hueco, Leche, "L1", null, 10);
        conteo.Contar(Hueco, Aceite, "", null, 5);
        conteo.TerminarVuelta();
        conteo.Estado.ShouldBe(EstadoDeConteo.Cerrado);
    }

    [Fact]
    public void Lo_que_no_cuadra_se_recuenta_y_el_ajuste_necesita_aprobacion()
    {
        var conteo = UnConteo();
        conteo.Contar(Hueco, Leche, "L1", null, 8);
        conteo.Contar(Hueco, Aceite, "", null, 5);
        conteo.Contar(Hueco, Leche, "L9", null, 3); // ha aparecido un lote que no estaba
        conteo.TerminarVuelta();

        conteo.Estado.ShouldBe(EstadoDeConteo.Recuento);
        conteo.PorRecontar.Select(l => l.Lote).ShouldBe(["L1", "L9"], ignoreOrder: true);
        Should.Throw<ReglaIncumplida>(() => conteo.Contar(Hueco, Aceite, "", null, 4)).Codigo.ShouldBe("no-hay-que-recontar");

        conteo.Contar(Hueco, Leche, "L1", null, 9);
        conteo.Contar(Hueco, Leche, "L9", null, 3);
        conteo.TerminarVuelta();
        conteo.Estado.ShouldBe(EstadoDeConteo.PorAprobar);

        var ajustes = conteo.Aprobar("jefa");
        ajustes.Select(l => (l.Lote, l.Diferencia)).ShouldBe([("L1", -1), ("L9", 3)], ignoreOrder: true);
        conteo.Estado.ShouldBe(EstadoDeConteo.Cerrado);
    }

    [Fact]
    public void Lo_que_no_se_cuenta_es_cero_y_solo_se_cuentan_las_ubicaciones_del_conteo()
    {
        var conteo = UnConteo();
        Should.Throw<ReglaIncumplida>(() => conteo.Contar(Guid.NewGuid(), Leche, "L1", null, 1)).Codigo.ShouldBe("ubicacion-fuera-del-conteo");
        conteo.Contar(Hueco, Leche, "L1", null, 10);
        conteo.TerminarVuelta(); // el aceite no se ha contado: 0

        conteo.TerminarVuelta(); // tampoco en el recuento
        conteo.Estado.ShouldBe(EstadoDeConteo.PorAprobar);
        conteo.Aprobar("jefa").ShouldHaveSingleItem().Diferencia.ShouldBe(-5);
    }
}
