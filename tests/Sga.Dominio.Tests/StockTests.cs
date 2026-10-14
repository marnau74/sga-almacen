using FsCheck.Xunit;
using Sga.Dominio.Almacen;
using Sga.Dominio.Comun;
using Sga.Dominio.Stock;

namespace Sga.Dominio.Tests;

public class StockTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Leche = Guid.NewGuid();

    private static Movimiento Mov(TipoDeMovimiento tipo, Guid? origen, Guid? destino, int cantidad, string lote = "L1") =>
        Movimiento.Crear(tipo, Leche, lote, new DateOnly(2026, 12, 1), origen, destino, cantidad, "prueba", "PRUEBA", Ahora);

    [Fact]
    public void Una_entrada_suma_y_un_traslado_mueve()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var enA = Existencia.Vacia(Leche, a, "L1", null);
        var enB = Existencia.Vacia(Leche, b, "L1", null);

        Libro.Aplicar(Mov(TipoDeMovimiento.Recepcion, null, a, 10), null, enA);
        Libro.Aplicar(Mov(TipoDeMovimiento.Traslado, a, b, 4), enA, enB);

        (enA.Cantidad, enB.Cantidad).ShouldBe((6, 4));
    }

    [Fact]
    public void No_sale_mas_de_lo_disponible_ni_se_toca_lo_reservado()
    {
        var a = Guid.NewGuid();
        var enA = Existencia.Vacia(Leche, a, "L1", null);
        Libro.Aplicar(Mov(TipoDeMovimiento.Recepcion, null, a, 10), null, enA);
        Reserva.Sobre(enA, Guid.NewGuid(), 1, 7);

        var error = Should.Throw<ReglaIncumplida>(() => Libro.Aplicar(Mov(TipoDeMovimiento.Expedicion, a, null, 4), enA, null));

        error.Codigo.ShouldBe("sin-stock");
        (enA.Cantidad, enA.Reservada, enA.Disponible).ShouldBe((10, 7, 3));
    }

    [Fact]
    public void Coger_lo_reservado_descuenta_la_existencia_y_la_reserva()
    {
        var hueco = Guid.NewGuid();
        var muelle = Guid.NewGuid();
        var enHueco = Existencia.Vacia(Leche, hueco, "L1", null);
        var enMuelle = Existencia.Vacia(Leche, muelle, "L1", null);
        Libro.Aplicar(Mov(TipoDeMovimiento.Recepcion, null, hueco, 10), null, enHueco);
        var reserva = Reserva.Sobre(enHueco, Guid.NewGuid(), 1, 6);

        Libro.Aplicar(Mov(TipoDeMovimiento.Picking, hueco, muelle, 4), enHueco, enMuelle, reserva);

        (enHueco.Cantidad, enHueco.Reservada).ShouldBe((6, 2));
        reserva.Pendiente.ShouldBe(2);
        enMuelle.Cantidad.ShouldBe(4);
    }

    [Fact]
    public void Coger_mas_de_lo_que_queda_en_la_reserva_no_deja_nada_a_medias()
    {
        var hueco = Guid.NewGuid();
        var enHueco = Existencia.Vacia(Leche, hueco, "L1", null);
        Libro.Aplicar(Mov(TipoDeMovimiento.Recepcion, null, hueco, 10), null, enHueco);
        var una = Reserva.Sobre(enHueco, Guid.NewGuid(), 1, 3);
        Reserva.Sobre(enHueco, Guid.NewGuid(), 1, 5);

        Should.Throw<ReglaIncumplida>(() => Libro.Aplicar(Mov(TipoDeMovimiento.Expedicion, hueco, null, 4), enHueco, null, una));

        (enHueco.Cantidad, enHueco.Reservada, una.Pendiente).ShouldBe((10, 8, 3));
    }

    [Fact]
    public void Liberar_una_reserva_devuelve_lo_pendiente_a_disponible()
    {
        var hueco = Guid.NewGuid();
        var enHueco = Existencia.Vacia(Leche, hueco, "L1", null);
        Libro.Aplicar(Mov(TipoDeMovimiento.Recepcion, null, hueco, 10), null, enHueco);
        var reserva = Reserva.Sobre(enHueco, Guid.NewGuid(), 1, 6);
        Libro.Aplicar(Mov(TipoDeMovimiento.Expedicion, hueco, null, 2), enHueco, null, reserva);

        reserva.Liberar(enHueco).ShouldBe(4);

        enHueco.Disponible.ShouldBe(8);
        reserva.Estado.ShouldBe(EstadoDeReserva.Consumida);
    }

    [Fact]
    public void Un_movimiento_sin_cantidad_o_al_mismo_sitio_no_existe()
    {
        var a = Guid.NewGuid();
        Should.Throw<ReglaIncumplida>(() => Mov(TipoDeMovimiento.Traslado, a, Guid.NewGuid(), 0));
        Should.Throw<ReglaIncumplida>(() => Mov(TipoDeMovimiento.Traslado, a, a, 1)).Codigo.ShouldBe("mismo-sitio");
    }

    /// <summary>
    /// Secuencias aleatorias de entradas, traslados, reservas, picking, liberaciones y salidas. Pase lo que pase (y
    /// fallen las operaciones que fallen), el stock cumple siempre las reglas del libro.
    /// </summary>
    [Property(MaxTest = 500)]
    public void El_libro_cuadra_siempre(byte[] semilla)
    {
        var ubicaciones = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var lotes = new[] { "A", "B" };
        var existencias = new Dictionary<ClaveDeExistencia, Existencia>();
        var movimientos = new List<Movimiento>();
        var reservas = new List<Reserva>();

        Existencia En(Guid ubicacion, string lote)
        {
            var clave = new ClaveDeExistencia(Leche, ubicacion, lote);
            if (!existencias.TryGetValue(clave, out var e))
            {
                existencias[clave] = e = Existencia.Vacia(Leche, ubicacion, lote, null);
            }
            return e;
        }

        void Mover(TipoDeMovimiento tipo, Guid? origen, Guid? destino, string lote, int n, Reserva? reserva = null)
        {
            var m = Movimiento.Crear(tipo, Leche, lote, null, origen, destino, n, "prueba", "P", Ahora);
            Libro.Aplicar(m, origen is null ? null : En(origen.Value, lote), destino is null ? null : En(destino.Value, lote), reserva);
            movimientos.Add(m);
        }

        for (var i = 0; i + 3 < semilla.Length; i += 4)
        {
            var u1 = ubicaciones[semilla[i + 1] % 3];
            var u2 = ubicaciones[(semilla[i + 1] + 1 + (semilla[i + 2] % 2)) % 3];
            var lote = lotes[semilla[i + 2] % 2];
            var n = (semilla[i + 3] % 15) + 1;
            try
            {
                switch (semilla[i] % 6)
                {
                    case 0:
                        Mover(TipoDeMovimiento.Recepcion, null, u1, lote, n);
                        break;
                    case 1:
                        Mover(TipoDeMovimiento.Traslado, u1, u2, lote, n);
                        break;
                    case 2:
                        reservas.Add(Reserva.Sobre(En(u1, lote), Guid.NewGuid(), 1, n));
                        break;
                    case 3 when reservas.Count > 0:
                        var aCoger = reservas[semilla[i + 1] % reservas.Count];
                        Mover(TipoDeMovimiento.Picking, aCoger.UbicacionId, u2 == aCoger.UbicacionId ? u1 : u2, aCoger.Lote, n, aCoger);
                        break;
                    case 4 when reservas.Count > 0:
                        var aLiberar = reservas[semilla[i + 1] % reservas.Count];
                        aLiberar.Liberar(En(aLiberar.UbicacionId, aLiberar.Lote));
                        break;
                    case 5:
                        Mover(TipoDeMovimiento.Expedicion, u1, null, lote, n);
                        break;
                }
            }
            catch (ReglaIncumplida)
            {
                // Una operación que no se puede hacer se rechaza sin cambiar nada; las comprobaciones lo confirman.
            }
            catch (ArgumentException)
            {
            }

            foreach (var (clave, e) in existencias)
            {
                e.Cantidad.ShouldBeGreaterThanOrEqualTo(0);
                e.Reservada.ShouldBeInRange(0, e.Cantidad);
                var segunLibro = movimientos.Where(m => m.Lote == clave.Lote && m.DestinoId == clave.UbicacionId).Sum(m => m.Cantidad)
                                 - movimientos.Where(m => m.Lote == clave.Lote && m.OrigenId == clave.UbicacionId).Sum(m => m.Cantidad);
                e.Cantidad.ShouldBe(segunLibro);
                reservas.Where(r => r.Clave == clave).Sum(r => r.Pendiente).ShouldBe(e.Reservada);
            }
        }
    }
}
