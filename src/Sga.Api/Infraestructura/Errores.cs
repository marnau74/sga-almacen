using FluentValidation;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Sga.Dominio.Comun;

namespace Sga.Api.Infraestructura;

/// <summary>
/// Convierte los errores en respuestas RFC 9457 (problem+json). Una regla del dominio lleva su código en
/// <c>codigo</c>: el terminal lo usa para decidir qué decir y qué sonido hacer.
/// </summary>
internal sealed class ManejadorDeErrores(IProblemDetailsService problemas, ILogger<ManejadorDeErrores> registro) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception excepcion, CancellationToken ct)
    {
        var (estado, titulo, codigo, detalle) = excepcion switch
        {
            ReglaIncumplida r => (StatusCodes.Status409Conflict, "No se puede hacer", r.Codigo, r.Message),
            NoEncontrado n => (StatusCodes.Status404NotFound, "No encontrado", "no-encontrado", n.Message),
            ValidationException v => (StatusCodes.Status400BadRequest, "Datos no válidos", "datos-no-validos",
                string.Join(" ", v.Errors.Select(e => e.ErrorMessage))),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Alguien se ha adelantado", "conflicto",
                "Otra persona ha cambiado esto a la vez. Vuelve a cargarlo e inténtalo de nuevo."),
            _ => (0, "", "", ""),
        };
        if (estado == 0)
        {
            return false;
        }
        Registro.ReglaIncumplida(registro, codigo, detalle);
        http.Response.StatusCode = estado;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = estado,
                Title = titulo,
                Detail = detalle,
                Extensions = { ["codigo"] = codigo },
            },
        });
    }
}

/// <summary>Valida el cuerpo de la petición con su validador de FluentValidation, si lo tiene.</summary>
internal sealed class Validacion<T>(IValidator<T> validador) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var cuerpo = contexto.Arguments.OfType<T>().FirstOrDefault();
        if (cuerpo is not null)
        {
            await validador.ValidateAndThrowAsync(cuerpo, contexto.HttpContext.RequestAborted);
        }
        return await siguiente(contexto);
    }
}

internal static class ExtensionesDeValidacion
{
    public static RouteHandlerBuilder Validado<T>(this RouteHandlerBuilder ruta) => ruta.AddEndpointFilter<Validacion<T>>();
}
