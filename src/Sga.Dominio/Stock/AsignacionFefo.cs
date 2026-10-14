using Sga.Dominio.Almacen;

namespace Sga.Dominio.Stock;

/// <summary>Una existencia que podría servir una línea, con lo que hace falta saber de su ubicación.</summary>
public sealed record Candidata(Existencia Existencia, TipoDeZona Zona, int Orden, bool UbicacionBloqueada,
    bool LoteBloqueado);

/// <summary>De qué existencia sale cuánto.</summary>
public sealed record Asignada(Existencia Existencia, int Cantidad);

/// <summary>
/// Decide de dónde sale una línea de pedido: FEFO (primero lo que antes caduca) entre lo que el cliente acepta.
/// </summary>
public static class AsignacionFefo
{
    /// <param name="fechaDeServicio">El día que el cliente recibe el pedido.</param>
    /// <param name="diasMinimosDeVidaUtil">Lo que le tiene que quedar al producto ese día.</param>
    public static IReadOnlyList<Asignada> Asignar(int cantidad, IEnumerable<Candidata> candidatas,
        DateOnly fechaDeServicio, int diasMinimosDeVidaUtil)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cantidad);
        var caducaComoPronto = fechaDeServicio.AddDays(diasMinimosDeVidaUtil);
        var validas = candidatas
            .Where(c => c.Zona is TipoDeZona.Picking or TipoDeZona.Reserva)
            .Where(c => !c.UbicacionBloqueada && !c.LoteBloqueado && c.Existencia.Disponible > 0)
            // Sin caducidad, vale siempre; con caducidad, solo si llega con la vida útil que pide el cliente.
            .Where(c => c.Existencia.Caducidad is null || c.Existencia.Caducidad.Value >= caducaComoPronto)
            // FEFO; a igual caducidad, el picking antes que la reserva (no obliga a bajar un palé) y el recorrido.
            .OrderBy(c => c.Existencia.Caducidad ?? DateOnly.MaxValue)
            .ThenBy(c => c.Zona == TipoDeZona.Picking ? 0 : 1)
            .ThenBy(c => c.Orden);

        var resultado = new List<Asignada>();
        var falta = cantidad;
        foreach (var candidata in validas)
        {
            var toma = Math.Min(falta, candidata.Existencia.Disponible);
            resultado.Add(new Asignada(candidata.Existencia, toma));
            falta -= toma;
            if (falta == 0)
            {
                break;
            }
        }
        return resultado;
    }
}
