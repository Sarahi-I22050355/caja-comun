# Pruebas manuales guiadas

Recorrido de las 15 pantallas en español e inglés, con sus estados de carga, vacío y error. Requisitos: pila levantada (`scripts/up`), datos de
demostración (`scripts/seed`) y, para la pantalla 14, la federación (`scripts/federacion iniciar`). Usuarios en el README; las contraseñas las imprime el seed
o están en `SEED_PASSWORD_*` de `.env`.

## Cómo provocar cada estado

| Estado | Cómo |
|---|---|
| Carga (esqueleto) | Recargar la pantalla; con las herramientas del navegador, limitar la red a "Slow 4G" para verlo más tiempo |
| Vacío | Un filtro sin coincidencias (por ejemplo buscar `zzzz` en Ahorradores); un año sin ciclo en Reportes |
| Error con reintento | `docker compose --env-file .env -f infra/docker-compose.yml stop api`, recargar la pantalla (aparece el error con **Reintentar**), `start api` y pulsar **Reintentar** |

## Antes de empezar

1. Entrar como **Administrador**: el correo y la contraseña erróneos muestran un mensaje traducido, sin revelar cuál de los dos falló.
2. Cambiar ES ↔ EN en el encabezado en cada pantalla: el texto se traduce, **lo escrito en formularios, los filtros, el paso del cierre y el archivo elegido se conservan**,
   y los datos (nombres, folios, importes) no cambian.
3. Estrechar la ventana a 375 px: ninguna pantalla debe tener desplazamiento horizontal de la página (las tablas se desplazan dentro de su tarjeta).

## Pantallas

| # | Ruta | Qué comprobar |
|---|---|---|
| 1 | `/login` | Mostrar/ocultar contraseña; error de credenciales; tras entrar vuelve a la pantalla pedida |
| 2 | `/` Inicio | Cuatro indicadores, gráfica de ahorro contra préstamos, resumen del último periodo aplicado y últimos movimientos; cambiar de caja en el encabezado actualiza todo |
| 3 | `/cajas` | Tarjetas con tasa, ahorro y calendario; **Nueva caja** y **Editar** (solo Administrador) con validación de campos |
| 4 | `/ahorradores` | Búsqueda (con retraso), filtro por estado, orden, paginación; **Alta de ahorrador** con validación de RFC y CLABE |
| 5 | `/ahorradores/:id` | Ahorro ordinario, voluntario y total, límite del 80 %, cuota (cambiarla), estado de cuenta en PDF y Excel, movimientos paginados |
| 6 | `/sincronizacion` | Descargar la plantilla CSV; subir un `.xlsx`, `.xml`, `.txt` o `.csv`; vista previa con errores por renglón; aplicar o aplicar omitiendo errores; historial |
| 7 | `/nomina` | Descuentos del periodo (ordinario, voluntario, préstamo), incidencias (alta y baja), layout en los cuatro formatos, **Aplicar periodo** (solo Administrador; no se puede aplicar dos veces) |
| 8 | `/ahorro-voluntario` | Activar, cambiar y apagar el voluntario de un ahorrador; indicadores arriba |
| 9 | `/prestamos` | Cartera, filtro por estado, autorizar o rechazar (Administrador) con confirmación |
| 10 | `/prestamos/nuevo` y `/prestamos/:id` | Simulador en vivo (amortización y garantía del 80 %), aval cuando excede el ahorro, detalle con tabla de amortización |
| 11 | `/cierre-ciclo` | Tres pasos: interés generado, distribución (mayor residuo) y layout SPEI en los dos formatos; el cierre corre en segundo plano y no se repite |
| 12 | `/reportes` | Estado de cuenta, estado de resultados y distribución; cada uno en JSON en pantalla y PDF / Excel descargable |
| 13 | `/tareas` | Interruptor (se refleja al instante), frecuencia y hora, **Ejecutar ahora**, historial paginado |
| 14 | `/consultas` | Las cinco consultas, el SQL de Trino, resultados paginados con el total, CSV y **Comparar con MariaDB**; sin la federación explica cómo encenderla |
| 15 | `/usuarios` | Crear y editar usuarios (rol y cajas), restablecer contraseña; solo Administrador |

## Por rol

- **Capturista** (Mariana, GIMSA): no ve "Usuarios"; en Tareas no puede editar; no ve **Aplicar periodo**, **Calcular distribución** ni autorizar préstamos. Las consultas distribuidas le responden "Sin acceso" (solo tiene una caja).
- **Consulta** (Luis, Aceros): solo lectura; ninguna pantalla muestra botones de captura.
- **Administrador**: todo, y ve todas las cajas.

## Accesibilidad básica

- Todo se maneja con teclado: **Tab** recorre los controles en orden con un anillo de foco visible; **Esc** cierra los modales y el foco queda dentro mientras están abiertos.
- Al abrir la página aparece, con **Tab**, el enlace "Saltar al contenido".
- Las tablas llevan encabezados, los campos tienen etiqueta, y los estados de carga y error se anuncian (`role="status"` / `role="alert"`).
- Revisión automática hecha: una `h1` por pantalla, ningún botón ni enlace sin nombre, ningún campo sin etiqueta, sin `id` repetidos.
- Contraste: el texto blanco sobre el botón rosa y el texto rosa sobre blanco usan tonos de al menos 4.5:1.
