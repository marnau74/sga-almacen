import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { api } from "../lib/api";
import { esJefe, useSesion } from "../lib/sesion";
import type { Tarea, TipoDeTarea } from "../lib/tipos";

const TAREAS: { tipo: TipoDeTarea; texto: string; ayuda: string }[] = [
  { tipo: "Picking", texto: "Preparar pedidos", ayuda: "Coger del hueco y llevar al muelle" },
  { tipo: "Ubicar", texto: "Ubicar", ayuda: "De la playa de recepción a su hueco" },
  { tipo: "Reponer", texto: "Reponer", ayuda: "De la reserva al picking" },
];

/** El menú del terminal: botones grandes, con lo que hay pendiente de cada cosa. */
export function InicioDelTerminal() {
  const sesion = useSesion();
  const { data: tareas = [] } = useQuery({ queryKey: ["tareas"], queryFn: () => api.get<Tarea[]>("/api/tareas") });
  const mias = tareas.filter((t) => t.estado === "EnCurso");

  return (
    <div className="flex flex-col gap-3">
      <h1 className="text-2xl font-semibold">Hola, {sesion.nombre}</h1>
      {mias.length > 0 && (
        <Link
          to="/terminal/tarea/$id"
          params={{ id: mias[0]!.id }}
          className="rounded-md border-2 border-aviso bg-aviso-suave p-4 text-aviso"
        >
          Tienes una tarea a medias: <span className="font-semibold">{mias[0]!.articulo}</span>. Toca para seguir.
        </Link>
      )}
      {TAREAS.map((t) => {
        const pendientes = tareas.filter((x) => x.tipo === t.tipo).length;
        return (
          <Link
            key={t.tipo}
            to="/terminal/tareas/$tipo"
            params={{ tipo: t.tipo }}
            className="flex min-h-20 items-center justify-between rounded-md border border-borde bg-superficie px-4 py-3 hover:border-acento"
          >
            <span>
              <span className="block text-xl font-semibold">{t.texto}</span>
              <span className="text-sm text-suave">{t.ayuda}</span>
            </span>
            <span className="cifras text-3xl font-semibold" aria-label={`${pendientes} pendientes`}>
              {pendientes}
            </span>
          </Link>
        );
      })}
      <div className="grid grid-cols-2 gap-3">
        <Link to="/terminal/recibir" className="rounded-md border border-borde bg-superficie px-4 py-5 text-lg font-semibold hover:border-acento">
          Recibir
        </Link>
        <Link to="/terminal/embalar" className="rounded-md border border-borde bg-superficie px-4 py-5 text-lg font-semibold hover:border-acento">
          Embalar y expedir
        </Link>
        <Link to="/terminal/contar" className="rounded-md border border-borde bg-superficie px-4 py-5 text-lg font-semibold hover:border-acento">
          Contar
        </Link>
        {esJefe(sesion) && (
          <Link to="/oficina" className="rounded-md border border-borde bg-superficie px-4 py-5 text-lg font-semibold hover:border-acento">
            Oficina
          </Link>
        )}
      </div>
    </div>
  );
}
