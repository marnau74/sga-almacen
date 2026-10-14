using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sga.Aplicacion.Consultas;
using Sga.Aplicacion.Salidas;
using Sga.Aplicacion.Tareas;
using Sga.Dominio.Almacen;
using Sga.Dominio.Comun;
using Sga.Dominio.Salidas;
using Sga.Infraestructura.Persistencia;

namespace Sga.Integracion.Tests;

/// <summary>Lo que solo pasa cuando dos personas hacen algo a la vez sobre el mismo documento.</summary>
public class CarrerasTests(ServidorPostgres servidor)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    /// <summary>
    /// Dos operarios terminan a la vez las dos últimas líneas de un pedido, cada uno en un hueco: el pedido queda
    /// preparado y la oleada, terminada (sin el bloqueo, cada uno veía la tarea del otro abierta y nadie los cerraba).
    /// </summary>
    [Fact]
    public async Task Las_dos_ultimas_lineas_a_la_vez_terminan_el_pedido_y_la_oleada()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Sal, "", null, 10);
        await e.Entrar(e.LecheHacendado, "L1", new DateOnly(2027, 1, 1), 10);
        var pedido = await e.Pedir(e.Tienda, (e.Sal, 2), (e.LecheHacendado, 2));
        await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([pedido.Id], Ct));
        await e.Con<ServicioDeSalidas, Oleada>(s => s.LanzarOleada([pedido.Id], Ct));
        var tareas = await e.Con<Consultas, IReadOnlyList<TareaParaElTerminal>>(c => c.TareasPara("nadie", TipoDeTarea.Picking, Ct));
        tareas.Count.ShouldBe(2);

        await Task.WhenAll(tareas.Select((t, i) => e.Hacer(t.Id, $"operario{i}",
            c => new Confirmacion(c.Origen, t.Ean, 2, null))));

        (await e.Leer(c => c.Pedidos.SingleAsync(p => p.Id == pedido.Id))).Estado.ShouldBe(EstadoDePedido.Preparado);
        (await e.Leer(c => c.Oleadas.SingleAsync())).Estado.ShouldBe(EstadoDeOleada.Terminada);
        await e.ComprobarLibro();
    }

    /// <summary>
    /// Las líneas del pedido están en otra tabla: cambiar solo una línea también tiene que comprobar la versión del
    /// pedido, o el segundo en guardar pisa al primero sin enterarse.
    /// </summary>
    [Fact]
    public async Task Cambiar_solo_las_lineas_de_un_pedido_comprueba_su_version()
    {
        await using var e = await Escenario.Crear(servidor);
        var pedido = await e.Pedir(e.Tienda, (e.Sal, 5));

        await e.En(async sp1 =>
        {
            await e.En(async sp2 =>
            {
                var primero = sp1.GetRequiredService<SgaContexto>();
                var segundo = sp2.GetRequiredService<SgaContexto>();
                var a = await primero.Pedidos.SingleAsync(p => p.Id == pedido.Id, Ct);
                var b = await segundo.Pedidos.SingleAsync(p => p.Id == pedido.Id, Ct);

                a.Asignado(1, 3);
                await primero.SaveChangesAsync(Ct);
                b.Asignado(1, 4);
                await Should.ThrowAsync<DbUpdateConcurrencyException>(() => segundo.SaveChangesAsync(Ct));
                return true;
            });
            return true;
        });
        (await e.Leer(c => c.Pedidos.SingleAsync(p => p.Id == pedido.Id))).Linea(1).Asignada.ShouldBe(3);
    }

    /// <summary>Una tarea que ya existía cuando se empezó a contar su hueco no se puede confirmar hasta que acabe.</summary>
    [Fact]
    public async Task No_se_confirma_una_tarea_de_un_hueco_que_se_esta_contando()
    {
        await using var e = await Escenario.Crear(servidor);
        await e.Entrar(e.Sal, "", null, 10);
        var pedido = await e.Pedir(e.Tienda, (e.Sal, 2));
        await e.Con<ServicioDeSalidas, IReadOnlyList<ResultadoDeAsignacion>>(s => s.Asignar([pedido.Id], Ct));
        await e.Con<ServicioDeSalidas, Oleada>(s => s.LanzarOleada([pedido.Id], Ct));
        var tarea = (await e.Con<Consultas, IReadOnlyList<TareaParaElTerminal>>(c => c.TareasPara("x", TipoDeTarea.Picking, Ct))).Single();
        // Como si el conteo se hubiera abierto a la vez que se lanzaba la oleada.
        await e.Leer(async c =>
        {
            (await c.Ubicaciones.SingleAsync(u => u.Codigo == "P-01-03-1")).Bloquear();
            return await c.SaveChangesAsync();
        });

        var error = await Should.ThrowAsync<ReglaIncumplida>(() =>
            e.Hacer(tarea.Id, "ana", c => new Confirmacion(c.Origen, Escenario.EanSal, 2, null)));
        error.Codigo.ShouldBe("ubicacion-en-recuento");
        await e.ComprobarLibro();
    }
}
