using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

using FluentValidation;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Scalar.AspNetCore;

using Sga.Api.Demo;
using Sga.Api.Endpoints;
using Sga.Api.Infraestructura;
using Sga.Aplicacion;
using Sga.Aplicacion.Abstracciones;
using Sga.Infraestructura;
using Sga.Infraestructura.Identidad;
using Sga.Infraestructura.Persistencia;

var builder = WebApplication.CreateBuilder(args);
var servicios = builder.Services;

servicios.AddInfraestructura(builder.Configuration.GetConnectionString("Sga")
                             ?? throw new InvalidOperationException("Falta la cadena de conexión ConnectionStrings:Sga."));
servicios.AddAplicacion();
servicios.Configure<OpcionesDelAlmacen>(builder.Configuration.GetSection("Almacen"));
servicios.Configure<OpcionesDeDemo>(builder.Configuration.GetSection("Demo"));
servicios.AddHttpContextAccessor();
servicios.AddScoped<IUsuarioActual, UsuarioDeLaPeticion>();
servicios.AddSingleton<IAvisos, AvisosPorSignalR>();
servicios.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);
servicios.AddScoped<SembradorDeDemo>();

// Identidad: usuarios en la base, sesión en una cookie HttpOnly del mismo sitio (sin tokens en el navegador).
servicios.AddIdentityCore<Usuario>(o =>
    {
        // Longitud antes que reglas de composición (como recomienda el NIST): frases fáciles de recordar.
        o.Password.RequiredLength = 10;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireDigit = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        o.User.RequireUniqueEmail = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<SgaContexto>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<FabricaDeIdentidad>();
servicios.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies(c => c.ApplicationCookie!.Configure(o =>
{
    o.Cookie.Name = "sga";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    // Un turno largo: la sesión se renueva mientras se use.
    o.ExpireTimeSpan = TimeSpan.FromHours(10);
    o.SlidingExpiration = true;
    // Es una API: 401 y 403, no redirecciones a una página de login.
    o.Events.OnRedirectToLogin = e => Responder(e.Response, StatusCodes.Status401Unauthorized);
    o.Events.OnRedirectToAccessDenied = e => Responder(e.Response, StatusCodes.Status403Forbidden);
}));
servicios.AddAuthorization(Politicas.Configurar);
servicios.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("entrar", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Seguridad:EntradasPorMinuto", 10),
            Window = TimeSpan.FromMinutes(1),
        }));
});

servicios.AddProblemDetails();
servicios.AddExceptionHandler<ManejadorDeErrores>();
servicios.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
servicios.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
servicios.AddOpenApi();
servicios.AddHealthChecks().AddDbContextCheck<SgaContexto>("base-de-datos");
servicios.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddOtlpExporter())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddRuntimeInstrumentation().AddOtlpExporter());

var app = builder.Build();

app.UseExceptionHandler();
app.Use(async (http, siguiente) =>
{
    var cabeceras = http.Response.Headers;
    cabeceras["X-Content-Type-Options"] = "nosniff";
    cabeceras["Referrer-Policy"] = "same-origin";
    cabeceras["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; " +
                                           "style-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:; frame-ancestors 'none'";
    cabeceras["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
    await siguiente();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}
app.MapHealthChecks("/health").AllowAnonymous();
app.MapSesion();
app.MapCatalogo();
app.MapOperaciones();
app.MapHub<CentroDeAvisos>("/avisos");
// El front (React) es una aplicación de una sola página: cualquier ruta que no sea de la API la sirve index.html.
app.MapFallbackToFile("index.html").AllowAnonymous();

await Inicializacion.Preparar(app.Services, app.Configuration, app.Logger);
await app.RunAsync();

static Task Responder(HttpResponse respuesta, int estado)
{
    respuesta.StatusCode = estado;
    return Task.CompletedTask;
}

/// <summary>Para las pruebas de la API (WebApplicationFactory).</summary>
public partial class Program;

/// <summary>Añade el nombre completo del usuario a su identidad (lo muestra el front).</summary>
internal sealed class FabricaDeIdentidad(UserManager<Usuario> usuarios, RoleManager<IdentityRole> roles, IOptions<IdentityOptions> opciones)
    : UserClaimsPrincipalFactory<Usuario, IdentityRole>(usuarios, roles, opciones)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Usuario usuario)
    {
        var identidad = await base.GenerateClaimsAsync(usuario);
        identidad.AddClaim(new Claim("nombre", usuario.NombreCompleto));
        return identidad;
    }
}
