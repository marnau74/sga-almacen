import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams, useSearch } from "@tanstack/react-router";
import { useState } from "react";
import { EditorDeLineas, type Linea } from "../componentes/Lineas";
import { Aviso, Boton, Cargando, Estado, Tabla, Titulo, Vacio, type Tono } from "../componentes/ui";
import { api, conConsulta } from "../lib/api";
import { dia, entero, ESTADOS_DE_PEDIDO } from "../lib/formato";
import { esJefe, useSesion } from "../lib/sesion";
import type { EstadoDePedido, PedidoDetalle, PedidoResumen, Tercero } from "../lib/tipos";

export const TONO_DE_PEDIDO: Record<EstadoDePedido, Tono> = {
  Pendiente: "neutro",
  Asignado: "acento",
  AsignadoParcial: "aviso",
  EnPreparacion: "acento",
  Preparado: "bien",
  Embalado: "bien",
  Expedido: "neutro",
  Cancelado: "mal",
};

const FILTROS: (EstadoDePedido | undefined)[] = [undefined, "Pendiente", "AsignadoParcial", "Asignado", "EnPreparacion", "Preparado", "Embalado", "Expedido"];

export function Pedidos() {
  const sesion = useSesion();
  const { estado } = useSearch({ from: "/oficina/pedidos" });
  const navegar = useNavigate();
  const clientes = useQueryClient();
  const { data: pedidos, isPending } = useQuery({
    queryKey: ["pedidos", estado ?? "todos"],
    queryFn: () => api.get<PedidoResumen[]>(conConsulta("/api/pedidos", { estado })),
  });
  const [elegidos, setElegidos] = useState<string[]>([]);
  const [nuevo, setNuevo] = useState(false);
  const [resultado, setResultado] = useState<string | null>(null);

  const asignar = useMutation({
    mutationFn: () => api.post<{ pedido: string; estado: EstadoDePedido; pedidas: number; asignadas: number }[]>("/api/pedidos/asignacion", { pedidos: elegidos }),
    onSuccess: (r) => {
      setResultado(r.map((x) => `${x.pedido}: ${entero(x.asignadas)} de ${entero(x.pedidas)}`).join(" · ") || "Nada que asignar.");
      setElegidos([]);
      void clientes.invalidateQueries({ queryKey: ["pedidos"] });
    },
  });
  const lanzar = useMutation({
    mutationFn: () => api.post<{ numero: string }>("/api/pedidos/oleadas", { pedidos: elegidos }),
    onSuccess: (r) => {
      setResultado(`Oleada ${r.numero} lanzada: las tareas ya están en el terminal.`);
      setElegidos([]);
      void clientes.invalidateQueries({ queryKey: ["pedidos"] });
    },
  });

  const lista = pedidos ?? [];
  const seleccion = lista.filter((p) => elegidos.includes(p.id));
  const sePuedenAsignar = seleccion.length > 0 && seleccion.every((p) => p.estado === "Pendiente" || p.estado === "AsignadoParcial");
  const sePuedenLanzar = seleccion.length > 0 && seleccion.every((p) => p.estado === "Asignado" || p.estado === "AsignadoParcial");

  return (
    <div className="flex flex-col gap-4">
      <Titulo accion={<Boton variante="principal" onClick={() => setNuevo(!nuevo)}>{nuevo ? "Cerrar" : "Nuevo pedido"}</Boton>}>Pedidos</Titulo>
      {nuevo && <NuevoPedido alTerminar={() => setNuevo(false)} />}

      <div className="flex flex-wrap gap-1" role="group" aria-label="Filtrar por estado">
        {FILTROS.map((f) => (
          <Boton
            key={f ?? "todos"}
            variante={f === estado ? "principal" : "normal"}
            aria-pressed={f === estado}
            onClick={() => {
              setElegidos([]);
              void navegar({ to: "/oficina/pedidos", search: { estado: f } });
            }}
          >
            {f ? ESTADOS_DE_PEDIDO[f] : "Todos"}
          </Boton>
        ))}
      </div>

      {esJefe(sesion) && (
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm text-suave">{seleccion.length} elegidos</span>
          <Boton disabled={!sePuedenAsignar || asignar.isPending} onClick={() => asignar.mutate()}>
            Asignar stock
          </Boton>
          <Boton variante="principal" disabled={!sePuedenLanzar || lanzar.isPending} onClick={() => lanzar.mutate()}>
            Lanzar oleada
          </Boton>
        </div>
      )}
      <Aviso error={asignar.error ?? lanzar.error} />
      {resultado && (
        <p role="status" className="rounded-md border border-bien/40 bg-bien-suave px-3 py-2 text-sm text-bien">
          {resultado}
        </p>
      )}

      {isPending ? (
        <Cargando />
      ) : lista.length === 0 ? (
        <Vacio>No hay pedidos{estado ? ` en «${ESTADOS_DE_PEDIDO[estado].toLowerCase()}»` : ""}.</Vacio>
      ) : (
        <Tabla titulo="Pedidos">
          <thead>
            <tr>
              {esJefe(sesion) && (
                <th>
                  <span className="sr-only">Elegir</span>
                </th>
              )}
              <th>Pedido</th>
              <th>Cliente</th>
              <th>Servicio</th>
              <th>Estado</th>
              <th className="!text-right">Líneas</th>
              <th className="!text-right">Pedidas</th>
              <th className="!text-right">Asignadas</th>
              <th className="!text-right">Preparadas</th>
              <th>Oleada</th>
            </tr>
          </thead>
          <tbody>
            {lista.map((p) => (
              <tr key={p.id}>
                {esJefe(sesion) && (
                  <td>
                    <input
                      type="checkbox"
                      aria-label={`Elegir ${p.numero}`}
                      checked={elegidos.includes(p.id)}
                      onChange={(e) => setElegidos(e.target.checked ? [...elegidos, p.id] : elegidos.filter((x) => x !== p.id))}
                    />
                  </td>
                )}
                <td>
                  <Link to="/oficina/pedidos/$id" params={{ id: p.id }} className="codigo text-acento hover:underline">
                    {p.numero}
                  </Link>
                </td>
                <td>{p.cliente}</td>
                <td>{dia(p.fechaDeServicio)}</td>
                <td>
                  <Estado tono={TONO_DE_PEDIDO[p.estado]}>{ESTADOS_DE_PEDIDO[p.estado]}</Estado>
                </td>
                <td className="text-right">{p.lineas}</td>
                <td className="text-right">{entero(p.pedidas)}</td>
                <td className={`text-right ${p.asignadas < p.pedidas && p.estado !== "Pendiente" ? "text-aviso" : ""}`}>{entero(p.asignadas)}</td>
                <td className="text-right">{entero(p.preparadas)}</td>
                <td className="codigo">{p.oleada ?? "—"}</td>
              </tr>
            ))}
          </tbody>
        </Tabla>
      )}
    </div>
  );
}

