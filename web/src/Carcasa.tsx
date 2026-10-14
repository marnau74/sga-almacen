import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Link, useRouterState } from "@tanstack/react-router";
import type { ReactNode } from "react";
import { api } from "./lib/api";
import { useAvisos } from "./lib/avisos";
import { CLAVE_SESION, esJefe, useSesion, veOficina, veTerminal } from "./lib/sesion";

const OFICINA = [
  { a: "/oficina", texto: "Panel", exacto: true },
  { a: "/oficina/pedidos", texto: "Pedidos" },
  { a: "/oficina/recepciones", texto: "Recepciones" },
  { a: "/oficina/stock", texto: "Stock" },
  { a: "/oficina/ubicaciones", texto: "Ubicaciones" },
  { a: "/oficina/inventario", texto: "Inventario", jefe: true },
] as const;

/** La oficina (claro, con menú) y el terminal (oscuro, sin distracciones) comparten sesión y avisos. */
export function Carcasa({ children }: { children: ReactNode }) {
  const sesion = useSesion();
  const ruta = useRouterState({ select: (s) => s.location.pathname });
  const enTerminal = ruta.startsWith("/terminal");
  const clientes = useQueryClient();
  useAvisos(true);
  const salir = useMutation({
    mutationFn: () => api.delete("/api/sesion"),
    onSuccess: () => {
      clientes.clear();
      clientes.setQueryData(CLAVE_SESION, null);
    },
  });

  const cabecera = (
    <header className="flex items-center justify-between gap-3 border-b border-borde bg-superficie px-4 py-2">
      <Link to="/" className="text-base font-semibold tracking-tight">
        SGA <span className="font-normal text-suave">· almacén</span>
      </Link>
      <div className="flex items-center gap-3 text-sm">
        {veOficina(sesion) && veTerminal(sesion) && (
          <Link to={enTerminal ? "/oficina" : "/terminal"} className="text-acento hover:underline">
            {enTerminal ? "Oficina" : "Terminal"}
          </Link>
        )}
        <span className="hidden text-suave sm:inline">{sesion.nombre}</span>
        <button type="button" className="text-acento hover:underline" onClick={() => salir.mutate()}>
          Salir
        </button>
      </div>
    </header>
  );

  if (enTerminal) {
    return (
      <div className="terminal min-h-dvh bg-fondo text-texto">
        {cabecera}
        <main className="mx-auto max-w-xl px-4 py-4">{children}</main>
      </div>
    );
  }

  return (
    <div className="min-h-dvh">
      {cabecera}
      <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-4 md:flex-row">
        <nav aria-label="Oficina" className="md:w-44 md:shrink-0">
          <ul className="flex gap-1 overflow-x-auto md:flex-col">
            {OFICINA.filter((e) => !("jefe" in e) || esJefe(sesion)).map((e) => (
              <li key={e.a}>
                <Link
                  to={e.a}
                  activeOptions={{ exact: "exacto" in e }}
                  className="block whitespace-nowrap rounded-md px-3 py-2 text-sm text-suave hover:bg-superficie hover:text-texto"
                  activeProps={{ className: "bg-superficie font-medium !text-texto border border-borde" }}
                >
                  {e.texto}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
        <main className="min-w-0 flex-1">{children}</main>
      </div>
    </div>
  );
}
