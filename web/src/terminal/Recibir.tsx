import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useState } from "react";
import { Escaner } from "../componentes/Escaner";
import { Aviso, Boton, Campo, Cargando, Estado, Vacio } from "../componentes/ui";
import { api } from "../lib/api";
import { dia, ESTADOS_DE_RECEPCION } from "../lib/formato";
import { leer } from "../lib/gs1";
import { bien, mal } from "../lib/sonido";
import type { RecepcionDetalle, RecepcionResumen } from "../lib/tipos";

/** Las recepciones que se pueden descargar: previstas y a medias. */
export function Recibir() {
  const { data, isPending } = useQuery({
    queryKey: ["recepciones", "terminal"],
    queryFn: async () => [
      ...(await api.get<RecepcionResumen[]>("/api/recepciones?estado=EnCurso")),
      ...(await api.get<RecepcionResumen[]>("/api/recepciones?estado=Prevista")),
    ],
  });
  if (isPending) return <Cargando />;
  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Recibir</h1>
        <Link to="/terminal" className="text-acento">
          Menú
        </Link>
      </div>
      {(data ?? []).length === 0 && <Vacio>No hay recepciones previstas.</Vacio>}
      {(data ?? []).map((r) => (
        <Link
          key={r.id}
          to="/terminal/recibir/$id"
          params={{ id: r.id }}
          className="block rounded-md border border-borde bg-superficie px-4 py-3 hover:border-acento"
        >
          <span className="codigo text-lg font-semibold">{r.numero}</span>{" "}
          <Estado tono={r.estado === "EnCurso" ? "aviso" : "neutro"}>{ESTADOS_DE_RECEPCION[r.estado]}</Estado>
          <span className="block text-suave">
            {r.proveedor} · {dia(r.fechaPrevista)} · {r.recibidas} de {r.previstas}
          </span>
        </Link>
      ))}
    </div>
  );
}

interface Pendiente {
  codigo: string;
  articulo: RecepcionDetalle["lineas"][number];
  lote: string;
  caducidad: string;
  cantidad: string;
}

/**
 * Descargar un camión: se escanea cada bulto; si la etiqueta GS1 trae el lote, la caducidad y la cantidad, se
 * rellenan solos; si no, se escriben. Lo bueno sale con su tarea de ubicar; lo dañado va a cuarentena.
 */
