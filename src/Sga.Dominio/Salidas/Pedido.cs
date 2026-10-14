using Sga.Dominio.Codigos;
using Sga.Dominio.Comun;

namespace Sga.Dominio.Salidas;

public enum EstadoDePedido
{
    Pendiente,
    Asignado,

    /// <summary>No hay stock para todo: se prepara lo que hay y el resto queda como rotura.</summary>
    AsignadoParcial,

    EnPreparacion,
    Preparado,
    Embalado,
    Expedido,
    Cancelado,
}

public sealed class LineaDePedido
{
    public int Numero { get; internal set; }
    public Guid ArticuloId { get; internal set; }
    public int Pedida { get; internal set; }
    public int Asignada { get; internal set; }
    public int Preparada { get; internal set; }
    public int Rotura => Pedida - Preparada;
}

/// <summary>Lo que se ha cogido de verdad para el pedido, con su lote (para la trazabilidad y el embalaje).</summary>
public sealed class Preparado
{
    public Guid ArticuloId { get; internal set; }
    public string Lote { get; internal set; } = "";
    public DateOnly? Caducidad { get; internal set; }
    public int Cantidad { get; internal set; }
    public int Embalado { get; internal set; }
}

/// <summary>Una caja o un palé del pedido, con su etiqueta SSCC.</summary>
public sealed class Bulto
{
    private readonly List<ContenidoDeBulto> _contenido = [];

    public int Numero { get; internal set; }
    public string Sscc { get; internal set; } = "";
    public bool Cerrado { get; internal set; }
    public IReadOnlyList<ContenidoDeBulto> Contenido => _contenido;
    internal List<ContenidoDeBulto> ContenidoModificable => _contenido;
}

public sealed class ContenidoDeBulto
{
    public Guid ArticuloId { get; internal set; }
    public string Lote { get; internal set; } = "";
    public int Cantidad { get; internal set; }
}

/// <summary>Un pedido de un cliente, desde que llega hasta que sale en el camión.</summary>
public sealed class Pedido
{
    private readonly List<LineaDePedido> _lineas = [];
    private readonly List<Preparado> _preparados = [];
    private readonly List<Bulto> _bultos = [];

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Numero { get; private set; } = "";
    public Guid ClienteId { get; private set; }
    public DateOnly FechaDeServicio { get; private set; }
    public EstadoDePedido Estado { get; private set; }
    public Guid? OleadaId { get; private set; }
    public DateTimeOffset Creado { get; private set; }
    public DateTimeOffset? Expedido { get; private set; }
    public IReadOnlyList<LineaDePedido> Lineas => _lineas;
    public IReadOnlyList<Preparado> Preparados => _preparados;
    public IReadOnlyList<Bulto> Bultos => _bultos;

    private Pedido()
    {
    }

    public static Pedido Crear(string numero, Guid clienteId, DateOnly fechaDeServicio,
        IEnumerable<(Guid ArticuloId, int Cantidad)> lineas, DateTimeOffset ahora)
    {
        var pedido = new Pedido
        {
            Numero = numero,
            ClienteId = clienteId,
            FechaDeServicio = fechaDeServicio,
            Creado = ahora,
        };
        foreach (var (articuloId, cantidad) in lineas)
        {
            if (cantidad <= 0)
            {
                throw new ReglaIncumplida("cantidad-no-valida", "Cada línea tiene que pedir al menos una unidad.");
            }
            if (pedido._lineas.Any(l => l.ArticuloId == articuloId))
            {
                throw new ReglaIncumplida("linea-repetida", "Un artículo va en una sola línea.");
            }
            pedido._lineas.Add(new LineaDePedido
            {
                Numero = pedido._lineas.Count + 1,
                ArticuloId = articuloId,
                Pedida = cantidad,
            });
        }
        if (pedido._lineas.Count == 0)
        {
            throw new ReglaIncumplida("sin-lineas", "El pedido necesita al menos una línea.");
        }
        if (fechaDeServicio < DateOnly.FromDateTime(ahora.UtcDateTime.Date))
        {
            throw new ReglaIncumplida("fecha-pasada", "La fecha de servicio no puede ser anterior a hoy.");
        }
        return pedido;
    }

