namespace Sga.Dominio.Comun;

/// <summary>
/// Una operación que el dominio no permite: no hay stock, el estado no lo admite, el código no es válido...
/// Lleva un código estable para que la API y el terminal lo traduzcan y lo distingan sin leer el mensaje.
/// </summary>
public sealed class ReglaIncumplida(string codigo, string mensaje) : Exception(mensaje)
{
    public string Codigo { get; } = codigo;
}

/// <summary>El recurso pedido no existe.</summary>
public sealed class NoEncontrado(string mensaje) : Exception(mensaje);