export function RecibirRecepcion() {
  const { id } = useParams({ from: "/terminal/recibir/$id" });
  const clientes = useQueryClient();
  const { data: recepcion } = useQuery({ queryKey: ["recepcion", id], queryFn: () => api.get<RecepcionDetalle>(`/api/recepciones/${id}`) });
  const [pendiente, setPendiente] = useState<Pendiente | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [hecho, setHecho] = useState<string | null>(null);
  const [observaciones, setObservaciones] = useState("");

  const recibir = useMutation({
    mutationFn: (danado: boolean) =>
      api.post<{ numero: number; tareas: number }>(`/api/recepciones/${id}/descargas`, {
        codigo: pendiente!.codigo,
        lote: pendiente!.lote || null,
        caducidad: pendiente!.caducidad || null,
        cantidad: Number(pendiente!.cantidad) || null,
        danado,
      }),
    onSuccess: (r, danado) => {
      bien();
      setHecho(danado ? "A cuarentena." : `Recibido. ${r.tareas === 1 ? "1 tarea" : `${r.tareas} tareas`} de ubicar.`);
      setPendiente(null);
      void clientes.invalidateQueries({ queryKey: ["recepcion", id] });
    },
    onError: (e) => {
      mal();
      setError(e.message);
    },
  });
  const cerrar = useMutation({
    mutationFn: () => api.post<{ estado: string }>(`/api/recepciones/${id}/cierre`, { observaciones: observaciones || null }),
    onSuccess: () => void clientes.invalidateQueries({ queryKey: ["recepcion", id] }),
  });

  if (!recepcion) return <Cargando />;
  const abierta = recepcion.resumen.estado === "Prevista" || recepcion.resumen.estado === "EnCurso";

  function escaneado(codigo: string) {
    setError(null);
    setHecho(null);
    const lectura = leer(codigo);
    const linea = recepcion!.lineas.find((l) => l.ean === lectura.gtin);
    if (!linea) {
      mal();
      setError(lectura.gtin ? "Este artículo no viene en esta recepción." : "Eso no es el código de un artículo.");
      return;
    }
    bien();
    setPendiente({
      codigo,
      articulo: linea,
      lote: lectura.lote ?? "",
      caducidad: lectura.caducidad ?? "",
      cantidad: lectura.cantidad ? String(lectura.cantidad) : "",
    });
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="codigo text-2xl font-semibold">{recepcion.resumen.numero}</h1>
        <Link to="/terminal/recibir" className="text-acento">
          Recepciones
        </Link>
      </div>
      <p className="text-suave">{recepcion.resumen.proveedor}</p>

      {error && <Aviso>{error}</Aviso>}
      {hecho && (
        <p role="status" className="rounded-md border border-bien/40 bg-bien-suave px-3 py-2 text-bien">
          {hecho}
        </p>
      )}

      {abierta && !pendiente && <Escaner etiqueta="Escanea un bulto" onLeido={escaneado} />}
      {pendiente && (
        <form
          className="flex flex-col gap-3 rounded-md border border-borde bg-superficie p-4"
          onSubmit={(e) => {
            e.preventDefault();
            recibir.mutate(false);
          }}
        >
          <p className="text-lg font-semibold">{pendiente.articulo.articulo}</p>
          <Campo id="lote" etiqueta="Lote" className="codigo" value={pendiente.lote} onChange={(e) => setPendiente({ ...pendiente, lote: e.target.value })} />
          <Campo
            id="caducidad"
            etiqueta="Caducidad"
            type="date"
            value={pendiente.caducidad}
            onChange={(e) => setPendiente({ ...pendiente, caducidad: e.target.value })}
          />
          <Campo
            id="cantidad"
            etiqueta="Unidades"
            type="number"
            inputMode="numeric"
            min={1}
            required
            className="cifras text-xl"
            value={pendiente.cantidad}
            onChange={(e) => setPendiente({ ...pendiente, cantidad: e.target.value })}
          />
          <Boton grande type="submit" variante="principal" disabled={recibir.isPending}>
            Recibir
          </Boton>
          <div className="flex justify-between">
            <Boton variante="peligro" onClick={() => recibir.mutate(true)} disabled={recibir.isPending}>
              Viene dañado
            </Boton>
            <Boton variante="discreto" onClick={() => setPendiente(null)}>
              Cancelar
            </Boton>
          </div>
        </form>
      )}

      <ul className="flex flex-col divide-y divide-borde rounded-md border border-borde bg-superficie" aria-label="Líneas">
        {recepcion.lineas.map((l) => (
          <li key={l.numero} className="flex items-center justify-between gap-3 px-4 py-2">
            <span>
              {l.articulo}
              <span className="codigo block text-xs text-suave">{l.ean}</span>
            </span>
            <span className={`cifras whitespace-nowrap ${l.recibida === l.prevista ? "text-bien" : ""}`}>
              {l.recibida} / {l.prevista}
              {l.danada > 0 && <span className="text-mal"> · {l.danada} dañadas</span>}
            </span>
          </li>
        ))}
      </ul>

      {recepcion.resumen.estado === "EnCurso" && (
        <div className="flex flex-col gap-2">
          <Campo id="observaciones" etiqueta="Observaciones (opcional)" value={observaciones} onChange={(e) => setObservaciones(e.target.value)} />
          <Boton grande onClick={() => cerrar.mutate()} disabled={cerrar.isPending}>
            Cerrar la recepción
          </Boton>
          <Aviso error={cerrar.error} />
        </div>
      )}
      {!abierta && <p className="text-suave">Cerrada: {ESTADOS_DE_RECEPCION[recepcion.resumen.estado].toLowerCase()}.</p>}
    </div>
  );
}
