using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Sga.Api.Demo;
using Sga.Aplicacion.Maestros;
using Sga.Infraestructura.Identidad;
using Sga.Infraestructura.Persistencia;

namespace Sga.Api.Infraestructura;

/// <summary>
/// Al arrancar: aplica las migraciones, crea las playas y los roles y, si se configura, el primer administrador y la
/// base de demostración. Todo se puede repetir sin duplicar nada.
/// </summary>
internal static class Inicializacion
{
    public static async Task Preparar(IServiceProvider servicios, IConfiguration configuracion, ILogger registro)
    {
        await using var scope = servicios.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var ct = CancellationToken.None;

        if (configuracion.GetValue("Almacen:MigrarAlArrancar", true))
        {
            await sp.GetRequiredService<SgaContexto>().Database.MigrateAsync(ct);
        }
        await sp.GetRequiredService<ServicioDeMaestros>().AsegurarPlayas(ct);

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var rol in Roles.Todos)
        {
            if (!await roles.RoleExistsAsync(rol))
            {
                await roles.CreateAsync(new IdentityRole(rol));
            }
        }

        // El primer administrador, solo si se da su contraseña (variable de entorno o secretos, nunca en el repositorio).
        var admin = configuracion["Almacen:AdministradorInicial:Usuario"];
        var contrasena = configuracion["Almacen:AdministradorInicial:Contrasena"];
        if (!string.IsNullOrWhiteSpace(admin) && !string.IsNullOrWhiteSpace(contrasena))
        {
            await Usuarios.Asegurar(sp.GetRequiredService<UserManager<Usuario>>(), admin, "Administración", contrasena, Roles.Administrador);
        }

        if (sp.GetRequiredService<IOptions<OpcionesDeDemo>>().Value.Activa)
        {
            await sp.GetRequiredService<SembradorDeDemo>().Sembrar(ct);
            Registro.DemoPreparada(registro);
        }
    }
}

internal static class Usuarios
{
    public static async Task Asegurar(UserManager<Usuario> usuarios, string nombre, string completo, string contrasena, string rol)
    {
        if (await usuarios.FindByNameAsync(nombre) is not null)
        {
            return;
        }
        var usuario = new Usuario { UserName = nombre, NombreCompleto = completo };
        var creado = await usuarios.CreateAsync(usuario, contrasena);
        if (!creado.Succeeded)
        {
            throw new InvalidOperationException($"No se ha podido crear el usuario {nombre}: " +
                                                string.Join(" ", creado.Errors.Select(e => e.Description)));
        }
        await usuarios.AddToRoleAsync(usuario, rol);
    }
}
