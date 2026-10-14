# Cambios

Formato de [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versiones con
[versionado semántico](https://semver.org/lang/es/).

## [0.1.0] - 2026-10-14

Primera versión.

### Añadido

- Dominio del almacén: códigos GS1 (GTIN, GS1-128, SSCC), mapa con recorrido en serpentina, libro de stock con
  existencias y reservas, asignación FEFO con vida útil mínima por cliente, ubicación sugerida, tareas, recepciones,
  pedidos con preparación y embalaje, oleadas y conteos ciegos.
- PostgreSQL con EF Core: un esquema por módulo, restricciones del stock, bloqueo por fila en orden fijo y
  concurrencia optimista.
- API con sesión por cookie, roles, errores RFC 9457, avisos en tiempo real (SignalR), PDF de albarán, etiquetas
  SSCC y etiquetas de ubicación.
- Front en React: oficina (panel, pedidos, recepciones, stock, trazabilidad, ubicaciones, inventario) y terminal para
  el móvil o la PDA, con escáner por lector y por cámara.
- Base de demostración con 420 artículos reales de Open Food Facts.
- Pruebas de dominio, de propiedades, de arquitectura, de integración y de concurrencia con PostgreSQL, de la API y
  de extremo a extremo con axe; CI, Dependabot e imagen Docker.
