using Sga.Dominio.Comun;

namespace Sga.Dominio.Almacen;

/// <summary>Un hueco con lo que tiene ahora (de cualquier artículo y lote).</summary>
public sealed record HuecoOcupado(Ubicacion Ubicacion, int Unidades);

/// <summary>A dónde va cuánto.</summary>
public sealed record Destino(Ubicacion Ubicacion, int Cantidad);

/// <summary>
/// Dónde dejar lo que llega. Primero el hueco de picking del artículo, hasta su máximo (así no hay que reponer
/// enseguida); lo que sobra, a un hueco de reserva vacío (un palé, un artículo y un lote por hueco), el más cercano
/// al picking del artículo para que la reposición sea corta.
/// </summary>
public static class SugerenciaDeUbicacion
{
    public static IReadOnlyList<Destino> Sugerir(Guid articuloId, int cantidad, IEnumerable<HuecoOcupado> huecos)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cantidad);
        var lista = huecos.Where(h => !h.Ubicacion.Bloqueada).ToList();
        var resultado = new List<Destino>();
        var falta = cantidad;

        var picking = lista
            .Where(h => h.Ubicacion.Zona == TipoDeZona.Picking && h.Ubicacion.ArticuloFijoId == articuloId)
            .OrderBy(h => h.Ubicacion.Orden)
            .ToList();
        foreach (var hueco in picking)
        {
            var cabe = Math.Max(0, hueco.Ubicacion.Maximo!.Value - hueco.Unidades);
            var deja = Math.Min(cabe, falta);
            if (deja > 0)
            {
                resultado.Add(new Destino(hueco.Ubicacion, deja));
                falta -= deja;
            }
            if (falta == 0)
            {
                return resultado;
            }
        }

        var referencia = picking.FirstOrDefault()?.Ubicacion;
        var vacio = lista
            .Where(h => h.Ubicacion.Zona == TipoDeZona.Reserva && h.Unidades == 0)
            .OrderBy(h => referencia is null ? 0 : Distancia(referencia, h.Ubicacion))
            // Mejor abajo: se baja antes y con menos riesgo.
            .ThenBy(h => h.Ubicacion.Nivel)
            .ThenBy(h => h.Ubicacion.Orden)
            .FirstOrDefault();
        if (vacio is null)
        {
            throw new ReglaIncumplida("sin-hueco", "No queda ningún hueco de reserva libre.");
        }
        resultado.Add(new Destino(vacio.Ubicacion, falta));
        return resultado;
    }

    /// <summary>Pasillos de diferencia (cruzar de pasillo cuesta más) y módulos dentro del pasillo.</summary>
    private static int Distancia(Ubicacion desde, Ubicacion hasta) =>
        (Math.Abs(desde.Pasillo - hasta.Pasillo) * 100) + Math.Abs(desde.Modulo - hasta.Modulo);
}