function manana() {
  const d = new Date();
  d.setDate(d.getDate() + 1);
  return d.toISOString().slice(0, 10);
}

function NuevoPedido({ alTerminar }: { alTerminar: () => void }) {
  const clientes = useQueryClient();
  const { data: lista = [] } = useQuery({ queryKey: ["clientes"], queryFn: () => api.get<Tercero[]>("/api/clientes") });
  const [cliente, setCliente] = useState("");
  const [fecha, setFecha] = useState(manana());
  const [lineas, setLineas] = useState<Linea[]>([]);
  const crear = useMutation({
    mutationFn: () =>
      api.post("/api/pedidos", {
        clienteId: cliente,
        fechaDeServicio: fecha,
        lineas: lineas.map((l) => ({ articuloId: l.articulo.id, cantidad: l.cantidad })),
      }),
    onSuccess: () => {
      void clientes.invalidateQueries({ queryKey: ["pedidos"] });
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
          <label htmlFor="cliente" className="text-sm font-medium">
            Cliente
          </label>
          <select id="cliente" required value={cliente} onChange={(e) => setCliente(e.target.value)} className="min-h-10 rounded-md border border-borde bg-superficie px-2">
            <option value="">Elige un cliente</option>
            {lista.map((c) => (
              <option key={c.id} value={c.id}>
                {c.codigo} · {c.nombre}
                {c.diasMinimosDeVidaUtil ? ` (${c.diasMinimosDeVidaUtil} días de vida útil)` : ""}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor="fecha-servicio" className="text-sm font-medium">
            Fecha de servicio
          </label>
          <input id="fecha-servicio" type="date" required value={fecha} onChange={(e) => setFecha(e.target.value)} className="min-h-10 rounded-md border border-borde bg-superficie px-2" />
        </div>
      </div>
      <EditorDeLineas lineas={lineas} onCambiar={setLineas} />
      <Aviso error={crear.error} />
      <Boton type="submit" variante="principal" disabled={!cliente || lineas.length === 0 || crear.isPending} className="self-start">
        Crear el pedido
      </Boton>
    </form>
  );
}

export function Pedido() {
  const { id } = useParams({ from: "/oficina/pedidos/$id" });
  const clientes = useQueryClient();
  const { data: pedido } = useQuery({ queryKey: ["pedido", id], queryFn: () => api.get<PedidoDetalle>(`/api/pedidos/${id}`) });
  const cancelar = useMutation({
    mutationFn: () => api.post(`/api/pedidos/${id}/cancelacion`),
    onSuccess: () => void clientes.invalidateQueries({ queryKey: ["pedido", id] }),
  });
  if (!pedido) return <Cargando />;
  const r = pedido.resumen;

  return (
    <div className="flex flex-col gap-5">
      <Titulo
        accion={
          <div className="flex gap-2">
            {["Embalado", "Expedido"].includes(r.estado) && (
              <a className="inline-flex min-h-9 items-center rounded-md border border-borde bg-superficie px-3 text-sm" href={`/api/pedidos/${id}/albaran.pdf`} target="_blank" rel="noreferrer">
                Albarán (PDF)
              </a>
            )}
            {["Pendiente", "Asignado", "AsignadoParcial"].includes(r.estado) && (
              <Boton variante="peligro" onClick={() => confirm(`¿Cancelar el pedido ${r.numero}? Su stock reservado vuelve a quedar libre.`) && cancelar.mutate()}>
                Cancelar pedido
              </Boton>
            )}
          </div>
        }
      >
        Pedido <span className="codigo">{r.numero}</span> <Estado tono={TONO_DE_PEDIDO[r.estado]}>{ESTADOS_DE_PEDIDO[r.estado]}</Estado>
      </Titulo>
      <p className="text-suave">
        {r.cliente} · servicio el {dia(r.fechaDeServicio)}
        {r.oleada && <> · oleada <span className="codigo">{r.oleada}</span></>}
      </p>
      <Aviso error={cancelar.error} />

      <Tabla titulo="Líneas del pedido">
        <thead>
          <tr>
            <th>#</th>
            <th>Artículo</th>
            <th>EAN</th>
            <th className="!text-right">Pedida</th>
            <th className="!text-right">Asignada</th>
            <th className="!text-right">Preparada</th>
            <th className="!text-right">Rotura</th>
          </tr>
        </thead>
        <tbody>
          {pedido.lineas.map((l) => (
            <tr key={l.numero}>
              <td>{l.numero}</td>
              <td>
                <Link to="/oficina/stock/$id" params={{ id: l.articuloId }} className="hover:underline">
                  {l.articulo}
                </Link>
              </td>
              <td className="codigo">{l.ean}</td>
              <td className="text-right">{entero(l.pedida)}</td>
              <td className="text-right">{entero(l.asignada)}</td>
              <td className="text-right">{entero(l.preparada)}</td>
              <td className={`text-right ${l.rotura > 0 && ["Preparado", "Embalado", "Expedido"].includes(r.estado) ? "text-mal" : "text-suave"}`}>
                {["Preparado", "Embalado", "Expedido"].includes(r.estado) ? entero(l.rotura) : "—"}
              </td>
            </tr>
          ))}
        </tbody>
      </Tabla>

      {pedido.preparados.length > 0 && (
        <section aria-labelledby="preparado">
          <h2 id="preparado" className="mb-2 font-semibold">
            Preparado, por lote
          </h2>
          <Tabla titulo="Preparado por lote">
            <thead>
              <tr>
                <th>Artículo</th>
                <th>Lote</th>
                <th>Caducidad</th>
                <th className="!text-right">Unidades</th>
                <th className="!text-right">Embaladas</th>
              </tr>
            </thead>
            <tbody>
              {pedido.preparados.map((p) => (
                <tr key={`${p.articuloId}-${p.lote}`}>
                  <td>{p.articulo}</td>
                  <td className="codigo">
                    {p.lote ? (
                      <Link to="/oficina/stock/$id/lotes/$lote" params={{ id: p.articuloId, lote: p.lote }} className="text-acento hover:underline">
                        {p.lote}
                      </Link>
                    ) : (
                      "—"
                    )}
                  </td>
                  <td>{dia(p.caducidad)}</td>
                  <td className="text-right">{entero(p.cantidad)}</td>
                  <td className="text-right">{entero(p.embalado)}</td>
                </tr>
              ))}
            </tbody>
          </Tabla>
        </section>
      )}

      {pedido.bultos.length > 0 && (
        <section aria-labelledby="bultos">
          <h2 id="bultos" className="mb-2 font-semibold">
            Bultos
          </h2>
          <ul className="flex flex-col gap-2">
            {pedido.bultos.map((b) => (
              <li key={b.numero} className="rounded-md border border-borde bg-superficie px-3 py-2 text-sm">
                Bulto {b.numero} · <span className="codigo">{b.sscc}</span> {!b.cerrado && <Estado tono="aviso">abierto</Estado>}
                {b.cerrado && (
                  <a className="ml-2 text-acento underline" href={`/api/pedidos/${id}/bultos/${b.numero}/etiqueta.pdf`} target="_blank" rel="noreferrer">
                    Etiqueta
                  </a>
                )}
                <span className="block text-suave">{b.contenido.map((c) => `${c.cantidad} × ${c.articulo}${c.lote ? ` (${c.lote})` : ""}`).join(" · ")}</span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
