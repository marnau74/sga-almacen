import { useQuery, useQueryClient } from "@tanstack/react-query";
import { createContext, useContext, useEffect, type ReactNode } from "react";
import { api, ErrorDeApi } from "./api";
import type { Rol, Sesion } from "./tipos";

const ContextoDeSesion = createContext<Sesion | null>(null);

export const CLAVE_SESION = ["sesion"] as const;

/** Quién ha entrado (o null). Se pregunta a la API una vez; un 401 en cualquier petición la vuelve a pedir. */
export function useSesionDeLaApi() {
  const clientes = useQueryClient();
  const consulta = useQuery({
    queryKey: CLAVE_SESION,
    queryFn: async () => {
      try {
        return await api.get<Sesion>("/api/sesion");
      } catch (e) {
        if (e instanceof ErrorDeApi && e.estado === 401) return null;
        throw e;
      }
    },
    staleTime: Infinity,
  });
  useEffect(() => {
    const caducada = () => clientes.setQueryData(CLAVE_SESION, null);
    window.addEventListener("sesion-caducada", caducada);
    return () => window.removeEventListener("sesion-caducada", caducada);
  }, [clientes]);
  return consulta;
}

export function ProveedorDeSesion({ sesion, children }: { sesion: Sesion; children: ReactNode }) {
  return <ContextoDeSesion.Provider value={sesion}>{children}</ContextoDeSesion.Provider>;
}

export function useSesion() {
  const sesion = useContext(ContextoDeSesion);
  if (!sesion) throw new Error("useSesion fuera de una sesión");
  return sesion;
}

export function tieneRol(sesion: Sesion, ...roles: Rol[]) {
  return sesion.roles.some((r) => roles.includes(r));
}

export const esJefe = (s: Sesion) => tieneRol(s, "Administrador", "JefeDeAlmacen");
export const veOficina = (s: Sesion) => tieneRol(s, "Administrador", "JefeDeAlmacen", "Administracion");
export const veTerminal = (s: Sesion) => tieneRol(s, "Administrador", "JefeDeAlmacen", "Operario");
