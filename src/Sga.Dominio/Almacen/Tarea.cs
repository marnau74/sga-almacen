using Sga.Dominio.Comun;
using Sga.Dominio.Stock;

namespace Sga.Dominio.Almacen;

public enum TipoDeTarea
{
    /// <summary>Llevar lo recibido de la playa a su hueco.</summary>
    Ubicar,

    /// <summary>Coger del hueco lo de un pedido y llevarlo al muelle.</summary>
    Picking,

    /// <summary>Bajar de la reserva al hueco de picking.</summary>
    Reponer,
}

public enum EstadoDeTarea
{
    Pendiente,
    EnCurso,
    Hecha,

    /// <summary>Hecha con menos de lo previsto: faltaba mercancía en el hueco.</summary>
    ConFalta,

    Anulada,
}

/// <summary>
/// Un trabajo para un operario del terminal: ir a un sitio, coger algo y dejarlo en otro. Se confirma escaneando.
/// </summary>
public sealed class Tarea
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public TipoDeTarea Tipo { get; private set; }
    public EstadoDeTarea Estado { get; private set; }
    public Guid ArticuloId { get; private set; }
    public string Lote { get; private set; } = "";
    public DateOnly? Caducidad { get; private set; }
    public int Cantidad { get; private set; }
    public int Hecho { get; private set; }
    public Guid OrigenId { get; private set; }
    public Guid DestinoId { get; private set; }

    /// <summary>El documento del que sale: REC-000012 (recepción) u OLE-000003 (oleada).</summary>
    public string Documento { get; private set; } = "";

    public Guid? ReservaId { get; private set; }
    public Guid? PedidoId { get; private set; }
    public Guid? OleadaId { get; private set; }

    /// <summary>Orden en la lista del operario (el del recorrido del hueco donde hay que ir a coger).</summary>
    public int Orden { get; private set; }

    public string? Operario { get; private set; }
    public DateTimeOffset Creada { get; private set; }
    public DateTimeOffset? Terminada { get; private set; }

    private Tarea()
    {
    }

    public static Tarea Ubicar(Guid articuloId, string lote, DateOnly? caducidad, int cantidad, Guid playa,
        Ubicacion destino, string documento, DateTimeOffset ahora) =>
        Nueva(TipoDeTarea.Ubicar, articuloId, lote, caducidad, cantidad, playa, destino.Id, documento, destino.Orden, ahora);

    public static Tarea Reponer(Guid articuloId, string lote, DateOnly? caducidad, int cantidad, Ubicacion reserva,
        Ubicacion picking, DateTimeOffset ahora) =>
        Nueva(TipoDeTarea.Reponer, articuloId, lote, caducidad, cantidad, reserva.Id, picking.Id, "REPOSICIÓN",
            reserva.Orden, ahora);

    public static Tarea Coger(Reserva reserva, Ubicacion hueco, Guid muelle, Guid oleadaId, string documento,
        DateTimeOffset ahora)
    {
        var tarea = Nueva(TipoDeTarea.Picking, reserva.ArticuloId, reserva.Lote, reserva.Caducidad, reserva.Pendiente,
            hueco.Id, muelle, documento, hueco.Orden, ahora);
        tarea.ReservaId = reserva.Id;
        tarea.PedidoId = reserva.PedidoId;
        tarea.OleadaId = oleadaId;
        return tarea;
    }

    private static Tarea Nueva(TipoDeTarea tipo, Guid articuloId, string lote, DateOnly? caducidad, int cantidad,
        Guid origen, Guid destino, string documento, int orden, DateTimeOffset ahora)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cantidad);
        return new Tarea
        {
            Tipo = tipo,
            ArticuloId = articuloId,
            Lote = lote,
            Caducidad = caducidad,
            Cantidad = cantidad,
            OrigenId = origen,
            DestinoId = destino,
            Documento = documento,
            Orden = orden,
            Creada = ahora,
        };
    }

    public bool Abierta => Estado is EstadoDeTarea.Pendiente or EstadoDeTarea.EnCurso;

    /// <summary>El operario la coge. Si otro ya la tiene, no.</summary>
    public void Tomar(string operario)
    {
        if (Estado == EstadoDeTarea.EnCurso && Operario == operario)
        {
            return;
        }
        if (Estado != EstadoDeTarea.Pendiente)
        {
            throw new ReglaIncumplida("tarea-ocupada", "Esta tarea ya la tiene otro operario o está terminada.");
        }
        (Estado, Operario) = (EstadoDeTarea.EnCurso, operario);
    }

    /// <summary>La deja para otro (sin hacer nada).</summary>
    public void Soltar(string operario)
    {
        ComprobarQueEsSuya(operario);
        (Estado, Operario) = (EstadoDeTarea.Pendiente, null);
    }

    /// <summary>
    /// Termina la tarea con lo que de verdad se ha movido. En picking puede ser menos de lo previsto (falta); al
    /// ubicar se puede dejar en otro hueco distinto del sugerido.
    /// </summary>
    public void Terminar(string operario, int cantidad, Guid destino, DateTimeOffset ahora)
    {
        ComprobarQueEsSuya(operario);
        if (cantidad < 0 || cantidad > Cantidad)
        {
            throw new ReglaIncumplida("cantidad-no-valida", $"Esta tarea es de {Cantidad} como mucho.");
        }
        if (cantidad < Cantidad && Tipo != TipoDeTarea.Picking)
        {
            throw new ReglaIncumplida("cantidad-no-valida", $"Hay que mover las {Cantidad} unidades.");
        }
        if (destino != DestinoId && Tipo != TipoDeTarea.Ubicar)
        {
            throw new ReglaIncumplida("destino-no-valido", "Este no es el destino de la tarea.");
        }
        Hecho = cantidad;
        DestinoId = destino;
        Estado = cantidad == Cantidad ? EstadoDeTarea.Hecha : EstadoDeTarea.ConFalta;
        Terminada = ahora;
    }

    public void Anular()
    {
        if (!Abierta)
        {
            throw new ReglaIncumplida("tarea-terminada", "La tarea ya está terminada.");
        }
        Estado = EstadoDeTarea.Anulada;
    }

    private void ComprobarQueEsSuya(string operario)
    {
        if (Estado != EstadoDeTarea.EnCurso || Operario != operario)
        {
            throw new ReglaIncumplida("tarea-no-es-tuya", "Esta tarea no la tienes tú.");
        }
    }
}
