import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { Escaner } from "../componentes/Escaner";
import { Aviso, Boton, Cargando, Vacio } from "../componentes/ui";
import { api, ErrorDeApi } from "../lib/api";
import { dia, entero, TAREAS } from "../lib/formato";
import { esElArticulo } from "../lib/gs1";
import { bien, mal } from "../lib/sonido";
import { useSesion } from "../lib/sesion";
import type { Tarea, TipoDeTarea } from "../lib/tipos";

const tareasDe = (tipo?: TipoDeTarea) => ({
  queryKey: ["tareas", tipo ?? "todas"],
  queryFn: () => api.get<Tarea[]>(tipo ? `/api/tareas?tipo=${tipo}` : "/api/tareas"),
});

/** Las tareas de un tipo, en el orden del recorrido. «Empezar» coge la primera libre. */
export function ListaDeTareas() {
  const { tipo } = useParams({ from: "/terminal/tareas/$tipo" }) as { tipo: TipoDeTarea };
  const { data: tareas, isPending } = useQuery(tareasDe(tipo));
  const navegar = useNavigate();
  if (isPending) return <Cargando />;
  const lista = tareas ?? [];

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{TAREAS[tipo]}</h1>
        <Link to="/terminal" className="text-acento">
          Menú
        </Link>
      </div>
      {lista.length === 0 ? (
        <Vacio>No hay nada pendiente.</Vacio>
      ) : (
        <>
          <Boton grande variante="principal" onClick={() => void navegar({ to: "/terminal/tarea/$id", params: { id: lista[0]!.id } })}>
            Empezar por la primera
          </Boton>
          <ul className="flex flex-col gap-2">
            {lista.map((t) => (
              <li key={t.id}>
                <Link
                  to="/terminal/tarea/$id"
                  params={{ id: t.id }}
                  className="block rounded-md border border-borde bg-superficie px-4 py-3 hover:border-acento"
                >
                  <span className="codigo text-lg font-semibold">{t.tipo === "Ubicar" ? t.destino : t.origen}</span>
                  <span className="ml-2 text-suave">{t.documento}</span>
                  <span className="block">
                    <span className="cifras font-semibold">{entero(t.cantidad)}</span> × {t.articulo}
                    {t.lote && <span className="codigo text-suave"> · {t.lote}</span>}
                  </span>
                  {t.estado === "EnCurso" && <span className="text-sm text-aviso">La tiene {t.operario}</span>}
                </Link>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}

type Paso = "origen" | "articulo" | "cantidad" | "destino";

function pasosDe(tipo: TipoDeTarea): Paso[] {
  if (tipo === "Picking") return ["origen", "articulo", "cantidad"];
  if (tipo === "Reponer") return ["origen", "articulo", "destino"];
  return ["articulo", "destino"];
}

/**
 * Una tarea, paso a paso: ir al sitio, escanearlo, escanear el artículo y confirmar. Cada escaneo se comprueba al
 * momento (con sonido); la API lo vuelve a comprobar todo al confirmar.
 */
export function HacerTarea() {
  const { id } = useParams({ from: "/terminal/tarea/$id" });
  // Con otra tarea, el flujo empieza de cero: se monta de nuevo.
  return <FlujoDeTarea key={id} id={id} />;
}

function FlujoDeTarea({ id }: { id: string }) {
  const sesion = useSesion();
  const clientes = useQueryClient();
  const navegar = useNavigate();
  const { data: tareas } = useQuery(tareasDe());
  const tarea = tareas?.find((t) => t.id === id);

  const [paso, setPaso] = useState(0);
  const [origen, setOrigen] = useState<string | null>(null);
  const [articulo, setArticulo] = useState<string | null>(null);
  const [destino, setDestino] = useState<string | null>(null);
  const [cantidad, setCantidad] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const tomar = useMutation({ mutationFn: () => api.post(`/api/tareas/${id}/toma`) });
  const soltar = useMutation({
    mutationFn: () => api.delete(`/api/tareas/${id}/toma`),
    onSuccess: () => void navegar({ to: "/terminal" }),
  });
  const confirmar = useMutation({
    mutationFn: (datos: { codigoDeOrigen: string | null; codigoDeArticulo: string; cantidad: number; codigoDeDestino: string | null }) =>
      api.post(`/api/tareas/${id}/confirmacion`, datos),
    onSuccess: async () => {
      bien();
      await clientes.invalidateQueries({ queryKey: ["tareas"] });
      // La siguiente del mismo tipo, sin volver al menú.
      const siguientes = await clientes.fetchQuery(tareasDe(tarea!.tipo));
      const siguiente = siguientes.find((t) => t.id !== id && (t.estado === "Pendiente" || t.operario === sesion.usuario));
      void navegar(siguiente ? { to: "/terminal/tarea/$id", params: { id: siguiente.id } } : { to: "/terminal" });
    },
    onError: (e) => {
      mal();
      setError(e instanceof ErrorDeApi ? e.message : "No se ha podido confirmar.");
    },
  });

  // Al abrirla, el operario se la queda (si otro la tiene, la API lo dice).
  const { mutate: tomarLa } = tomar;
  useEffect(() => {
    tomarLa(undefined, { onError: () => mal() });
  }, [tomarLa]);

  if (!tareas) return <Cargando />;
  if (!tarea) {
    return (
      <div className="flex flex-col gap-3">
        <Vacio>Esta tarea ya no está pendiente.</Vacio>
        <Link to="/terminal" className="text-acento">
          Volver al menú
        </Link>
      </div>
    );
  }

  const pasos = pasosDe(tarea.tipo);
  const actual = pasos[paso];
  const cantidadFinal = cantidad ?? tarea.cantidad;

  function siguiente() {
    setError(null);
    bien();
    if (paso + 1 < pasos.length) {
      setPaso(paso + 1);
    } else {
      enviar();
    }
  }

  function enviar(cuantas = cantidadFinal, alDestino = destino) {
    confirmar.mutate({ codigoDeOrigen: origen, codigoDeArticulo: articulo ?? tarea!.ean, cantidad: cuantas, codigoDeDestino: alDestino });
  }

  function fallo(mensaje: string) {
    mal();
    setError(mensaje);
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between text-sm text-suave">
        <span>
          {TAREAS[tarea.tipo]} · {tarea.documento}
        </span>
        <span>
          Paso {paso + 1} de {pasos.length}
        </span>
      </div>

      <section className="rounded-md border border-borde bg-superficie p-4" aria-label="Qué hay que hacer">
        <p className="text-sm text-suave">{tarea.tipo === "Ubicar" ? "Lleva" : "Coge"}</p>
        <p className="text-2xl font-semibold leading-tight">
          <span className="cifras">{entero(tarea.cantidad)}</span> × {tarea.articulo}
        </p>
        <p className="codigo mt-1 text-suave">
          {tarea.ean}
          {tarea.lote && ` · lote ${tarea.lote}`}
          {tarea.caducidad && ` · cad. ${dia(tarea.caducidad)}`}
        </p>
        <dl className="mt-3 grid grid-cols-2 gap-2">
          <div>
            <dt className="text-sm text-suave">Desde</dt>
            <dd className="codigo text-2xl font-semibold">{tarea.origen}</dd>
          </div>
          <div>
            <dt className="text-sm text-suave">{tarea.tipo === "Ubicar" ? "Hasta (sugerido)" : "Hasta"}</dt>
            <dd className="codigo text-2xl font-semibold">{tarea.destino}</dd>
          </div>
        </dl>
      </section>

      {tomar.error && <Aviso error={tomar.error} />}
      {error && <Aviso>{error}</Aviso>}

      {!tomar.error && actual === "origen" && (
        <Escaner
          etiqueta="Escanea el hueco"
          pista={tarea.origen}
          onLeido={(codigo) =>
            codigo.toUpperCase() === tarea.origen ? (setOrigen(codigo.toUpperCase()), siguiente()) : fallo(`Ese es ${codigo}: tienes que estar en ${tarea.origen}.`)
          }
        />
      )}
      {!tomar.error && actual === "articulo" && (
        <Escaner
          etiqueta="Escanea el artículo"
          pista={tarea.ean}
          onLeido={(codigo) => (esElArticulo(codigo, tarea.ean) ? (setArticulo(codigo), siguiente()) : fallo("Ese no es el artículo de la tarea."))}
        />
      )}
      {!tomar.error && actual === "cantidad" && (
        <div className="flex flex-col gap-3">
          <p className="text-base font-medium">¿Cuántas has cogido?</p>
          <div className="flex items-center gap-3">
            <Boton grande onClick={() => setCantidad(Math.max(0, cantidadFinal - 1))} aria-label="Una menos">
              −
            </Boton>
            <output className="cifras min-w-20 text-center text-4xl font-semibold" aria-live="polite">
              {cantidadFinal}
            </output>
            <Boton grande onClick={() => setCantidad(Math.min(tarea.cantidad, cantidadFinal + 1))} aria-label="Una más">
              +
            </Boton>
          </div>
          {cantidadFinal < tarea.cantidad && (
            <p className="text-aviso">Faltan {tarea.cantidad - cantidadFinal}: se buscarán en otro hueco y quedará anotado.</p>
          )}
          <Boton grande variante="principal" disabled={confirmar.isPending} onClick={() => enviar()}>
            Confirmar {cantidadFinal}
          </Boton>
        </div>
      )}
      {!tomar.error && actual === "destino" && (
        <Escaner
          etiqueta={tarea.tipo === "Ubicar" ? "Escanea el hueco donde lo dejas" : "Escanea el hueco de picking"}
          pista={tarea.destino}
          desactivado={confirmar.isPending}
          onLeido={(codigo) => {
            const hueco = codigo.toUpperCase();
            if (tarea.tipo === "Reponer" && hueco !== tarea.destino) {
              fallo(`Ese es ${hueco}: va a ${tarea.destino}.`);
              return;
            }
            setDestino(hueco);
            enviar(tarea.cantidad, hueco);
          }}
        />
      )}

      <div className="mt-2 flex items-center justify-between">
        <Boton variante="discreto" onClick={() => soltar.mutate()} disabled={soltar.isPending}>
          Dejarla para otro
        </Boton>
        <Link to="/terminal" className="text-sm text-acento hover:underline">
          Menú
        </Link>
      </div>
    </div>
  );
}
