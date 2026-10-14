import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useState } from "react";
import { Escaner } from "../componentes/Escaner";
import { Aviso, Boton, Campo, Cargando, Vacio } from "../componentes/ui";
import { api } from "../lib/api";
import { ESTADOS_DE_CONTEO } from "../lib/formato";
import { leer } from "../lib/gs1";
import { bien, mal } from "../lib/sonido";
import type { ConteoDetalle, ConteoResumen } from "../lib/tipos";

export function Contar() {
  const { data, isPending } = useQuery({ queryKey: ["conteos"], queryFn: () => api.get<ConteoResumen[]>("/api/conteos") });
  if (isPending) return <Cargando />;
  const abiertos = (data ?? []).filter((c) => c.estado === "Abierto" || c.estado === "Recuento");
  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Contar</h1>
        <Link to="/terminal" className="text-acento">
          Menú
        </Link>
      </div>
      {abiertos.length === 0 && <Vacio>No hay conteos abiertos.</Vacio>}
      {abiertos.map((c) => (
        <Link
          key={c.id}
          to="/terminal/contar/$id"
          params={{ id: c.id }}
          className="block rounded-md border border-borde bg-superficie px-4 py-3 hover:border-acento"
        >
          <span className="codigo text-lg font-semibold">{c.numero}</span>
          <span className="ml-2 text-suave">
            {ESTADOS_DE_CONTEO[c.estado]} · {c.ubicaciones} ubicaciones
          </span>
        </Link>
      ))}
    </div>
  );
}

/**
 * Conteo ciego: el operario escanea el hueco, el artículo y escribe lo que hay, sin ver lo que el sistema espera.
 * En el recuento solo se vuelve a contar lo que no cuadró (tampoco se dice cuánto se esperaba).
 */
export function ContarConteo() {
  const { id } = useParams({ from: "/terminal/contar/$id" });
  const clientes = useQueryClient();
  const { data: conteo } = useQuery({ queryKey: ["conteo", id], queryFn: () => api.get<ConteoDetalle>(`/api/conteos/${id}`) });
  const [ubicacion, setUbicacion] = useState<string | null>(null);
  const [articulo, setArticulo] = useState<string | null>(null);
  const [lote, setLote] = useState("");
  const [cantidad, setCantidad] = useState("");
  const [error, setError] = useState<string | null>(null);
  const recargar = () => clientes.invalidateQueries({ queryKey: ["conteo", id] });

  const contar = useMutation({
    mutationFn: () =>
      api.post(`/api/conteos/${id}/cantidades`, {
        codigoDeUbicacion: ubicacion,
        codigoDeArticulo: articulo,
        lote: lote || null,
        caducidad: null,
        cantidad: Number(cantidad),
      }),
    onSuccess: () => {
      bien();
      setArticulo(null);
      setLote("");
      setCantidad("");
      setError(null);
      void recargar();
    },
    onError: (e) => {
      mal();
      setError(e.message);
    },
  });
  const terminar = useMutation({ mutationFn: () => api.post(`/api/conteos/${id}/vuelta`), onSuccess: () => void recargar() });

  if (!conteo) return <Cargando />;
  const abierto = conteo.estado === "Abierto" || conteo.estado === "Recuento";

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="codigo text-2xl font-semibold">{conteo.numero}</h1>
        <Link to="/terminal/contar" className="text-acento">
          Conteos
        </Link>
      </div>
      <p className="text-suave">
        {ESTADOS_DE_CONTEO[conteo.estado]}
        {conteo.estado === "Recuento" && ": vuelve a contar lo que no cuadró, sin fiarte de la primera vez."}
      </p>
      <p className="codigo text-sm text-suave">{conteo.ubicaciones.join(" · ")}</p>
      {error && <Aviso>{error}</Aviso>}

      {abierto && !ubicacion && (
        <Escaner
          etiqueta="Escanea el hueco que vas a contar"
          onLeido={(c) => {
            const codigo = c.toUpperCase();
            if (conteo.ubicaciones.includes(codigo)) {
              bien();
              setError(null);
              setUbicacion(codigo);
            } else {
              mal();
              setError(`${codigo} no está en este conteo.`);
            }
          }}
        />
      )}
      {abierto && ubicacion && !articulo && (
        <>
          <p>
            Contando <span className="codigo font-semibold">{ubicacion}</span>
          </p>
          <Escaner
            etiqueta="Escanea un artículo del hueco"
            onLeido={(c) => {
              const lectura = leer(c);
              if (!lectura.gtin) {
                mal();
                setError("Eso no es un artículo.");
                return;
              }
              bien();
              setError(null);
              setArticulo(c);
              setLote(lectura.lote ?? "");
            }}
          />
          <Boton variante="discreto" onClick={() => setUbicacion(null)}>
            Otro hueco
          </Boton>
        </>
      )}
      {abierto && ubicacion && articulo && (
        <form
          className="flex flex-col gap-3 rounded-md border border-borde bg-superficie p-4"
          onSubmit={(e) => {
            e.preventDefault();
            contar.mutate();
          }}
        >
          <p className="codigo">
            {ubicacion} · {leer(articulo).gtin}
          </p>
          <Campo id="lote-contado" etiqueta="Lote (si lo lleva)" className="codigo" value={lote} onChange={(e) => setLote(e.target.value)} />
          <Campo
            id="cantidad-contada"
            etiqueta="Unidades que hay"
            type="number"
            inputMode="numeric"
            min={0}
            required
            className="cifras text-xl"
            value={cantidad}
            onChange={(e) => setCantidad(e.target.value)}
          />
          <Boton grande type="submit" variante="principal" disabled={contar.isPending}>
            Anotar
          </Boton>
          <Boton variante="discreto" onClick={() => setArticulo(null)}>
            Cancelar
          </Boton>
        </form>
      )}

      {conteo.lineas.length > 0 && (
        <ul className="flex flex-col divide-y divide-borde rounded-md border border-borde bg-superficie" aria-label="Lo contado">
          {conteo.lineas.map((l) => (
            <li key={`${l.ubicacion}-${l.ean}-${l.lote}`} className="flex justify-between gap-3 px-4 py-2">
              <span>
                <span className="codigo">{l.ubicacion}</span> · {l.articulo}
                {l.lote && <span className="codigo text-suave"> · {l.lote}</span>}
              </span>
              <span className="cifras">{l.segunda ?? l.primera}</span>
            </li>
          ))}
        </ul>
      )}

      {abierto && (
        <Boton grande onClick={() => terminar.mutate()} disabled={terminar.isPending}>
          {conteo.estado === "Abierto" ? "He terminado de contar" : "He terminado el recuento"}
        </Boton>
      )}
      {!abierto && <p className="text-suave">Esta vuelta está terminada. Lo que quede, lo revisa el jefe de almacén.</p>}
    </div>
  );
}
