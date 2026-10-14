import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from "react";
import { ErrorDeApi } from "../lib/api";

type Variante = "principal" | "normal" | "peligro" | "discreto";

const VARIANTES: Record<Variante, string> = {
  principal: "bg-acento text-acento-texto hover:brightness-110",
  normal: "bg-superficie text-texto border border-borde hover:border-suave",
  peligro: "bg-superficie text-mal border border-mal/50 hover:bg-mal-suave",
  discreto: "text-acento hover:underline",
};

export function Boton({
  variante = "normal",
  grande = false,
  className = "",
  ...resto
}: ButtonHTMLAttributes<HTMLButtonElement> & { variante?: Variante; grande?: boolean }) {
  return (
    <button
      type="button"
      {...resto}
      className={`inline-flex items-center justify-center gap-2 rounded-md font-medium transition disabled:cursor-not-allowed disabled:opacity-50 ${
        grande ? "min-h-14 text-lg" : "min-h-9 text-sm"
      } ${variante === "discreto" ? "" : grande ? "px-5" : "px-3"} ${VARIANTES[variante]} ${className}`}
    />
  );
}

export type Tono = "neutro" | "acento" | "bien" | "aviso" | "mal";

const TONOS: Record<Tono, string> = {
  neutro: "bg-fondo text-suave border-borde",
  acento: "bg-acento-suave text-acento border-acento/30",
  bien: "bg-bien-suave text-bien border-bien/30",
  aviso: "bg-aviso-suave text-aviso border-aviso/30",
  mal: "bg-mal-suave text-mal border-mal/30",
};

/** Una etiqueta de estado: siempre con texto, el color solo acompaña. */
export function Estado({ tono = "neutro", children }: { tono?: Tono; children: ReactNode }) {
  return (
    <span className={`inline-flex items-center whitespace-nowrap rounded border px-1.5 py-0.5 text-xs font-medium ${TONOS[tono]}`}>
      {children}
    </span>
  );
}

export function Campo({
  etiqueta,
  ayuda,
  id,
  ...resto
}: InputHTMLAttributes<HTMLInputElement> & { etiqueta: string; ayuda?: string; id: string }) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-sm font-medium">
        {etiqueta}
      </label>
      <input
        id={id}
        {...resto}
        className={`min-h-10 rounded-md border border-borde bg-superficie px-3 text-texto placeholder:text-suave ${resto.className ?? ""}`}
      />
      {ayuda && <p className="text-xs text-suave">{ayuda}</p>}
    </div>
  );
}

export function Aviso({ error, children }: { error?: unknown; children?: ReactNode }) {
  if (!error && !children) return null;
  const texto = error instanceof ErrorDeApi || error instanceof Error ? error.message : children;
  return (
    <p role="alert" className="rounded-md border border-mal/40 bg-mal-suave px-3 py-2 text-sm text-mal">
      {texto}
    </p>
  );
}

export function Cargando() {
  return <p className="py-6 text-sm text-suave">Cargando…</p>;
}

export function Vacio({ children }: { children: ReactNode }) {
  return <p className="py-6 text-sm text-suave">{children}</p>;
}

export function Titulo({ children, accion }: { children: ReactNode; accion?: ReactNode }) {
  return (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <h1 className="text-xl font-semibold tracking-tight">{children}</h1>
      {accion}
    </div>
  );
}

/** Una tabla densa, que en pantallas estrechas se desplaza en horizontal dentro de su caja. */
export function Tabla({ titulo, children }: { titulo: string; children: ReactNode }) {
  return (
    <div className="overflow-x-auto rounded-md border border-borde bg-superficie" role="region" aria-label={titulo} tabIndex={0}>
      <table className="cifras w-full border-collapse text-sm [&_td]:border-t [&_td]:border-borde [&_td]:px-3 [&_td]:py-2 [&_th]:px-3 [&_th]:py-2 [&_th]:text-left [&_th]:font-medium [&_th]:text-suave">
        <caption className="sr-only">{titulo}</caption>
        {children}
      </table>
    </div>
  );
}

export function Cifra({ valor, texto, tono }: { valor: ReactNode; texto: string; tono?: "mal" | "aviso" }) {
  return (
    <div className="rounded-md border border-borde bg-superficie p-4">
      <p className={`cifras text-3xl font-semibold ${tono === "mal" ? "text-mal" : tono === "aviso" ? "text-aviso" : ""}`}>{valor}</p>
      <p className="mt-1 text-sm text-suave">{texto}</p>
    </div>
  );
}
