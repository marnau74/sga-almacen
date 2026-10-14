# 3. Monolito modular en .NET y un front en React con dos caras

## Contexto

Un SGA tiene partes bien separadas (catálogo, mapa del almacén, stock, entradas, salidas, inventario) que comparten
la misma transacción en los momentos clave: confirmar un picking mueve stock, consume una reserva, actualiza el
pedido y puede crear una reposición, todo o nada. Y lo usan dos tipos de persona: la oficina, en un ordenador, y los
operarios, de pie, con un móvil o una PDA con lector y a veces con guantes.

## Decisión

- **Un solo despliegue, cuatro proyectos**: `Dominio` (las reglas, sin dependencias: ni EF ni ASP.NET),
  `Aplicacion` (los casos de uso), `Infraestructura` (PostgreSQL con EF Core, identidad, PDF) y `Api`. Dentro, un
  espacio de nombres por módulo y **un esquema de base de datos por módulo** (`catalogo`, `almacen`, `stock`,
  `entradas`, `salidas`, `inventario`, `identidad`).
- **La aplicación usa EF Core a través de una interfaz** (`IContexto`): el `DbContext` ya es unidad de trabajo y
  repositorio, y lo que EF no hace solo (bloquear filas, numerar documentos) está en esa interfaz. Las reglas están
  en el dominio, que se prueba sin base de datos; la aplicación se prueba contra PostgreSQL real, porque lo que
  importa ahí (bloqueos, restricciones) no se puede simular.
- **Sesión con cookie** (ASP.NET Core Identity, `HttpOnly`, `SameSite=Strict`) en vez de tokens en el navegador: el
  front y la API son el mismo sitio. Bloqueo de la cuenta tras cinco intentos fallidos y límite de peticiones al
  entrar. Cada acción se autoriza en la API por rol.
- **Un solo front (React + TypeScript), dos caras**:
  - *Oficina*: tablas densas, filtros, panel, PDF.
  - *Terminal*: una tarea por pantalla, instrucción grande, alto contraste, botones de 56 px, y todo se confirma
    **escaneando**: con el lector de la PDA (que escribe y pulsa Intro) o con la cámara del móvil (`BarcodeDetector`
    o zxing-wasm servido desde el propio sitio). Pitido agudo si va bien; dos graves y vibración si no.
- **Avisos en tiempo real** con SignalR: solo dicen «ha cambiado esto» y el front vuelve a pedir lo que enseña. Así
  el panel y las listas del terminal se actualizan solos sin duplicar la lógica de permisos en los mensajes.

## Alternativas descartadas

- **Microservicios**: obligarían a coordinar entre servicios lo que aquí es una transacción.
- **Una app nativa para el terminal**: una PWA funciona en cualquier móvil y en las PDA Android con lector sin
  instalar nada, y comparte el código con la oficina.
