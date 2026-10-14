/** Error de la API (problem+json, RFC 9457). `codigo` es el de la regla del dominio que no se ha cumplido. */
export class ErrorDeApi extends Error {
  constructor(
    public readonly estado: number,
    public readonly codigo: string,
    mensaje: string,
  ) {
    super(mensaje);
  }
}

async function peticion<T>(metodo: string, ruta: string, cuerpo?: unknown): Promise<T> {
  const respuesta = await fetch(ruta, {
    method: metodo,
    credentials: "same-origin",
    headers: cuerpo === undefined ? { Accept: "application/json" } : { "Content-Type": "application/json", Accept: "application/json" },
    body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo),
  });
  if (respuesta.status === 401 && !ruta.startsWith("/api/sesion")) {
    window.dispatchEvent(new Event("sesion-caducada"));
  }
  if (!respuesta.ok) {
    let codigo = "error";
    let mensaje = `Error ${respuesta.status}`;
    try {
      const problema = (await respuesta.json()) as { codigo?: string; detail?: string; title?: string };
      codigo = problema.codigo ?? codigo;
      mensaje = problema.detail ?? problema.title ?? mensaje;
    } catch {
      // Sin cuerpo (403, 429...): vale el mensaje genérico.
      if (respuesta.status === 403) mensaje = "No tienes permiso para esto.";
      if (respuesta.status === 429) mensaje = "Demasiados intentos seguidos. Espera un momento.";
    }
    throw new ErrorDeApi(respuesta.status, codigo, mensaje);
  }
  if (respuesta.status === 204) return undefined as T;
  const tipo = respuesta.headers.get("content-type") ?? "";
  return (tipo.includes("json") ? await respuesta.json() : undefined) as T;
}

export const api = {
  get: <T>(ruta: string) => peticion<T>("GET", ruta),
  post: <T = void>(ruta: string, cuerpo: unknown = {}) => peticion<T>("POST", ruta, cuerpo),
  put: <T = void>(ruta: string, cuerpo: unknown = {}) => peticion<T>("PUT", ruta, cuerpo),
  delete: <T = void>(ruta: string) => peticion<T>("DELETE", ruta),
};

/** Construye una ruta con su consulta, saltándose los parámetros vacíos. */
export function conConsulta(ruta: string, parametros: Record<string, string | undefined | null>) {
  const consulta = new URLSearchParams();
  for (const [clave, valor] of Object.entries(parametros)) {
    if (valor) consulta.set(clave, valor);
  }
  const texto = consulta.toString();
  return texto ? `${ruta}?${texto}` : ruta;
}
