import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useState } from "react";
import { EditorDeLineas, type Linea } from "../componentes/Lineas";
import { Aviso, Boton, Cargando, Estado, Tabla, Titulo, Vacio, type Tono } from "../componentes/ui";
import { api } from "../lib/api";
import { dia, entero, ESTADOS_DE_RECEPCION } from "../lib/formato";
import type { EstadoDeRecepcion, RecepcionDetalle, RecepcionResumen, Tercero } from "../lib/tipos";

const TONO: Record<EstadoDeRecepcion, Tono> = { Prevista: "neutro", EnCurso: "acento", Recibida: "bien", ConDiferencias: "aviso" };

export function Recepciones() {
  const { data, isPending } = useQuery({ queryKey: ["recepciones"], queryFn: () => api.get<RecepcionResumen[]>("/api/recepciones") });
  const [nueva, setNueva] = useState(false);
  return (
    <div className="flex flex-col gap-4">
      <Titulo accion={<Boton variante="principal" onClick={() => setNueva(!nueva)}>{nueva ? "Cerrar" : "Nueva recepción"}</Boton>}>Recepciones</Titulo>
      {nueva && <NuevaRecepcion alTerminar={() => setNueva(false)} />}
      {isPending ? (
        <Cargando />
      ) : (data ?? []).length === 0 ? (
        <Vacio>No hay recepciones.</Vacio>
      ) : (
        <Tabla titulo="Recepciones">
          <thead>
            <tr>
              <th>Recepción</th>
              <th>Proveedor</th>
              <th>Prevista</th>
              <th>Estado</th>
              <th className="!text-right">Previstas</th>
              <th className="!text-right">Recibidas</th>
              <th className="!text-right">Dañadas</th>
            </tr>
          </thead>
          <tbody>
            {(data ?? []).map((r) => (
              <tr key={r.id}>
                <td>
                  <Link to="/oficina/recepciones/$id" params={{ id: r.id }} className="codigo text-acento hover:underline">
                    {r.numero}
                  </Link>
                </td>
                <td>{r.proveedor}</td>
                <td>{dia(r.fechaPrevista)}</td>
                <td>
                  <Estado tono={TONO[r.estado]}>{ESTADOS_DE_RECEPCION[r.estado]}</Estado>
                </td>
                <td className="text-right">{entero(r.previstas)}</td>
                <td className="text-right">{entero(r.recibidas)}</td>
                <td className={`text-right ${r.danadas ? "text-mal" : ""}`}>{entero(r.danadas)}</td>
              </tr>
            ))}
          </tbody>
        </Tabla>
      )}
    </div>
  );
}

function NuevaRecepcion({ alTerminar }: { alTerminar: () => void }) {
  const clientes = useQueryClient();
  const { data: proveedores = [] } = useQuery({ queryKey: ["proveedores"], queryFn: () => api.get<Tercero[]>("/api/proveedores") });
  const [proveedor, setProveedor] = useState("");
  const [fecha, setFecha] = useState(new Date().toISOString().slice(0, 10));
  const [lineas, setLineas] = useState<Linea[]>([]);
  const crear = useMutation({
    mutationFn: () =>
      api.post("/api/recepciones", {
        proveedorId: proveedor,
        fechaPrevista: fecha,
        lineas: lineas.map((l) => ({ articuloId: l.articulo.id, cantidad: l.cantidad })),
      }),
    onSuccess: () => {
      void clientes.invalidateQueries({ queryKey: ["recepciones"] });
      alTerminar();
    },
  });
  return (
    <form
      className="flex flex-col gap-3 rounded-md border border-borde bg-superficie p-4"
      onSubmit={(e) => {
        e.preventDefault();
        crear.mutate();
      }}
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="flex flex-col gap-1">
          <label htmlFor="proveedor" className="text-sm font-medium">
            Proveedor
          </label>
          <select id="proveedor" required value={proveedor} onChange={(e) => setProveedor(e.target.value)} className="min-h-10 rounded-md border border-borde bg-superficie px-2">
            <option value="">Elige un proveedor</option>
            {proveedores.map((p) => (
              <option key={p.id} value={p.id}>
                {p.codigo} · {p.nombre}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor="fecha-prevista" className="text-sm font-medium">
            Llega el
          </label>
          <input id="fecha-prevista" type="date" required value={fecha} onChange={(e) => setFecha(e.target.value)} className="min-h-10 rounded-md border border-borde bg-superficie px-2" />
        </div>
      </div>
      <EditorDeLineas lineas={lineas} onCambiar={setLineas} />
      <Aviso error={crear.error} />
      <Boton type="submit" variante="principal" disabled={!proveedor || lineas.length === 0 || crear.isPending} className="self-start">
        Prever la recepción
      </Boton>
    </form>
  );
}

export function Recepcion() {
  const { id } = useParams({ from: "/oficina/recepciones/$id" });
  const { data: r } = useQuery({ queryKey: ["recepcion", id], queryFn: () => api.get<RecepcionDetalle>(`/api/recepciones/${id}`) });
  if (!r) return <Cargando />;
  return (
    <div className="flex flex-col gap-4">
      <Titulo>
        Recepción <span className="codigo">{r.resumen.numero}</span> <Estado tono={TONO[r.resumen.estado]}>{ESTADOS_DE_RECEPCION[r.resumen.estado]}</Estado>
      </Titulo>
      <p className="text-suave">
        {r.resumen.proveedor} · prevista el {dia(r.resumen.fechaPrevista)}
      </p>
      {r.observaciones && <p className="rounded-md border border-borde bg-superficie px-3 py-2">«{r.observaciones}»</p>}
      <Tabla titulo="Líneas de la recepción">
        <thead>
          <tr>
            <th>#</th>
            <th>Artículo</th>
            <th>EAN</th>
            <th className="!text-right">Prevista</th>
            <th className="!text-right">Recibida</th>
            <th className="!text-right">Dañada</th>
            <th className="!text-right">Diferencia</th>
          </tr>
        </thead>
        <tbody>
          {r.lineas.map((l) => {
            const diferencia = l.recibida - l.prevista;
            return (
              <tr key={l.numero}>
                <td>{l.numero}</td>
                <td>{l.articulo}</td>
                <td className="codigo">{l.ean}</td>
                <td className="text-right">{entero(l.prevista)}</td>
                <td className="text-right">{entero(l.recibida)}</td>
                <td className={`text-right ${l.danada ? "text-mal" : ""}`}>{entero(l.danada)}</td>
                <td className={`text-right ${diferencia ? "text-aviso" : "text-suave"}`}>{diferencia > 0 ? `+${diferencia}` : diferencia}</td>
              </tr>
            );
          })}
        </tbody>
      </Tabla>
    </div>
  );
}
