import { expect, test } from "@playwright/test";
import { api, entrar, sinProblemasDeAccesibilidad } from "./ayudas";

interface Tarea {
  id: string;
  tipo: string;
  estado: string;
  ean: string;
  origen: string;
  destino: string;
  cantidad: number;
  articulo: string;
}

test("preparar: ir al hueco, escanear y confirmar; un hueco equivocado se rechaza", async ({ page, request }) => {
  const sesion = await api(request, "operario1");
  const tareas = await sesion.get<Tarea[]>("/api/tareas?tipo=Picking");
  // El terminal empieza por la que el operario ya tenga en curso y, si no, por la primera libre: la misma lista.
  const primera = tareas[0]!;

  await entrar(page, "operario1");
  await expect(page.getByRole("heading", { name: /Hola/ })).toBeVisible();
  await sinProblemasDeAccesibilidad(page);
  await page.getByRole("link", { name: /Preparar pedidos/ }).click();
  await page.getByRole("button", { name: "Empezar por la primera" }).click();

  await expect(page.getByRole("region", { name: "Qué hay que hacer" })).toContainText(primera.origen);
  // Un hueco que no es: pita y lo dice.
  await page.getByLabel(/Escanea el hueco/).fill("R-99-99-9");
  await page.keyboard.press("Enter");
  await expect(page.getByRole("alert")).toContainText(`tienes que estar en ${primera.origen}`);

  await page.getByLabel(/Escanea el hueco/).fill(primera.origen);
  await page.keyboard.press("Enter");
  await page.getByLabel(/Escanea el artículo/).fill(primera.ean);
  await page.keyboard.press("Enter");
  await expect(page.getByRole("button", { name: `Confirmar ${primera.cantidad}` })).toBeVisible();
  await sinProblemasDeAccesibilidad(page);
  await page.getByRole("button", { name: `Confirmar ${primera.cantidad}` }).click();

  // Pasa sola a la siguiente (o al menú si era la última).
  await expect(page).not.toHaveURL(new RegExp(primera.id));
  const despues = await sesion.get<Tarea[]>("/api/tareas?tipo=Picking");
  expect(despues.some((t) => t.id === primera.id)).toBe(false);
});

test("recibir: escanear un bulto con su etiqueta GS1 rellena lote, caducidad y cantidad", async ({ page, request }) => {
  // Una recepción nueva, para no depender de lo que ya se haya recibido de la demo.
  const jefa = await api(request, "jefa");
  const [proveedor] = await jefa.get<{ id: string }[]>("/api/proveedores");
  const articulo = (await jefa.get<{ id: string; ean: string; nombre: string; controlDeLote: boolean }[]>("/api/articulos?buscar=leche")).find(
    (a) => a.controlDeLote,
  )!;
  const creada = await jefa.post("/api/recepciones", {
    proveedorId: proveedor!.id,
    fechaPrevista: new Date().toISOString().slice(0, 10),
    lineas: [{ articuloId: articulo.id, cantidad: 12 }],
  });
  const recepcion = (await creada.json()) as { id: string; numero: string };
  const linea = { ean: articulo.ean, prevista: 12 };
  await entrar(page, "operario2");
  await page.getByRole("link", { name: "Recibir" }).click();
  await page.getByRole("link", { name: new RegExp(recepcion.numero) }).click();
  await page.getByLabel("Escanea un bulto").fill(`(01)0${linea.ean}(17)271231(10)E2E1(37)${linea.prevista}`);
  await page.keyboard.press("Enter");

  await expect(page.getByLabel("Lote")).toHaveValue("E2E1");
  await expect(page.getByLabel("Caducidad")).toHaveValue("2027-12-31");
  await expect(page.getByLabel("Unidades")).toHaveValue(String(linea.prevista));
  await sinProblemasDeAccesibilidad(page);
  await page.getByRole("button", { name: "Recibir", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("tarea");
  await expect(page.getByRole("list", { name: "Líneas" })).toContainText(`${linea.prevista} / ${linea.prevista}`);
});

test("contar: el operario no ve lo que se espera", async ({ page, request }) => {
  const jefa = await api(request, "jefa");
  const ubicaciones = await jefa.get<{ codigo: string; unidades: number; bloqueada: boolean }[]>("/api/ubicaciones?zona=Reserva");
  const vacia = ubicaciones.find((u) => u.unidades === 0 && !u.bloqueada)!;
  const creado = await jefa.post("/api/conteos", { ubicaciones: [vacia.codigo] });
  expect(creado.ok()).toBeTruthy();
  const { numero } = (await creado.json()) as { numero: string };

  await entrar(page, "operario1");
  await page.getByRole("link", { name: "Contar" }).click();
  await page.getByRole("link", { name: new RegExp(numero) }).click();
  await page.getByLabel("Escanea el hueco que vas a contar").fill(vacia.codigo);
  await page.keyboard.press("Enter");
  await expect(page.getByText(`Contando ${vacia.codigo}`)).toBeVisible();
  await expect(page.getByText(/esperad/i)).toHaveCount(0);
  await page.getByRole("button", { name: "He terminado de contar" }).click();
  await expect(page.getByText("Esta vuelta está terminada")).toBeVisible();
});
