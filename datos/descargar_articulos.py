"""Descarga de Open Food Facts los artículos de la demostración: productos vendidos en España, de varias categorías.

    python datos/descargar_articulos.py

Escribe datos/articulos.json. Los datos son de Open Food Facts (https://world.openfoodfacts.org), con licencia
Open Database License (ODbL): ver datos/LICENSE-ODbL.md. Solo se quedan los que tienen un EAN-13 válido, nombre y
formato. Usa su buscador (search.openfoodfacts.org) y espera entre una búsqueda y otra para no cargarlo.
"""

import json
import time
import urllib.parse
import urllib.request
from pathlib import Path

AGENTE = "sga-almacen/0.1 (proyecto de portfolio; https://github.com/marnau74/sga-almacen)"
POR_CATEGORIA = 30

# Categoría de Open Food Facts -> (nombre en la demo, lleva caducidad). Todos llevan lote; la sal y el azúcar no
# caducan (no tienen fecha de consumo).
CATEGORIAS = {
    "milks": ("Leche", True),
    "yogurts": ("Yogures", True),
    "cheeses": ("Quesos", True),
    "olive-oils": ("Aceite de oliva", True),
    "pastas": ("Pasta", True),
    "rices": ("Arroz", True),
    "canned-tunas": ("Conservas de atún", True),
    "breakfast-cereals": ("Cereales", True),
    "biscuits": ("Galletas", True),
    "coffees": ("Café", True),
    "chickpeas": ("Legumbres", True),
    "tomato-sauces": ("Tomate frito", True),
    "salts": ("Sal", False),
    "sugars": ("Azúcar", False),
}


def ean_valido(codigo: str) -> bool:
    if len(codigo) != 13 or not codigo.isdigit():
        return False
    suma = sum(int(c) * (3 if i % 2 else 1) for i, c in enumerate(codigo[:12]))
    return (10 - suma % 10) % 10 == int(codigo[12])


CACHE = Path(__file__).with_name(".cache-openfoodfacts")


def buscar(categoria: str) -> list[dict] | None:
    """Los productos de una categoría. Se guardan en una caché local para poder reanudar si la API falla."""
    guardado = CACHE / f"{categoria}.json"
    if guardado.exists():
        return json.loads(guardado.read_text(encoding="utf-8"))
    parametros = urllib.parse.urlencode({
        "q": f'categories_tags:"en:{categoria}" AND countries_tags:"en:spain"',
        "fields": "code,product_name_es,product_name,brands,quantity",
        "page_size": 100,
        "sort_by": "-unique_scans_n",
    })
    peticion = urllib.request.Request(f"https://search.openfoodfacts.org/search?{parametros}",
                                      headers={"User-Agent": AGENTE})
    for intento in range(5):
        try:
            with urllib.request.urlopen(peticion, timeout=60) as respuesta:
                productos = json.load(respuesta)["hits"]
            CACHE.mkdir(exist_ok=True)
            guardado.write_text(json.dumps(productos, ensure_ascii=False), encoding="utf-8")
            return productos
        except Exception as error:  # noqa: BLE001 - la API a veces responde HTML con un error temporal
            print(f"  {categoria}: {error}; reintento")
            time.sleep(20 * (intento + 1))
    print(f"  {categoria}: Open Food Facts no responde; se omite (vuelve a ejecutar el script para reintentarla)")
    return None


def marca(marcas: str | list[str] | None) -> str | None:
    """La primera marca: el buscador las da como lista y la API antigua como texto separado por comas."""
    primera = (marcas[0] if marcas else "") if isinstance(marcas, list) else (marcas or "").split(",")[0]
    return primera.strip() or None


def main() -> None:
    articulos, vistos = [], set()
    for categoria, (nombre_categoria, caduca) in CATEGORIAS.items():
        elegidos = 0
        productos = buscar(categoria)
        if productos is None:
            continue
        for producto in productos:
            codigo = (producto.get("code") or "").strip()
            nombre = (producto.get("product_name_es") or producto.get("product_name") or "").strip()
            formato = (producto.get("quantity") or "").strip()
            if not ean_valido(codigo) or codigo in vistos or not nombre or not formato or len(nombre) > 120:
                continue
            vistos.add(codigo)
            articulos.append({
                "ean": codigo,
                "nombre": nombre,
                "marca": marca(producto.get("brands")),
                "formato": formato[:60],
                "categoria": nombre_categoria,
                "controlDeLote": True,
                "controlDeCaducidad": caduca,
            })
            elegidos += 1
            if elegidos == POR_CATEGORIA:
                break
        print(f"{nombre_categoria}: {elegidos}")
        time.sleep(3)
    destino = Path(__file__).with_name("articulos.json")
    destino.write_text(json.dumps(articulos, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"{len(articulos)} artículos en {destino}")


if __name__ == "__main__":
    main()
