import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Aviso, Boton, Campo } from "./componentes/ui";
import { api } from "./lib/api";
import { CLAVE_SESION } from "./lib/sesion";
import type { Sesion } from "./lib/tipos";

export function Entrar() {
  const [usuario, setUsuario] = useState("");
  const [contrasena, setContrasena] = useState("");
  const clientes = useQueryClient();
  const entrar = useMutation({
    mutationFn: () => api.post<Sesion>("/api/sesion", { usuario, contrasena }),
    onSuccess: (sesion) => clientes.setQueryData(CLAVE_SESION, sesion),
  });

  return (
    <main className="mx-auto flex min-h-dvh max-w-sm flex-col justify-center px-4">
      <h1 className="text-2xl font-semibold tracking-tight">SGA · almacén</h1>
      <p className="mt-1 text-sm text-suave">Entra con tu usuario del almacén.</p>
      <form
        className="mt-6 flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault();
          entrar.mutate();
        }}
      >
        <Campo id="usuario" etiqueta="Usuario" autoComplete="username" value={usuario} onChange={(e) => setUsuario(e.target.value)} required />
        <Campo
          id="contrasena"
          etiqueta="Contraseña"
          type="password"
          autoComplete="current-password"
          value={contrasena}
          onChange={(e) => setContrasena(e.target.value)}
          required
        />
        <Aviso error={entrar.error} />
        <Boton type="submit" variante="principal" grande disabled={entrar.isPending}>
          {entrar.isPending ? "Entrando…" : "Entrar"}
        </Boton>
      </form>
    </main>
  );
}
