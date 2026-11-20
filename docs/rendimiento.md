# Rendimiento: qué se hizo y cómo se comprobó

Los números salen de este equipo (Windows 11, Docker Desktop, una sola instancia) con los datos del perfil demo, salvo
donde se indica. No son una prueba de carga: sirven para ver que ningún endpoint se acerque al límite de 500 ms y que no haya
consultas de más.

## Resumen de mediciones

| Qué | Resultado | Cómo se midió |
|---|---|---|
| Latencia de 11 endpoints principales | p95 de 6 a 40 ms; el primer llamado (en frío) de 7 a 71 ms | `scripts/medir-api.ps1` (15 repeticiones por ruta) |
| Consultas SQL por petición | Constante: `GET /ahorradores` hace 4 SELECT con 25 filas y 4 con 100 | Diferencia de `Com_select` de MariaDB antes y después de cada llamada (sin N+1) |
| Perfil `full` (≈ 2,200 ahorradores y ≈ 111,000 movimientos) | Todo bajo 0.7 s; dashboard en frío 0.43 s | Medido en la Fase 5, antes de las mejoras de esta fase; ver [indices.md](indices.md) |
| Compresión de la API (100 ahorradores, 13.5 KB) | gzip 2.6 KB, **Brotli 1.9 KB** | `curl` con `Accept-Encoding` |
| JavaScript inicial | ≈ 135 KB gzip (`index` 81 KB + `base` 54 KB) más la pantalla visitada; recharts (118 KB gzip) se carga aparte | Salida de `npm run build` |
| Web vitals reales (escritorio, sin limitar la red) | LCP 928 ms, FCP 928 ms, TTFB 7-20 ms, CLS 0.01 | Los reporta la propia aplicación a `/api/v1/telemetria/web-vitals` (log de la API) |
| Federación (Parte 2) | 0.2-0.8 s por consulta federada; 2-40 ms la directa sobre MariaDB | `scripts/federacion.ps1 verificar` |

**Lighthouse no se ejecutó.** Este equipo no tiene Chrome y la herramienta se descarga con `npx`, que aquí no se pudo usar. Para
correrlo en móvil y con "Slow 4G": `npx lighthouse http://localhost:8080/login --form-factor=mobile --throttling-method=devtools --only-categories=performance,accessibility,best-practices`
(la pantalla de inicio de sesión no pide sesión). Quedó pendiente de hacerse en una máquina con Chrome.

## Lista de la sección 8

