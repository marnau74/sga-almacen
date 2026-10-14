using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Sga.Api.Infraestructura;
using Sga.Aplicacion.Maestros;
using Sga.Infraestructura.Identidad;

using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Sga.Api.Tests.Aplicacion))]

namespace Sga.Api.Tests;

/// <summary>La API entera contra un PostgreSQL de Docker, con un usuario de cada rol.</summary>
public sealed class Aplicacion : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Contrasena = "contrasena-de-prueba";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        await Usuarios.Asegurar(usuarios, "jefa", "Jefa", Contrasena, Roles.JefeDeAlmacen);
        await Usuarios.Asegurar(usuarios, "operario", "Operario", Contrasena, Roles.Operario);
        await Usuarios.Asegurar(usuarios, "oficina", "Oficina", Contrasena, Roles.Administracion);
        await Usuarios.Asegurar(usuarios, "bloqueado", "Bloqueado", Contrasena, Roles.Operario);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Pruebas");
        builder.UseSetting("ConnectionStrings:Sga", _postgres.GetConnectionString());
        builder.UseSetting("Demo:Activa", "false");
        // Todas las peticiones de las pruebas llegan sin IP: compartirían el límite de intentos de entrar.
        builder.UseSetting("Seguridad:EntradasPorMinuto", "1000");
    }

    /// <summary>Un cliente con la sesión de <paramref name="usuario"/> ya iniciada (la cookie la guarda el cliente).</summary>
    public async Task<HttpClient> Con(string usuario)
    {
        var cliente = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var respuesta = await cliente.PostAsJsonAsync("/api/sesion", new { usuario, contrasena = Contrasena }, TestContext.Current.CancellationToken);
        respuesta.EnsureSuccessStatusCode();
        return cliente;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

public class ApiTests(Aplicacion app)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string[] HuecoDelConteo = ["R-90-01-0"];

    private static async Task<JsonElement> Problema(HttpResponseMessage respuesta)
    {
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task Sin_sesion_no_se_ve_nada_pero_la_salud_y_el_front_si()
    {
        var anonimo = app.CreateClient();
        (await anonimo.GetAsync("/api/articulos", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonimo.GetAsync("/api/panel", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonimo.GetAsync("/health", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Una_contrasena_equivocada_no_dice_si_el_usuario_existe()
    {
        var cliente = app.CreateClient();
        var noExiste = await cliente.PostAsJsonAsync("/api/sesion", new { usuario = "nadie", contrasena = "lo-que-sea-12" }, Ct);
        var malaClave = await cliente.PostAsJsonAsync("/api/sesion", new { usuario = "jefa", contrasena = "lo-que-sea-12" }, Ct);

        noExiste.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        malaClave.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Problema(noExiste)).GetProperty("detail").GetString().ShouldBe((await Problema(malaClave)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Tras_cinco_intentos_fallidos_la_cuenta_se_bloquea_un_rato()
    {
        var cliente = app.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await cliente.PostAsJsonAsync("/api/sesion", new { usuario = "bloqueado", contrasena = "equivocada-123" }, Ct);
        }
        var buena = await cliente.PostAsJsonAsync("/api/sesion", new { usuario = "bloqueado", contrasena = Aplicacion.Contrasena }, Ct);
        buena.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Problema(buena)).GetProperty("detail").GetString()!.ShouldContain("Demasiados intentos");
    }

    [Fact]
    public async Task La_cookie_de_sesion_es_httponly_y_samesite_strict()
    {
        var cliente = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var respuesta = await cliente.PostAsJsonAsync("/api/sesion", new { usuario = "jefa", contrasena = Aplicacion.Contrasena }, Ct);
        var cookie = respuesta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("sga=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
    }

    [Fact]
    public async Task Cada_rol_hace_solo_lo_suyo()
    {
        var operario = await app.Con("operario");
        var oficina = await app.Con("oficina");
        var jefa = await app.Con("jefa");

        // El operario no lanza oleadas ni da de alta pedidos ni ve el panel.
        (await operario.PostAsJsonAsync("/api/pedidos/oleadas", new { pedidos = Array.Empty<Guid>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await operario.PostAsJsonAsync("/api/pedidos", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await operario.GetAsync("/api/panel", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // La oficina no trabaja en el terminal ni aprueba ajustes.
        (await oficina.GetAsync("/api/tareas", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await oficina.PostAsync($"/api/conteos/{Guid.NewGuid()}/aprobacion", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // La jefa puede de todo.
        (await jefa.GetAsync("/api/panel", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await jefa.GetAsync("/api/tareas", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Los_datos_mal_formados_y_las_reglas_del_dominio_salen_como_problem_json_con_su_codigo()
    {
        var jefa = await app.Con("jefa");

        var sinNombre = await jefa.PostAsJsonAsync("/api/articulos", new { ean = "8410297012150", nombre = "", unidadesPorCaja = 6 }, Ct);
        sinNombre.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problema(sinNombre)).GetProperty("codigo").GetString().ShouldBe("datos-no-validos");

        var eanMalo = await jefa.PostAsJsonAsync("/api/articulos", new { ean = "8410297012151", nombre = "Leche", unidadesPorCaja = 6 }, Ct);
        eanMalo.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Problema(eanMalo)).GetProperty("codigo").GetString().ShouldBe("gtin-no-valido");

        (await jefa.GetAsync($"/api/pedidos/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task El_conteo_es_ciego_para_quien_cuenta()
    {
        var jefa = await app.Con("jefa");
        using (var scope = app.Services.CreateScope())
        {
            var maestros = scope.ServiceProvider.GetRequiredService<ServicioDeMaestros>();
            await maestros.CrearEstanteria(new NuevaEstanteria(Dominio.Almacen.TipoDeZona.Reserva, 90, 1, 1), Ct);
        }
        var creado = await jefa.PostAsJsonAsync("/api/conteos", new { ubicaciones = HuecoDelConteo }, Ct);
        creado.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await creado.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var operario = await app.Con("operario");
        var leche = await jefa.PostAsJsonAsync("/api/articulos",
            new { ean = "8480000103826", nombre = "Leche semidesnatada", unidadesPorCaja = 6, controlDeLote = false, controlDeCaducidad = false }, Ct);
        leche.EnsureSuccessStatusCode();
        (await operario.PostAsJsonAsync($"/api/conteos/{id}/cantidades",
            new { codigoDeUbicacion = "R-90-01-0", codigoDeArticulo = "8480000103826", cantidad = 3 }, Ct)).EnsureSuccessStatusCode();

        var paraElOperario = await operario.GetFromJsonAsync<JsonElement>($"/api/conteos/{id}", Ct);
        var paraLaJefa = await jefa.GetFromJsonAsync<JsonElement>($"/api/conteos/{id}", Ct);
        paraElOperario.GetProperty("lineas")[0].GetProperty("esperada").ValueKind.ShouldBe(JsonValueKind.Null);
        paraLaJefa.GetProperty("lineas")[0].GetProperty("esperada").GetInt32().ShouldBe(0);
        paraLaJefa.GetProperty("lineas")[0].GetProperty("diferencia").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Las_respuestas_llevan_las_cabeceras_de_seguridad()
    {
        var respuesta = await app.CreateClient().GetAsync("/health", Ct);
        respuesta.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        respuesta.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("frame-ancestors 'none'");
    }
}
