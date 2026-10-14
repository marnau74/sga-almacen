using System.Globalization;
using Sga.Dominio.Comun;

namespace Sga.Dominio.Almacen;

public enum TipoDeZona
{
    /// <summary>Donde se deja lo que llega hasta que se ubica.</summary>
    Recepcion,

    /// <summary>Huecos de palé, en altura: un artículo y un lote por hueco.</summary>
    Reserva,

    /// <summary>Huecos a mano, cada uno con su artículo fijo y su mínimo y máximo para reponer.</summary>
    Picking,

    /// <summary>Muelle: lo preparado espera aquí a ser embalado y cargado.</summary>
    Expedicion,

    /// <summary>Mercancía dañada o pendiente de revisar: no se sirve.</summary>
    Cuarentena,
}

/// <summary>
/// Un sitio del almacén donde puede haber stock. Las zonas de recepción, expedición y cuarentena tienen una sola
/// ubicación (la playa); las de reserva y picking, una por hueco de estantería.
/// </summary>
public sealed class Ubicacion
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Lo que lleva la etiqueta del hueco y se escanea: P-01-04-1 (zona, pasillo, módulo, nivel).</summary>
    public string Codigo { get; private set; } = "";

    public TipoDeZona Zona { get; private set; }
    public int Pasillo { get; private set; }
    public int Modulo { get; private set; }
    public int Nivel { get; private set; }

    /// <summary>Posición en el recorrido del operario: las rutas de picking se ordenan por aquí.</summary>
    public int Orden { get; private set; }

    /// <summary>En picking, el único artículo que va en este hueco.</summary>
    public Guid? ArticuloFijoId { get; private set; }

    /// <summary>En picking, por debajo de este disponible se pide reposición desde la reserva.</summary>
    public int? Minimo { get; private set; }

    /// <summary>En picking, lo que cabe: la reposición llena hasta aquí.</summary>
    public int? Maximo { get; private set; }

    /// <summary>Bloqueada mientras se cuenta: ni se coge ni se deja nada.</summary>
    public bool Bloqueada { get; private set; }

    private Ubicacion()
    {
    }

    public static Ubicacion Playa(TipoDeZona zona, string codigo, int orden)
    {
        if (zona is TipoDeZona.Reserva or TipoDeZona.Picking)
        {
            throw new ArgumentException("La reserva y el picking se organizan en huecos.", nameof(zona));
        }
        return new Ubicacion { Zona = zona, Codigo = codigo, Orden = orden };
    }

    public static Ubicacion Hueco(TipoDeZona zona, int pasillo, int modulo, int nivel)
    {
        if (zona is not (TipoDeZona.Reserva or TipoDeZona.Picking))
        {
            throw new ArgumentException("Solo la reserva y el picking tienen huecos.", nameof(zona));
        }
        if (pasillo is < 1 or > 99 || modulo is < 1 or > 99 || nivel is < 0 or > 9)
        {
            throw new ReglaIncumplida("hueco-no-valido", "Pasillo y módulo van de 1 a 99 y el nivel de 0 a 9.");
        }
        var letra = zona == TipoDeZona.Reserva ? "R" : "P";
        return new Ubicacion
        {
            Zona = zona,
            Pasillo = pasillo,
            Modulo = modulo,
            Nivel = nivel,
            Codigo = string.Create(CultureInfo.InvariantCulture, $"{letra}-{pasillo:00}-{modulo:00}-{nivel}"),
            Orden = Recorrido.Orden(zona, pasillo, modulo, nivel),
        };
    }

    public void AsignarArticulo(Guid articuloId, int minimo, int maximo)
    {
        if (Zona != TipoDeZona.Picking)
        {
            throw new ReglaIncumplida("no-es-picking", "Solo los huecos de picking tienen artículo fijo.");
        }
        if (minimo < 0 || maximo < 1 || minimo >= maximo)
        {
            throw new ReglaIncumplida("minimo-maximo", "El mínimo tiene que ser menor que el máximo.");
        }
        (ArticuloFijoId, Minimo, Maximo) = (articuloId, minimo, maximo);
    }

    public bool AdmiteSalidas => !Bloqueada && Zona is TipoDeZona.Reserva or TipoDeZona.Picking;

    public void Bloquear() => Bloqueada = true;

    public void Desbloquear() => Bloqueada = false;
}

/// <summary>
/// El orden en que el operario recorre el almacén: los pasillos uno detrás de otro, en serpentina (los impares de
/// principio a fin y los pares de vuelta) para no desandar, y el picking antes que la reserva.
/// </summary>
public static class Recorrido
{
    public static int Orden(TipoDeZona zona, int pasillo, int modulo, int nivel)
    {
        var tramo = pasillo % 2 == 1 ? modulo : 100 - modulo;
        var bloque = zona == TipoDeZona.Picking ? 0 : 1_000_000;
        return bloque + (pasillo * 10_000) + (tramo * 10) + nivel;
    }
}