    public LineaDePedido Linea(int numero) =>
        _lineas.FirstOrDefault(l => l.Numero == numero)
        ?? throw new ReglaIncumplida("linea-no-existe", $"El pedido no tiene la línea {numero}.");

    // Asignación de stock

    public void Asignado(int linea, int cantidad)
    {
        ComprobarEstado(EstadoDePedido.Pendiente, EstadoDePedido.AsignadoParcial, EstadoDePedido.EnPreparacion);
        var l = Linea(linea);
        if (cantidad <= 0 || l.Asignada + cantidad > l.Pedida)
        {
            throw new ReglaIncumplida("cantidad-no-valida", "No se puede asignar más de lo pedido.");
        }
        l.Asignada += cantidad;
    }

    /// <summary>Tras repartir el stock: asignado entero, en parte, o nada (sigue pendiente).</summary>
    public void TerminarAsignacion()
    {
        ComprobarEstado(EstadoDePedido.Pendiente, EstadoDePedido.AsignadoParcial);
        if (_lineas.All(l => l.Asignada == 0))
        {
            Estado = EstadoDePedido.Pendiente;
            return;
        }
        Estado = _lineas.All(l => l.Asignada == l.Pedida) ? EstadoDePedido.Asignado : EstadoDePedido.AsignadoParcial;
    }

    // Preparación

    public void Lanzar(Guid oleadaId)
    {
        ComprobarEstado(EstadoDePedido.Asignado, EstadoDePedido.AsignadoParcial);
        OleadaId = oleadaId;
        Estado = EstadoDePedido.EnPreparacion;
    }

    /// <summary>El operario ha cogido <paramref name="cantidad"/> de la línea, de este lote.</summary>
    public void Cogido(int linea, string lote, DateOnly? caducidad, int cantidad)
    {
        ComprobarEstado(EstadoDePedido.EnPreparacion);
        var l = Linea(linea);
        if (cantidad <= 0 || l.Preparada + cantidad > l.Asignada)
        {
            throw new ReglaIncumplida("cantidad-no-valida", "Se ha cogido más de lo asignado.");
        }
        l.Preparada += cantidad;
        var preparado = _preparados.FirstOrDefault(p => p.ArticuloId == l.ArticuloId && p.Lote == lote);
        if (preparado is null)
        {
            _preparados.Add(new Preparado { ArticuloId = l.ArticuloId, Lote = lote, Caducidad = caducidad, Cantidad = cantidad });
        }
        else
        {
            preparado.Cantidad += cantidad;
        }
    }

    /// <summary>En el hueco no había todo lo asignado: esa parte deja de estar asignada.</summary>
    public void Falta(int linea, int cantidad)
    {
        ComprobarEstado(EstadoDePedido.EnPreparacion);
        var l = Linea(linea);
        if (cantidad <= 0 || cantidad > l.Asignada - l.Preparada)
        {
            throw new ReglaIncumplida("cantidad-no-valida", "La falta es mayor que lo pendiente de coger.");
        }
        l.Asignada -= cantidad;
    }

    /// <summary>Ya no queda nada por coger.</summary>
    public void TerminarPreparacion()
    {
        ComprobarEstado(EstadoDePedido.EnPreparacion);
        if (_lineas.Any(l => l.Preparada < l.Asignada))
        {
            throw new ReglaIncumplida("preparacion-pendiente", "Todavía quedan líneas por coger.");
        }
        Estado = EstadoDePedido.Preparado;
    }

    // Embalaje y expedición

