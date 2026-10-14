using Microsoft.AspNetCore.Identity;

namespace Sga.Infraestructura.Identidad;

public sealed class Usuario : IdentityUser
{
    public string NombreCompleto { get; set; } = "";
}

/// <summary>Los roles del almacén.</summary>
public static class Roles
{
    public const string Administrador = "Administrador";

    /// <summary>Configura el almacén, lanza oleadas, aprueba ajustes y ve el panel.</summary>
    public const string JefeDeAlmacen = "JefeDeAlmacen";

    /// <summary>Trabaja en el terminal: recibe, ubica, prepara, embala y cuenta.</summary>
    public const string Operario = "Operario";

    /// <summary>Oficina: da de alta pedidos y recepciones y consulta el stock.</summary>
    public const string Administracion = "Administracion";

    public static readonly string[] Todos = [Administrador, JefeDeAlmacen, Operario, Administracion];
}
