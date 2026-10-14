import { expect, test } from "@playwright/test";
import { api, entrar, sinProblemasDeAccesibilidad } from "./ayudas";

test("el panel enseña el almacén de un vistazo", async ({ page }) => {
  await entrar(page, "jefa");
  await expect(page.getByRole("heading", { name: "Panel" })).toBeVisible();
  await expect(page.getByText("líneas preparadas hoy", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Pedidos", exact: true })).toBeVisible();
  await sinProblemasDeAccesibilidad(page);
});

test("un pedido nuevo, su asignación de stock y su oleada", async ({ page, request }) => {
  const sesion = await api(request);
  const articulos = await sesion.get<{ id: string; nombre: string; ean: string; disponible: number; unidadesPorCaja: number }[]>(
    "/api/articulos",
  );
  const conStock = articulos.find((a) => a.disponible >= a.unidadesPorCaja * 2)!;
  // El pedido que se va a crear será el siguiente de la serie.
  const numero = `PED-${String((await sesion.get<unknown[]>("/api/pedidos")).length + 1).padStart(6, "0")}`;

  await entrar(page, "jefa");
  await page.getByRole("link", { name: "Pedidos" }).click();
  await page.getByRole("button", { name: "Nuevo pedido" }).click();
  await expect(page.getByLabel("Cliente").getByRole("option")).not.toHaveCount(1);
  await page.getByLabel("Cliente").selectOption({ index: 1 });
  await page.getByLabel("Añadir artículo").fill(conStock.ean);
  await page.getByRole("listbox").getByRole("option").first().click();
  await expect(page.getByLabel(`Unidades de ${conStock.nombre}`)).toHaveValue(String(conStock.unidadesPorCaja));
  await page.getByRole("button", { name: "Crear el pedido" }).click();

  await page.getByRole("button", { name: "Pendiente", exact: true }).click();
  await page.getByRole("checkbox", { name: `Elegir ${numero}` }).check();
  await page.getByRole("button", { name: "Asignar stock" }).click();
  await expect(page.getByRole("status")).toContainText(`${numero}: ${conStock.unidadesPorCaja} de ${conStock.unidadesPorCaja}`);

  await page.getByRole("button", { name: "Asignado", exact: true }).click();
  await page.getByRole("checkbox", { name: `Elegir ${numero}` }).check();
  await page.getByRole("button", { name: "Lanzar oleada" }).click();
  await expect(page.getByRole("status")).toContainText("las tareas ya están en el terminal");
  await sinProblemasDeAccesibilidad(page);
});

test("el stock de un artículo y la trazabilidad de un lote", async ({ page, request }) => {
  const sesion = await api(request);
  const articulos = await sesion.get<{ id: string; nombre: string; stock: number; controlDeLote: boolean }[]>("/api/articulos");
  const articulo = articulos.find((a) => a.stock > 0 && a.controlDeLote)!;

  await entrar(page, "jefa");
  await page.goto(`/oficina/stock/${articulo.id}`);
  await expect(page.getByRole("region", { name: "Stock por ubicación y lote" })).toBeVisible();
  await page.getByRole("region", { name: "Stock por ubicación y lote" }).getByRole("link").first().click();
  await expect(page.getByRole("heading", { name: /^Lote/ })).toBeVisible();
  await expect(page.getByRole("region", { name: "Movimientos del lote" })).toContainText("Recepción");
  await sinProblemasDeAccesibilidad(page);
});

test("la oficina no ve lo que no le toca", async ({ page }) => {
  await entrar(page, "oficina");
  await expect(page.getByRole("link", { name: "Inventario" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Lanzar oleada" })).toHaveCount(0);
});

for (const ruta of ["/oficina/pedidos", "/oficina/recepciones", "/oficina/stock", "/oficina/ubicaciones", "/oficina/inventario"]) {
  test(`sin problemas de accesibilidad: ${ruta}`, async ({ page }) => {
    await entrar(page, "jefa");
    await page.goto(ruta);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    await expect(page.getByText("Cargando…")).toHaveCount(0);
    await sinProblemasDeAccesibilidad(page);
  });
}
