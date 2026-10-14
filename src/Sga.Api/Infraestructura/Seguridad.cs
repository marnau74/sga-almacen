using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

using Sga.Aplicacion.Abstracciones;
using Sga.Infraestructura.Identidad;

namespace Sga.Api.Infraestructura;

/// <summary>Quién hace cada cosa: el usuario de la cookie de sesión.</summary>
internal sealed class UsuarioDeLaPeticion(IHttpContextAccessor http) : IUsuarioActual
{
    public string Nombre => Demo.UsuarioDeDemo.Actual.Value ?? http.HttpContext?.User.Identity?.Name ?? "sistema";
}

/// <summary>Quién puede qué. Cada acción se comprueba aquí, en la API: esconder un botón en el front no protege nada.</summary>
internal static class Politicas
{
    /// <summary>Consultas y altas de oficina: pedidos, recepciones, stock.</summary>
    public const string Oficina = "oficina";

    /// <summary>El trabajo del terminal: recibir, ubicar, preparar, embalar, contar.</summary>
    public const string Terminal = "terminal";

    /// <summary>Decisiones: lanzar oleadas, aprobar ajustes, bloquear lotes, configurar el almacén.</summary>
    public const string Jefe = "jefe";

    public static void Configurar(AuthorizationOptions opciones)
    {
        opciones.AddPolicy(Oficina, p => p.RequireRole(Roles.Administrador, Roles.JefeDeAlmacen, Roles.Administracion));
        opciones.AddPolicy(Terminal, p => p.RequireRole(Roles.Administrador, Roles.JefeDeAlmacen, Roles.Operario));
        opciones.AddPolicy(Jefe, p => p.RequireRole(Roles.Administrador, Roles.JefeDeAlmacen));
        // Sin política explícita, hace falta sesión.
        opciones.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }
}

/// <summary>Avisos en directo al panel y a los terminales. Solo dicen «ha cambiado esto»: el front vuelve a pedir.</summary>
[Authorize]
internal sealed class CentroDeAvisos : Hub;

internal sealed class AvisosPorSignalR(IHubContext<CentroDeAvisos> centro) : IAvisos
{
    public Task Publicar(Aviso aviso, CancellationToken ct) =>
        centro.Clients.All.SendAsync("aviso", new { tipo = aviso.Tipo.ToString(), documento = aviso.Documento }, ct);
}