| Criterio | Estado | Detalle y comprobación |
|---|---|---|
| Skeleton de carga en vistas con datos | Hecho | `Skeleton` y `DataTable` (esqueletos, vacío, error con reintento); prueba de componentes y recorrido manual |
| Optimistic UI donde es seguro | Hecho | Interruptor de tareas: se refleja al instante y se revierte si el servidor lo rechaza (`modulos/tareas`) |
| Sin peticiones duplicadas | Hecho | TanStack Query con `staleTime` de 30 s y llaves por pantalla; la caja activa y los periodos se comparten entre pantallas |
| Búsqueda con debounce | Hecho | `useDebounce` (350 ms por omisión) en las búsquedas de ahorradores, préstamos, voluntario, nómina y cierre, en el selector de ahorrador (300 ms) y en el simulador de préstamos (400 ms) |
| Peticiones en paralelo | Parcial | En el frontend cada pantalla lanza sus consultas a la vez; en el backend `Task.WhenAll` solo donde no hay un `DbContext` compartido (conteo y página de las consultas federadas, caché). El panel y los listados no se paralelizan: comparten contexto y ya responden en < 40 ms |
| Paginación en toda lista | Hecho | `page`/`pageSize` con máximo 100 en la API |
| Virtualización | Hecho | Vista previa de importaciones (hasta 5,000 renglones) con TanStack Virtual |
| Proyecciones | Hecho | Listados con Dapper o `Select` a DTO; no se cargan entidades completas |
| Sin N+1 | Hecho y medido | Ver tabla de arriba; el número de SELECT no crece con las filas |
| Índices | Hecho | [indices.md](indices.md) con `EXPLAIN`; `scripts/explain-indices.ps1` lo repite |
| Pool de conexiones | Hecho | MySqlConnector: mínimo 2, máximo 50, inactividad 180 s |
| Caché en Redis con invalidación | Hecho | Panel y estado de resultados; la llave lleva una versión por caja que suben las operaciones que cambian los datos. Si Redis cae se calcula sin caché |
| Brotli y gzip | Hecho en la API; gzip en nginx | La API usa Brotli (nivel óptimo) y gzip. nginx oficial no trae Brotli: lo aplica el borde de Cloudflare a los estáticos |
| Caché de navegador con hash | Hecho | `/assets/*` con `Cache-Control: public, immutable` y `Expires` a un año; el HTML con `no-cache` |
| Cola en segundo plano | Hecho | Cierre de ciclo, reportes grandes y "ejecutar ahora" responden 202 y se consultan en `/trabajos/{id}` |
| Code splitting por ruta | Hecho | Cada pantalla es un trozo (rutas `lazy`); la gráfica de inicio se carga aparte (`Grafica.tsx`) |
| `useMemo` / `React.memo` | Parcial | `useMemo` en columnas de tablas y series (44 usos); no se usó `React.memo` porque no se midió ningún re-render que lo justificara |
| Fuentes autoalojadas con pocos pesos | Hecho | Plus Jakarta Sans 400-800 vía `@fontsource`, como archivos (no incrustadas) |
| Dependencias sin usar | Hecho | `depcheck` no se pudo ejecutar aquí; se hizo el mismo cruce con un script (todas las dependencias se importan; las `devDependencies` restantes son herramientas de compilación) |
| Sin scripts de terceros | Hecho y forzado | La CSP (`script-src 'self'`) impide cargarlos |
| Timeouts y reintentos hacia Trino | Hecho | Polly: 3 reintentos con espera creciente solo para lecturas; conexión 3 s; tope de 100 s por consulta |
| OpenTelemetry para detectar > 500 ms | Hecho | Ver abajo |
| Web vitals a un endpoint propio | Hecho | `lib/vitals.ts` → `/api/v1/telemetria/web-vitals` |
| Lighthouse móvil / Slow 4G | Pendiente | Ver la nota de arriba |

## Observabilidad

- **Trazas** (OpenTelemetry): ASP.NET Core, `HttpClient` (las llamadas a Trino) y las consultas SQL (MySqlConnector, con el texto de la consulta
  parametrizada). Con `OTEL_EXPORTER_OTLP_ENDPOINT` se exportan por OTLP (Jaeger, Tempo, Grafana, etc.); con `Telemetria__Consola=true` (o en
  desarrollo) salen por consola; si no, no se exporta nada.
- **Solicitudes lentas**: cada petición a `/api` se mide; las que pasan de 500 ms (`Telemetria:UmbralLentoMs`) dejan un aviso en el log con la
  **plantilla de la ruta** (`api/v1/ahorradores/{id}`, no la URL), y alimentan `caja.solicitudes_lentas` y el histograma `caja.solicitud.duracion`.
  Buscar `Solicitud lenta` en `docker compose logs api`.
- **Métricas web de usuarios reales**: el navegador manda LCP, CLS, INP, FCP y TTFB con `sendBeacon`; la ruta es anónima pero solo acepta
  una lista cerrada de métricas y rutas, valores acotados y tiene límite de 60 por minuto por IP. Se registran en el log y en `caja.web.vital`.

Las consultas federadas (Parte 2) tardan por naturaleza entre 0.2 y 1.5 s (Trino arranca una consulta distribuida contra tres motores): salen en el aviso
de solicitudes lentas y es lo esperado. El estado de la federación (`GET /consultas-distribuidas`) hace tres conteos y fue la solicitud más lenta
medida (1.5 s en frío); si molesta, el siguiente paso es cachearlo unos segundos en Redis.

## No aplica

- **SSR/SSG**: es una SPA detrás de un inicio de sesión; no hay contenido público que indexar.
- **Respuestas en streaming**: no hay nada que se transmita poco a poco; cada pantalla pide datos acotados.
- **Balanceo de carga**: una sola instancia de la API. nginx ya está preparado (basta un `upstream` con más servidores).
- **Servidor cerca de la base de datos**: todo corre en el mismo host Docker, en la misma red interna.
- **Imágenes WebP/AVIF con carga diferida**: la aplicación casi no usa imágenes (solo íconos SVG y el favicon).
