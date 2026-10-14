import AxeBuilder from "@axe-core/playwright";
import { expect, type APIRequestContext, type Page } from "@playwright/test";

export const CONTRASENA = "almacen-demo";

export async function entrar(page: Page, usuario: string) {
  await page.goto("/");
  await page.getByLabel("Usuario").fill(usuario);
  await page.getByLabel("Contraseña").fill(CONTRASENA);
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("button", { name: "Salir" })).toBeVisible();
}

/** Una sesión de la API aparte, para averiguar datos de la demo (que dependen del día en que se creó). */
export async function api(request: APIRequestContext, usuario = "jefa") {
  const respuesta = await request.post("/api/sesion", { data: { usuario, contrasena: CONTRASENA } });
  expect(respuesta.ok()).toBeTruthy();
  return {
    get: async <T>(ruta: string) => (await (await request.get(ruta)).json()) as T,
    post: async (ruta: string, data: unknown) => request.post(ruta, { data }),
  };
}

export async function sinProblemasDeAccesibilidad(page: Page) {
  const resultado = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
  expect(resultado.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.length})`)).toEqual([]);
}
