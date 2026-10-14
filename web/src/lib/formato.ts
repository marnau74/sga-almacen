import type { EstadoDeConteo, EstadoDePedido, EstadoDeRecepcion, TipoDeTarea, Zona } from "./tipos";

const numero = new Intl.NumberFormat("es-ES");
const fecha = new Intl.DateTimeFormat("es-ES", { day: "2-digit", month: "2-digit", year: "numeric", timeZone: "UTC" });
const fechaYHora = new Intl.DateTimeFormat("es-ES", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });

export const entero = (n: number) => numero.format(n);

/** Una fecha sin hora («2026-12-01»): se enseña tal cual, sin zona horaria que la mueva un día. */
export const dia = (iso: string | null | undefined) => (iso ? fecha.format(new Date(`${iso}T00:00:00Z`)) : "—");

export const momento = (iso: string | null | undefined) => (iso ? fechaYHora.format(new Date(iso)) : "—");

export const ESTADOS_DE_PEDIDO: Record<EstadoDePedido, string> = {
  Pendiente: "Pendiente",
  Asignado: "Asignado",
  AsignadoParcial: "Asignado en parte",
  EnPreparacion: "En preparación",
  Preparado: "Preparado",
  Embalado: "Embalado",
  Expedido: "Expedido",
  Cancelado: "Cancelado",
};

export const ESTADOS_DE_RECEPCION: Record<EstadoDeRecepcion, string> = {
  Prevista: "Prevista",
  EnCurso: "Descargando",
  Recibida: "Recibida",
  ConDiferencias: "Con diferencias",
};

export const ESTADOS_DE_CONTEO: Record<EstadoDeConteo, string> = {
  Abierto: "Contando",
  Recuento: "Recuento",
  PorAprobar: "Por aprobar",
  Cerrado: "Cerrado",
  Anulado: "Anulado",
};

export const TAREAS: Record<TipoDeTarea, string> = { Ubicar: "Ubicar", Picking: "Preparar", Reponer: "Reponer" };

export const ZONAS: Record<Zona, string> = {
  Recepcion: "Recepción",
  Reserva: "Reserva",
  Picking: "Picking",
  Expedicion: "Muelle",
  Cuarentena: "Cuarentena",
};

/** Días que faltan hasta una fecha (negativo si ya pasó). */
export function diasHasta(iso: string, hoy = new Date()) {
  const objetivo = Date.UTC(+iso.slice(0, 4), +iso.slice(5, 7) - 1, +iso.slice(8, 10));
  const inicio = Date.UTC(hoy.getFullYear(), hoy.getMonth(), hoy.getDate());
  return Math.round((objetivo - inicio) / 86_400_000);
}

export const MOVIMIENTOS: Record<string, string> = {
  Recepcion: "Recepción",
  Ubicacion: "Ubicación",
  Traslado: "Traslado",
  Reposicion: "Reposición",
  Picking: "Picking",
  Expedicion: "Expedición",
  Ajuste: "Ajuste",
  Cuarentena: "Cuarentena",
};

/** La API cuenta por hora UTC; se enseña en la hora del navegador. */
export function aHoraLocal(porHoraUtc: number[], desfaseEnMinutos = new Date().getTimezoneOffset()) {
  const horas = -desfaseEnMinutos / 60;
  return porHoraUtc.map((_, local) => porHoraUtc[(((local - horas) % 24) + 24) % 24] ?? 0);
}
