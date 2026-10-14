using Sga.Dominio.Almacen;
using Sga.Dominio.Catalogo;
using Sga.Dominio.Codigos;
using Sga.Dominio.Comun;
using Sga.Dominio.Stock;

namespace Sga.Dominio.Tests;

public class ReglasDeAlmacenTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Yogur = Guid.NewGuid();
    private static readonly DateOnly Servicio = new(2026, 10, 20);

    private static Candidata Hay(TipoDeZona zona, int orden, string lote, DateOnly? caducidad, int cantidad,
        bool ubicacionBloqueada = false, bool loteBloqueado = false)
    {
        var e = Existencia.Vacia(Yogur, Guid.NewGuid(), lote, caducidad);
        Libro.Aplicar(Movimiento.Crear(TipoDeMovimiento.Recepcion, Yogur, lote, caducidad, null, e.UbicacionId, cantidad,
            "prueba", "P", Ahora), null, e);
        return new Candidata(e, zona, orden, ubicacionBloqueada, loteBloqueado);
    }

    // FEFO

    [Fact]
    public void Sale_primero_lo_que_antes_caduca_aunque_este_en_la_reserva()
    {
        var tarde = Hay(TipoDeZona.Picking, 1, "TARDE", new DateOnly(2026, 11, 30), 20);
        var pronto = Hay(TipoDeZona.Reserva, 9, "PRONTO", new DateOnly(2026, 11, 10), 5);

        var asignado = AsignacionFefo.Asignar(8, [tarde, pronto], Servicio, 0);

        asignado.Select(a => (a.Existencia.Lote, a.Cantidad)).ShouldBe([("PRONTO", 5), ("TARDE", 3)]);
    }

    [Fact]
    public void Nunca_se_asigna_lo_que_no_llega_con_la_vida_util_que_pide_el_cliente()
    {
        // El cliente quiere 10 días de vida útil el día 20: nada que caduque antes del 30.
        var casi = Hay(TipoDeZona.Picking, 1, "CASI", new DateOnly(2026, 10, 29), 50);
        var justo = Hay(TipoDeZona.Picking, 2, "JUSTO", new DateOnly(2026, 10, 30), 4);

        var asignado = AsignacionFefo.Asignar(10, [casi, justo], Servicio, 10);

        asignado.ShouldHaveSingleItem().Existencia.Lote.ShouldBe("JUSTO");
        asignado.Sum(a => a.Cantidad).ShouldBe(4); // el resto es rotura, no se sirve lo que el cliente no acepta
    }

    [Fact]
    public void Ni_lotes_bloqueados_ni_ubicaciones_bloqueadas_ni_otras_zonas()
    {
        var candidatas = new[]
        {
            Hay(TipoDeZona.Picking, 1, "RETIRADO", new DateOnly(2026, 11, 1), 10, loteBloqueado: true),
            Hay(TipoDeZona.Picking, 2, "CONTANDO", new DateOnly(2026, 11, 2), 10, ubicacionBloqueada: true),
            Hay(TipoDeZona.Cuarentena, 3, "DANADO", new DateOnly(2026, 11, 3), 10),
            Hay(TipoDeZona.Recepcion, 4, "SIN-UBICAR", new DateOnly(2026, 11, 4), 10),
            Hay(TipoDeZona.Reserva, 5, "BUENO", new DateOnly(2026, 11, 5), 10),
        };

        AsignacionFefo.Asignar(30, candidatas, Servicio, 0).ShouldHaveSingleItem().Existencia.Lote.ShouldBe("BUENO");
    }

    [Fact]
    public void A_igual_caducidad_el_picking_antes_que_la_reserva_y_luego_el_recorrido()
    {
        var caducidad = new DateOnly(2026, 12, 1);
        var reserva = Hay(TipoDeZona.Reserva, 1, "R", caducidad, 10);
        var lejos = Hay(TipoDeZona.Picking, 50, "P2", caducidad, 10);
        var cerca = Hay(TipoDeZona.Picking, 10, "P1", caducidad, 10);

        AsignacionFefo.Asignar(25, [reserva, lejos, cerca], Servicio, 0).Select(a => a.Existencia.Lote)
            .ShouldBe(["P1", "P2", "R"]);
    }

    [Fact]
    public void Lo_que_no_caduca_va_despues_de_lo_que_caduca_y_lo_reservado_no_cuenta()
    {
        var sinCaducidad = Hay(TipoDeZona.Picking, 1, "", null, 10);
        var conCaducidad = Hay(TipoDeZona.Picking, 2, "C", new DateOnly(2027, 1, 1), 10);
        Reserva.Sobre(conCaducidad.Existencia, Guid.NewGuid(), 1, 6);

        AsignacionFefo.Asignar(8, [sinCaducidad, conCaducidad], Servicio, 0).Select(a => (a.Existencia.Lote, a.Cantidad))
            .ShouldBe([("C", 4), ("", 4)]);
    }

    // Ubicación sugerida

    private static Ubicacion PickingDe(Guid articulo, int pasillo, int modulo, int minimo, int maximo)
    {
        var u = Ubicacion.Hueco(TipoDeZona.Picking, pasillo, modulo, 1);
        u.AsignarArticulo(articulo, minimo, maximo);
        return u;
    }

    [Fact]
    public void Primero_se_llena_el_picking_del_articulo_y_el_resto_va_a_la_reserva_mas_cercana()
    {
        var picking = PickingDe(Yogur, 3, 10, 10, 60);
        var lejos = Ubicacion.Hueco(TipoDeZona.Reserva, 9, 1, 0);
        var cercaPeroOcupado = Ubicacion.Hueco(TipoDeZona.Reserva, 3, 10, 0);
        var cerca = Ubicacion.Hueco(TipoDeZona.Reserva, 3, 11, 0);
        var cercaPeroArriba = Ubicacion.Hueco(TipoDeZona.Reserva, 3, 11, 4);

        var destinos = SugerenciaDeUbicacion.Sugerir(Yogur, 100,
        [
            new HuecoOcupado(picking, 45), new HuecoOcupado(lejos, 0), new HuecoOcupado(cercaPeroOcupado, 80),
            new HuecoOcupado(cercaPeroArriba, 0), new HuecoOcupado(cerca, 0),
        ]);

        destinos.Select(d => (d.Ubicacion.Codigo, d.Cantidad)).ShouldBe([("P-03-10-1", 15), ("R-03-11-0", 85)]);
    }

    [Fact]
    public void Si_no_queda_hueco_libre_lo_dice()
    {
        var ocupado = Ubicacion.Hueco(TipoDeZona.Reserva, 1, 1, 0);
        var bloqueado = Ubicacion.Hueco(TipoDeZona.Reserva, 1, 2, 0);
        bloqueado.Bloquear();

        Should.Throw<ReglaIncumplida>(() => SugerenciaDeUbicacion.Sugerir(Yogur, 5,
            [new HuecoOcupado(ocupado, 10), new HuecoOcupado(bloqueado, 0)])).Codigo.ShouldBe("sin-hueco");
    }

    // Mapa y recorrido

    [Fact]
    public void El_recorrido_va_en_serpentina_y_el_picking_antes_que_la_reserva()
    {
        var huecos = new[]
        {
            Ubicacion.Hueco(TipoDeZona.Picking, 2, 1, 1), Ubicacion.Hueco(TipoDeZona.Picking, 1, 5, 1),
            Ubicacion.Hueco(TipoDeZona.Picking, 2, 9, 1), Ubicacion.Hueco(TipoDeZona.Picking, 1, 1, 1),
            Ubicacion.Hueco(TipoDeZona.Reserva, 1, 1, 0),
        };

        huecos.OrderBy(h => h.Orden).Select(h => h.Codigo)
            .ShouldBe(["P-01-01-1", "P-01-05-1", "P-02-09-1", "P-02-01-1", "R-01-01-0"]);
    }

    [Fact]
    public void Solo_el_picking_tiene_articulo_fijo_y_el_minimo_es_menor_que_el_maximo()
    {
        Should.Throw<ReglaIncumplida>(() => Ubicacion.Hueco(TipoDeZona.Reserva, 1, 1, 0).AsignarArticulo(Yogur, 1, 5));
        Should.Throw<ReglaIncumplida>(() => PickingDe(Yogur, 1, 1, 10, 10)).Codigo.ShouldBe("minimo-maximo");
    }

    // Tareas

    [Fact]
    public void Una_tarea_es_de_quien_la_coge()
    {
        var tarea = Tarea.Ubicar(Yogur, "L1", null, 10, Guid.NewGuid(), Ubicacion.Hueco(TipoDeZona.Reserva, 1, 1, 0), "REC-1", Ahora);

        tarea.Tomar("ana");
        tarea.Tomar("ana"); // volver a abrirla no cambia nada
        Should.Throw<ReglaIncumplida>(() => tarea.Tomar("luis")).Codigo.ShouldBe("tarea-ocupada");
        Should.Throw<ReglaIncumplida>(() => tarea.Terminar("luis", 10, tarea.DestinoId, Ahora)).Codigo.ShouldBe("tarea-no-es-tuya");

        tarea.Soltar("ana");
        tarea.Tomar("luis");
        tarea.Terminar("luis", 10, tarea.DestinoId, Ahora);
        tarea.Estado.ShouldBe(EstadoDeTarea.Hecha);
    }

    [Fact]
    public void Al_ubicar_se_mueve_todo_pero_se_puede_cambiar_de_hueco()
    {
        var tarea = Tarea.Ubicar(Yogur, "L1", null, 10, Guid.NewGuid(), Ubicacion.Hueco(TipoDeZona.Reserva, 1, 1, 0), "REC-1", Ahora);
        tarea.Tomar("ana");
        var otro = Guid.NewGuid();

        Should.Throw<ReglaIncumplida>(() => tarea.Terminar("ana", 9, otro, Ahora));
        tarea.Terminar("ana", 10, otro, Ahora);

        tarea.DestinoId.ShouldBe(otro);
    }

    [Fact]
    public void En_el_picking_se_puede_coger_menos_y_queda_como_falta()
    {
        var hueco = Hay(TipoDeZona.Picking, 1, "L1", null, 10).Existencia;
        var reserva = Reserva.Sobre(hueco, Guid.NewGuid(), 1, 6);
        var muelle = Guid.NewGuid();
        var tarea = Tarea.Coger(reserva, Ubicacion.Hueco(TipoDeZona.Picking, 1, 1, 1), muelle, Guid.NewGuid(), "OLE-1", Ahora);
        tarea.Tomar("ana");

        Should.Throw<ReglaIncumplida>(() => tarea.Terminar("ana", 6, Guid.NewGuid(), Ahora)).Codigo.ShouldBe("destino-no-valido");
        tarea.Terminar("ana", 4, muelle, Ahora);

        tarea.Estado.ShouldBe(EstadoDeTarea.ConFalta);
        tarea.Hecho.ShouldBe(4);
    }

    // Catálogo

    [Fact]
    public void Un_articulo_con_caducidad_lleva_lote_y_las_entradas_lo_traen()
    {
        var ean = Gtin.Crear("8410297012150");
        Should.Throw<ReglaIncumplida>(() => Articulo.Crear(ean, "Leche", null, null, 6, false, true)).Codigo.ShouldBe("caducidad-sin-lote");

        var leche = Articulo.Crear(ean, "Leche semidesnatada", "Central Lechera Asturiana", "1 l", 6, true, true);
        Should.Throw<ReglaIncumplida>(() => leche.ValidarLote("", new DateOnly(2026, 12, 1))).Codigo.ShouldBe("lote-obligatorio");
        Should.Throw<ReglaIncumplida>(() => leche.ValidarLote("L1", null)).Codigo.ShouldBe("caducidad-obligatoria");
        leche.ValidarLote(" L1 ", new DateOnly(2026, 12, 1)).ShouldBe(("L1", new DateOnly(2026, 12, 1)));

        var sal = Articulo.Crear(Gtin.Crear("8480000103826"), "Sal", null, null, 12, false, false);
        sal.ValidarLote("L9", new DateOnly(2030, 1, 1)).ShouldBe(("", (DateOnly?)null));
    }
}