    public Bulto AbrirBulto(Sscc sscc)
    {
        ComprobarEstado(EstadoDePedido.Preparado);
        if (_bultos.Any(b => !b.Cerrado))
        {
            throw new ReglaIncumplida("bulto-abierto", "Cierra el bulto que está abierto antes de abrir otro.");
        }
        var bulto = new Bulto { Numero = _bultos.Count + 1, Sscc = sscc.Valor };
        _bultos.Add(bulto);
        return bulto;
    }

    public void Meter(Guid articuloId, string lote, int cantidad)
    {
        ComprobarEstado(EstadoDePedido.Preparado);
        var bulto = _bultos.LastOrDefault(b => !b.Cerrado)
                    ?? throw new ReglaIncumplida("sin-bulto", "Abre un bulto primero.");
        var preparado = _preparados.FirstOrDefault(p => p.ArticuloId == articuloId && p.Lote == lote);
        if (preparado is null || cantidad <= 0 || preparado.Embalado + cantidad > preparado.Cantidad)
        {
            throw new ReglaIncumplida("no-esta-preparado", "Eso no está preparado para este pedido (o ya está embalado).");
        }
        preparado.Embalado += cantidad;
        var contenido = bulto.Contenido.FirstOrDefault(c => c.ArticuloId == articuloId && c.Lote == lote);
        if (contenido is null)
        {
            bulto.ContenidoModificable.Add(new ContenidoDeBulto { ArticuloId = articuloId, Lote = lote, Cantidad = cantidad });
        }
        else
        {
            contenido.Cantidad += cantidad;
        }
    }

    public Bulto CerrarBulto()
    {
        var bulto = _bultos.LastOrDefault(b => !b.Cerrado)
                    ?? throw new ReglaIncumplida("sin-bulto", "No hay ningún bulto abierto.");
        if (bulto.Contenido.Count == 0)
        {
            throw new ReglaIncumplida("bulto-vacio", "Un bulto vacío no se cierra: mete algo o descártalo.");
        }
        bulto.Cerrado = true;
        if (_preparados.All(p => p.Embalado == p.Cantidad))
        {
            Estado = EstadoDePedido.Embalado;
        }
        return bulto;
    }

    public void Expedir(DateTimeOffset ahora)
    {
        ComprobarEstado(EstadoDePedido.Embalado);
        Estado = EstadoDePedido.Expedido;
        Expedido = ahora;
    }

    public bool SePuedeCancelar =>
        Estado is EstadoDePedido.Pendiente or EstadoDePedido.Asignado or EstadoDePedido.AsignadoParcial;

    /// <summary>Solo antes de empezar a prepararlo. Las reservas las libera la aplicación.</summary>
    public void Cancelar()
    {
        if (!SePuedeCancelar)
        {
            throw new ReglaIncumplida("no-se-puede-cancelar", "Un pedido que ya se está preparando no se cancela.");
        }
        Estado = EstadoDePedido.Cancelado;
        foreach (var linea in _lineas)
        {
            linea.Asignada = 0;
        }
    }

    private void ComprobarEstado(params EstadoDePedido[] admitidos)
    {
        if (!admitidos.Contains(Estado))
        {
            throw new ReglaIncumplida("estado-no-valido", $"El pedido {Numero} está {Estado} y eso no se puede hacer ahora.");
        }
    }
}

public enum EstadoDeOleada
{
    EnCurso,
    Terminada,
}

/// <summary>Un grupo de pedidos que se preparan a la vez, en una sola pasada por el almacén.</summary>
public sealed class Oleada
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Numero { get; private set; } = "";
    public EstadoDeOleada Estado { get; private set; }
    public DateTimeOffset Lanzada { get; private set; }
    public DateTimeOffset? Terminada { get; private set; }

    private Oleada()
    {
    }

    public static Oleada Lanzar(string numero, DateTimeOffset ahora) => new() { Numero = numero, Lanzada = ahora };

    public void Terminar(DateTimeOffset ahora)
    {
        Estado = EstadoDeOleada.Terminada;
        Terminada = ahora;
    }
}
