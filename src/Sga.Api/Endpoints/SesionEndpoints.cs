using System.Security.Claims;

using Microsoft.AspNetCore.Identity;

using Sga.Infraestructura.Identidad;

namespace Sga.Api.Endpoints;

internal sealed record Entrada(string Usuario, string Contrasena);

internal sealed record Sesion(string Usuario, string Nombre, IReadOnlyList<string> Roles);

internal static class SesionEndpoints
{
    public static void MapSesion(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/sesion").WithTags("Sesión");

        grupo.MapPost("/", async (Entrada entrada, SignInManager<Usuario> sesiones, UserManager<Usuario> usuarios) =>
            {
                // Bloqueo tras varios intentos fallidos (lo configura Identity) y el mismo mensaje exista o no el usuario.
                var resultado = await sesiones.PasswordSignInAsync(entrada.Usuario, entrada.Contrasena, isPersistent: false,
                    lockoutOnFailure: true);
                if (!resultado.Succeeded)
                {
                    return Results.Problem(resultado.IsLockedOut
                            ? "Demasiados intentos. Espera unos minutos."
                            : "Usuario o contraseña incorrectos.",
                        statusCode: StatusCodes.Status401Unauthorized, title: "No se ha podido entrar");
                }
                var usuario = (await usuarios.FindByNameAsync(entrada.Usuario))!;
                return Results.Ok(new Sesion(usuario.UserName!, usuario.NombreCompleto, [.. await usuarios.GetRolesAsync(usuario)]));
            })
            .AllowAnonymous()
            .RequireRateLimiting("entrar");

        grupo.MapGet("/", (ClaimsPrincipal yo, UserManager<Usuario> usuarios) =>
            Results.Ok(new Sesion(yo.Identity!.Name!, yo.FindFirstValue("nombre") ?? yo.Identity.Name!,
                yo.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList())));

        grupo.MapDelete("/", async (SignInManager<Usuario> sesiones) =>
        {
            await sesiones.SignOutAsync();
            return Results.NoContent();
        });
    }
}
