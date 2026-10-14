// Lo que devuelve la API (los nombres de C# en camelCase).

export type Zona = "Recepcion" | "Reserva" | "Picking" | "Expedicion" | "Cuarentena";
export type TipoDeTarea = "Ubicar" | "Picking" | "Reponer";
export type EstadoDeTarea = "Pendiente" | "EnCurso" | "Hecha" | "ConFalta" | "Anulada";
export type EstadoDeRecepcion = "Prevista" | "EnCurso" | "Recibida" | "ConDiferencias";
export type EstadoDePedido =
  | "Pendiente"
  | "Asignado"
  | "AsignadoParcial"
  | "EnPreparacion"
  | "Preparado"
  | "Embalado"
  | "Expedido"
  | "Cancelado";
export type EstadoDeConteo = "Abierto" | "Recuento" | "PorAprobar" | "Cerrado" | "Anulado";
export type Rol = "Administrador" | "JefeDeAlmacen" | "Operario" | "Administracion";

export interface Sesion {
  usuario: string;
  nombre: string;
  roles: Rol[];
}

export interface Articulo {
  id: string;
  ean: string;
  nombre: string;
  marca: string | null;
  formato: string | null;
  unidadesPorCaja: number;
  controlDeLote: boolean;
  controlDeCaducidad: boolean;
  stock: number;
  disponible: number;
}

export interface StockEnUbicacion {
  ubicacion: string;
  zona: Zona;
  lote: string;
  caducidad: string | null;
  cantidad: number;
  reservada: number;
  loteBloqueado: boolean;
  ubicacionBloqueada: boolean;
}

export interface Ubicacion {
  id: string;
  codigo: string;
  zona: Zona;
  pasillo: number;
  modulo: number;
  nivel: number;
  bloqueada: boolean;
  articuloFijo: string | null;
  minimo: number | null;
  maximo: number | null;
  unidades: number;
}

export interface Tarea {
  id: string;
  tipo: TipoDeTarea;
  estado: EstadoDeTarea;
  operario: string | null;
  articulo: string;
  ean: string;
  lote: string;
  caducidad: string | null;
  cantidad: number;
  origen: string;
  destino: string;
  documento: string;
  orden: number;
}

export interface RecepcionResumen {
  id: string;
  numero: string;
  proveedor: string;
  fechaPrevista: string;
  estado: EstadoDeRecepcion;
  previstas: number;
  recibidas: number;
  danadas: number;
}

export interface RecepcionDetalle {
  resumen: RecepcionResumen;
  lineas: { numero: number; articuloId: string; articulo: string; ean: string; prevista: number; recibida: number; danada: number }[];
  observaciones: string | null;
}

export interface PedidoResumen {
  id: string;
  numero: string;
  cliente: string;
  fechaDeServicio: string;
  estado: EstadoDePedido;
  lineas: number;
  pedidas: number;
  asignadas: number;
  preparadas: number;
  oleada: string | null;
}

export interface PedidoDetalle {
  resumen: PedidoResumen;
  lineas: { numero: number; articuloId: string; articulo: string; ean: string; pedida: number; asignada: number; preparada: number; rotura: number }[];
  preparados: { articuloId: string; articulo: string; lote: string; caducidad: string | null; cantidad: number; embalado: number }[];
  bultos: { numero: number; sscc: string; cerrado: boolean; contenido: { articulo: string; lote: string; cantidad: number }[] }[];
}

export interface ConteoResumen {
  id: string;
  numero: string;
  estado: EstadoDeConteo;
  ubicaciones: number;
  abierto: string;
}

export interface ConteoDetalle {
  id: string;
  numero: string;
  estado: EstadoDeConteo;
  ubicaciones: string[];
  lineas: {
    ubicacion: string;
    articulo: string;
    ean: string;
    lote: string;
    esperada: number | null;
    primera: number | null;
    segunda: number | null;
    diferencia: number | null;
  }[];
  aprobadoPor: string | null;
}

export interface Trazabilidad {
  articulo: string;
  ean: string;
  lote: string;
  caducidad: string | null;
  bloqueado: boolean;
  recepciones: string[];
  salidas: { pedido: string; cliente: string; cantidad: number; expedido: string | null }[];
  dondeEsta: StockEnUbicacion[];
  movimientos: { momento: string; tipo: string; origen: string | null; destino: string | null; cantidad: number; documento: string; usuario: string }[];
}

export interface Panel {
  pedidosPorEstado: Partial<Record<EstadoDePedido, number>>;
  tareasPendientes: Partial<Record<TipoDeTarea, number>>;
  lineasPreparadasHoy: number;
  lineasPorHora: number[];
  huecosDeReserva: number;
  huecosDeReservaOcupados: number;
  caducanPronto: { articulo: string; lote: string; caducidad: string; cantidad: number }[];
  faltas: { ubicacion: string; articulo: string; unidades: number; momento: string }[];
  roturas: { pedido: string; cliente: string; unidades: number }[];
  recepcionesConDiferencias: number;
}

export interface Tercero {
  id: string;
  codigo: string;
  nombre: string;
  diasMinimosDeVidaUtil?: number;
}
