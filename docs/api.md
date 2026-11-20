# API (v1)

Base: `/api/v1`. JSON en `camelCase`; los enums viajan como en la base de datos (`ACTIVO`, `AHORRO_ORDINARIO`).
Todas las rutas piden `Authorization: Bearer <token>` salvo `auth/login`, `auth/refresh`, `auth/logout` y `/health`.
Las consultas distribuidas (Parte 2) están en [consultas-parte2.md](consultas-parte2.md) y al final de este documento.

## Roles

| Política | Administrador | Capturista | Consulta |
|---|:-:|:-:|:-:|
| Lectura (consultar) | sí | sí | sí |
| Captura (altas, ediciones, cuotas, incidencias, importaciones, layouts, solicitudes de préstamo, voluntario) | sí | sí | no |
| Administración (cajas, empresas, usuarios, **aplicar periodo**, **autorizar y rechazar préstamos**, **cerrar ciclos y dispersar**, **editar tareas**) | sí | no | no |

Cada usuario ve solo las cajas asignadas (el Administrador ve todas). Una caja ajena pedida de forma explícita responde
403 `CAJA_SIN_ACCESO`; un ahorrador de otra caja responde 404.

## Sesión

| Método y ruta | Descripción |
|---|---|
| `POST /auth/login` `{ correo, password }` | Devuelve `{ accessToken, expiraEnSegundos, usuario }` y pone la cookie `caja_refresh`. Límite: 8 intentos por minuto por IP. |
| `POST /auth/refresh` | Sin cuerpo; usa la cookie. Devuelve una sesión nueva y rota la cookie. |
| `POST /auth/logout` | Revoca la cookie. Responde 204. |
| `GET /auth/me` | Usuario, rol y cajas accesibles. |

El token de acceso dura 15 minutos: la interfaz debe llamar a `refresh` al recibir un 401 (una sola vez por petición) y,
si también falla, volver al login.

## Recursos

| Método y ruta | Política | Descripción |
|---|---|---|
| `GET /cajas` | Lectura | Cajas accesibles con ahorradores activos y ahorro total. |
| `GET /cajas/{id}` | Lectura | Una caja. |
| `POST /cajas` | Administración | `{ empresaId` o `empresaNueva, nombre, prefijo, tasaInteresMensual, frecuenciaNomina, anioCiclo }`. Crea también los periodos del ciclo. |
| `PUT /cajas/{id}` | Administración | `{ nombre, tasaInteresMensual, activa }`. |
| `GET /cajas/{id}/periodos?anio=` | Lectura | Periodos de la caja con su estado. |
| `GET /empresas` | Administración | Catálogo para el alta de cajas. |
| `GET /ahorradores` | Lectura | Paginado. Filtros: `cajaId`, `estado`, `q` (nombre, apellidos o número). Orden: `orden` = `nombre` (defecto), `numEmpleado`, `ahorro`, `ingreso`; `desc`. |
| `GET /ahorradores/{id}` | Lectura | Datos, cuota vigente, ahorro (ordinario, voluntario, total, límite 80%, disponible sin aval) y préstamos vigentes. |
| `POST /ahorradores` | Captura | Alta con su primera cuota. Sin `numEmpleado` se asigna el siguiente de la caja. |
| `PUT /ahorradores/{id}` | Captura | Datos personales y de nómina. |
| `GET /ahorradores/{id}/movimientos` | Lectura | Paginado, del más reciente al más antiguo, con saldo de ahorro acumulado. Filtros: `tipo`, `desde`, `hasta`, `periodoId`. |
| `GET /ahorradores/{id}/estado-cuenta?anio=` | Lectura | Saldo inicial, movimientos del año, totales por concepto y saldo final. |
| `GET /ahorradores/{id}/cuotas` | Lectura | Historial de cuotas. |
| `POST /ahorradores/{id}/cuotas` | Captura | Cuota nueva (`vigenteDesde` dentro de un periodo abierto). |
| `GET /movimientos` | Lectura | Movimientos de las cajas accesibles. Filtros: `cajaId`, `ahorradorId`, `tipo`, `desde`, `hasta`, `periodoId`. |
| `GET /usuarios`, `GET /usuarios/{id}` | Administración | Usuarios con sus cajas. |
| `POST /usuarios` | Administración | `{ nombre, correo, password, rol, accesoTodasCajas, cajaIds }`. |
| `PUT /usuarios/{id}` | Administración | `{ nombre, rol, activo, accesoTodasCajas, cajaIds }`. |
| `POST /usuarios/{id}/password` | Administración | `{ password }`. Revoca las sesiones del usuario. |

## Nómina

Los descuentos de cada periodo salen de la vista `v_descuento_periodo` (ordinario + voluntario + cuota de préstamo, con el
voluntario siempre aparte). Las rutas de periodo reciben el `id` que devuelve `GET /cajas/{id}/periodos`; un periodo de una
caja ajena responde 404.

| Método y ruta | Política | Descripción |
|---|---|---|
| `GET /nomina/periodos/{periodoId}/descuentos` | Lectura | Resumen del periodo y renglones paginados por ahorrador (`q`, `suspendido`, `page`, `pageSize`), con sus incidencias. Sirve también para periodos ya aplicados. |
| `GET /nomina/periodos/{periodoId}/incidencias` | Lectura | Incidencias del periodo. |
| `POST /nomina/incidencias` | Captura | `{ ahorradorId, periodoId, tipo, valor?, nota?, fechaBaja? }`. Solo en periodos abiertos. |
| `DELETE /nomina/incidencias/{id}` | Captura | Solo `LICENCIA`, `INCAPACIDAD` y `SUSPENDE_VOLUNTARIO`, mientras el periodo esté abierto. |
| `POST /nomina/periodos/{periodoId}/aplicar` | Administración | Genera los movimientos del periodo y lo marca `APLICADO`. Una sola vez y en orden. |
| `POST /nomina/periodos/{periodoId}/layout?formato=` | Captura | Descarga el layout de incidencias para nómina. `formato`: `CSV` (defecto), `XLSX`, `TXT_PIPE` o `TXT_ANCHO`. Cabeceras `X-Registros`, `X-Importe-Total` y `X-Suma-Control`. Ver [layout-nomina.md](layout-nomina.md). |
| `GET /nomina/layouts` | Lectura | Historial de layouts generados (`cajaId`, `periodoId`). |

Tipos de incidencia: `LICENCIA` e `INCAPACIDAD` suspenden todo el descuento del periodo; `SUSPENDE_VOLUNTARIO` solo el
voluntario; `CAMBIO_CUOTA` (`valor` = nueva cuota ordinaria, rige desde el inicio del periodo); `BAJA` (`fechaBaja` dentro
del periodo, que debe ser el primero abierto de la caja) marca al ahorrador `BAJA` y liquida su ahorro con un movimiento
`LIQUIDACION_CICLO` referencia `BAJA`. No se da de baja a quien tiene un préstamo propio sin liquidar (`BAJA_CON_PRESTAMO`)
ni a quien avala uno (`BAJA_CON_AVAL`).

Aplicar un periodo (`AplicarPeriodoResultadoDto`) es una sola transacción: movimientos `AHORRO_ORDINARIO` y
`AHORRO_VOLUNTARIO` (referencia `Q20-2024`), `ABONO_CAPITAL` y `ABONO_INTERES` de las cuotas programadas (referencia = folio),
cuotas en `APLICADO`, préstamo en `LIQUIDADO` si era la última, y los ahorradores en licencia pasan a `LICENCIA` (los que
regresan, a `ACTIVO`). La cuota de préstamo de quien está suspendido no se cobra: esa cuota y las siguientes se recorren un
periodo. Una segunda llamada responde 409 `PERIODO_YA_APLICADO` sin duplicar nada.

## Importación de altas y bajas

Formato de archivo y reglas en [importacion.md](importacion.md). El flujo es en tres pasos: cargar (vista previa),
revisar los errores por renglón y aplicar.

| Método y ruta | Política | Descripción |
|---|---|---|
| `POST /importaciones` | Captura | `multipart/form-data` con `cajaId` y `archivo` (.xlsx, .xml, .txt o .csv, máximo 2 MB y 5,000 renglones). Devuelve la vista previa (201). No cambia el padrón. |
| `GET /importaciones` | Lectura | Historial paginado (`cajaId`, `estado`) con el resumen de cada lote. |
| `GET /importaciones/{id}` | Lectura | Lote con sus renglones paginados (`validacion`, `tipo`). Cada renglón trae sus errores como `{ campo, codigo }`. |
| `POST /importaciones/{id}/aplicar?omitirErrores=` | Captura | Aplica el lote en una sola transacción. Con renglones con error responde 409 `IMPORTACION_CON_ERRORES`, salvo `omitirErrores=true`. |
| `POST /importaciones/{id}/cancelar` | Captura | Descarta una vista previa (204). |

## Préstamos

| Método y ruta | Política | Descripción |
|---|---|---|
| `POST /prestamos/simular` | Lectura | `{ ahorradorId, monto, plazoQuincenas, avalAhorradorId? }`. Evalúa la garantía y devuelve la tabla de amortización con fechas; no guarda nada. |
| `POST /prestamos` | Captura | Crea la solicitud (`PENDIENTE`) con folio `FOL-AAAA-NNN` y la tabla proyectada. |
| `GET /prestamos` | Lectura | Paginado. Filtros: `cajaId`, `ahorradorId`, `estado`, `q` (folio, número o nombre). Orden: `orden` = `folio` (defecto), `monto`, `saldo`, `solicitud`; `desc`. |
| `GET /prestamos/resumen?cajaId=` | Lectura | Cartera: vigentes, pendientes, liquidados, rechazados, capital colocado, saldo vigente e interés cobrado. |
| `GET /prestamos/{id}` | Lectura | Préstamo, aval, avance y tabla de amortización con su estado; `cuotaActual` es la próxima por cobrar. |
| `POST /prestamos/{id}/autorizar` | Administración | Pasa a `VIGENTE`: revalida la garantía, registra el desembolso y programa las cuotas. |
| `POST /prestamos/{id}/rechazar` | Administración | Pasa a `RECHAZADO` y quita la tabla proyectada. |

Garantía: hasta el 80% del ahorro (menos lo que ya se debe) basta con el ahorro; si el monto lo excede se pide un aval, otro
ahorrador activo de la misma caja cuyo disponible cubra el excedente, descontando lo que ya respalda en otros préstamos
pendientes o vigentes. El plazo va de 1 a 36 cuotas. El desembolso se registra en la fecha de pago del primer periodo
abierto y la primera cuota cae en el siguiente.

## Ahorro voluntario

| Método y ruta | Política | Descripción |
|---|---|---|
| `GET /ahorro-voluntario` | Lectura | Ahorradores no dados de baja con su cuota voluntaria vigente y lo acumulado. Filtros: `cajaId`, `q`, `activo`. |
| `GET /ahorro-voluntario/resumen?cajaId=` | Lectura | Ahorradores con voluntario, cuota total por periodo y acumulado. |
| `PUT /ahorro-voluntario/{ahorradorId}` | Captura | `{ activo, cuotaVoluntaria, vigenteDesde? }`. Crea una cuota nueva (conserva la ordinaria); sin fecha rige desde el primer periodo abierto. Solo aportaciones: no hay retiros. |

## Ciclos (cierre y distribución)

El ciclo es el año natural de una caja. El cierre tiene tres pasos: interés generado, distribución proporcional y archivo de
dispersión. Hasta que se cierra, el cálculo es un borrador que se puede repetir. Detalle de las reglas en `docs/decisiones.md`.

| Método y ruta | Política | Descripción |
|---|---|---|
| `GET /ciclos?cajaId=` | Lectura | Ciclos de las cajas accesibles con estado, interés y participantes. |
| `GET /ciclos/{id}` | Lectura | Resumen: interés cobrado y devengado, periodos aplicados, préstamos vigentes, lo distribuido, ahorro liquidado y retenido, y si se puede cerrar (`puedeCerrar`, `impedimentos`). |
| `GET /ciclos/{id}/distribucion` | Lectura | Paginada (`q`): saldo-días, saldo promedio, días activos, participación, interés asignado, ahorro liquidado y retenido. |
| `POST /ciclos/borrador` | Administración | `{ cajaId, anio }`. Crea el ciclo si no existe, calcula la distribución provisional (estado `EN_CIERRE`) y la recalcula las veces que haga falta. 409 `CICLO_YA_CERRADO` si ya está cerrado. |
| `POST /ciclos/{id}/cerrar` | Administración | **202** con el id de un trabajo en segundo plano (ver Trabajos). Falla con `CICLO_PERIODOS_ABIERTOS` si faltan periodos por aplicar y con `CICLO_YA_CERRADO` si ya está cerrado. |
| `POST /ciclos/{id}/layout-spei?formato=&incluirAhorro=` | Administración | Descarga la dispersión de un ciclo cerrado. `formato`: `GENERICO_CSV` (defecto) o `BANORTE_SIMPLIFICADO`; `incluirAhorro` (defecto `true`) o solo el rendimiento. Cabeceras `X-Registros`, `X-Importe-Total`, `X-Suma-Control`. Ver [layout-spei.md](layout-spei.md). |
| `GET /ciclos/{id}/layouts` | Lectura | Historial de archivos de dispersión del ciclo. |

Cerrar el ciclo, en una transacción: recalcula la distribución, genera `INTERES_ANUAL` por el rendimiento de cada ahorrador y
`LIQUIDACION_CICLO` por su ahorro (referencia `CIERRE-AAAA`, fecha 31-dic), devuelve lo retenido como aportación inicial del
ciclo siguiente (`SALDO-INICIAL-AAAA`, 1-ene), pasa los periodos del año a `CERRADO` y abre el ciclo del año siguiente.

## Reportes

JSON, PDF y Excel con `formato=JSON|PDF|XLSX` (JSON por omisión). Los permisos son los de Lectura y el alcance por caja se
respeta igual que en el resto de la API.

| Método y ruta | Descripción |
|---|---|
| `GET /reportes/estado-cuenta/{ahorradorId}?anio=&formato=` | Estado de cuenta de un ahorrador. En JSON es la respuesta de `/ahorradores/{id}/estado-cuenta`. |
| `GET /reportes/estado-resultados?cajaId=&anio=&formato=` | Intereses cobrados y devengados por mes, utilidad a distribuir, interés ya distribuido y cartera vigente de una caja. |
| `GET /reportes/distribucion?cicloId=&formato=` | Distribución de intereses de un ciclo (en JSON, paginada; PDF y Excel traen todos los renglones). |
| `POST /reportes/trabajos` | `{ tipo: ESTADO_CUENTA\|ESTADO_RESULTADOS\|DISTRIBUCION, formato, ahorradorId?, cajaId?, cicloId?, anio? }`. Genera el reporte en segundo plano (**202**). |

## Panel

`GET /dashboard?cajaId=` (Lectura): fondo total, ahorradores activos, préstamos vigentes y su saldo, ciclo en curso, interés
cobrado en el ciclo, serie de 12 meses (ahorro aportado, préstamos desembolsados y capital cobrado) y los 8 últimos movimientos.
Sin `cajaId` suma las cajas del usuario. Se guarda en Redis y se refresca cuando cambian los datos de la caja.

## Tareas programadas y trabajos

| Método y ruta | Política | Descripción |
|---|---|---|
| `GET /tareas`, `GET /tareas/{id}` | Lectura | Tareas con frecuencia, hora, interruptor, última y próxima ejecución y el resultado de la última. |
| `PUT /tareas/{id}` | Administración | `{ frecuencia: DIARIA\|QUINCENAL\|MENSUAL, hora, activa }`. Se reprograma al instante, sin reiniciar. La hora es la de la Ciudad de México. |
| `POST /tareas/{id}/ejecutar` | Administración | Ejecuta la tarea ya, en segundo plano (**202**). |
| `GET /tareas/{id}/historial` | Lectura | Ejecuciones (inicio, fin, estado, resumen), de la más reciente a la más antigua. |
| `GET /trabajos/{id}` | Lectura | Estado de un trabajo en segundo plano: `PENDIENTE`, `EN_CURSO`, `EXITO` o `ERROR` (con `codigoError` y `mensajeError`), su `resultado` y el nombre del archivo. Solo lo ve quien lo pidió o un Administrador. |
| `GET /trabajos/{id}/archivo` | Lectura | Descarga el archivo de un trabajo terminado. |

Los trabajos (cierre de ciclo, reportes, ejecución de tareas) corren en una cola en segundo plano con el usuario que los pidió;
su estado y archivo se conservan 24 horas en Redis. La frecuencia `QUINCENAL` corre los días 1 y 16 y la `MENSUAL` el día 1.

Paginación: `page` (desde 1) y `pageSize` (25 por defecto, máximo 100). La respuesta trae `items`, `page`, `pageSize`,
`total` y `totalPages`. Los movimientos son de solo lectura: los generan la nómina, los préstamos y el cierre de ciclo.

## Errores

Respuesta `application/problem+json` con `status`, `code` (estable, para traducir en la interfaz), `detail` (texto de
referencia en español) y `traceId`. Si la validación falla, `errors` trae los códigos por campo.

```json
{ "status": 400, "code": "VALIDACION", "errors": { "rfc": ["RFC_INVALIDO"], "clabe": ["CLABE_INVALIDA"] } }
```

| Estado | Códigos |
|---|---|
| 400 | `VALIDACION` (con `errors`: `REQUERIDO`, `LONGITUD_MAXIMA`, `VALOR_INVALIDO`, `RFC_INVALIDO`, `CLABE_INVALIDA`, `NUM_EMPLEADO_INVALIDO`, `NUM_EMPLEADO_PREFIJO`, `MONTO_NEGATIVO`, `MONTO_INVALIDO`, `VOLUNTARIO_INACTIVO`, `FECHA_INVALIDA`, `ANIO_INVALIDO`, `TASA_FUERA_DE_RANGO`, `PREFIJO_INVALIDO`, `EMPRESA_REQUERIDA`, `PASSWORD_DEBIL`, `CORREO_INVALIDO`, `USUARIO_SIN_CAJAS`, `PLAZO_INVALIDO`, `BOOLEANO_INVALIDO`), `CUOTA_FECHA_INVALIDA`, `BAJA_FECHA_INVALIDA`, `ARCHIVO_INVALIDO` (con `COLUMNA_FALTANTE:<columna>` en `errors.columnas`), `ARCHIVO_SIN_FILAS`, `ARCHIVO_DEMASIADAS_FILAS`, `SPEI_RENGLONES_INVALIDOS` (con `errors.renglones`: `EMPLEADO:CODIGO`) |
| 401 | `NO_AUTENTICADO`, `CREDENCIALES_INVALIDAS`, `SESION_INVALIDA` |
| 403 | `SIN_PERMISO` (rol), `CAJA_SIN_ACCESO`, `CONSULTA_REQUIERE_TODAS_LAS_CAJAS` |
| 404 | `AHORRADOR_NO_ENCONTRADO`, `CAJA_NO_ENCONTRADA`, `EMPRESA_NO_ENCONTRADA`, `USUARIO_NO_ENCONTRADO`, `PERIODO_NO_ENCONTRADO`, `INCIDENCIA_NO_ENCONTRADA`, `IMPORTACION_NO_ENCONTRADA`, `PRESTAMO_NO_ENCONTRADO`, `CICLO_NO_ENCONTRADO`, `TAREA_NO_ENCONTRADA`, `TRABAJO_NO_ENCONTRADO`, `CONSULTA_NO_ENCONTRADA` |
| 409 | `NUM_EMPLEADO_DUPLICADO`, `CAJA_DUPLICADA`, `CAJA_INACTIVA`, `AHORRADOR_EN_BAJA`, `CUOTA_FECHA_DUPLICADA`, `CORREO_DUPLICADO`, `AUTO_MODIFICACION`, `PERIODO_NO_ABIERTO`, `PERIODO_YA_APLICADO`, `PERIODO_NO_SIGUIENTE`, `SIN_PERIODO_ABIERTO`, `INCIDENCIA_DUPLICADA`, `INCIDENCIA_NO_ELIMINABLE`, `BAJA_CON_PRESTAMO`, `BAJA_CON_AVAL`, `LAYOUT_SIN_REGISTROS`, `IMPORTACION_YA_PROCESADA`, `IMPORTACION_CON_ERRORES`, `PRESTAMO_NO_PENDIENTE`, `PRESTAMO_PENDIENTE_EXISTENTE`, `AHORRADOR_NO_ACTIVO`, `AVAL_REQUERIDO`, `AVAL_INSUFICIENTE`, `AVAL_INVALIDO`, `CICLO_YA_CERRADO`, `CICLO_NO_CERRADO`, `CICLO_PERIODOS_ABIERTOS`, `SPEI_SIN_DISPERSIONES`, `TRABAJO_NO_TERMINADO` |
| 413 | `ARCHIVO_DEMASIADO_GRANDE` |
| 429 | `DEMASIADOS_INTENTOS` |
| 502 | `CONSULTA_FEDERADA_FALLO` (Trino rechazó la consulta; el detalle trae su mensaje) |
| 503 | `FEDERACION_NO_DISPONIBLE` (Trino no responde o la consulta pasó el tiempo límite) |
| 500 | `ERROR_INTERNO` |

## Consultas distribuidas (Parte 2)

Requieren la política de Lectura **y acceso a todas las cajas** (cada consulta abarca los tres motores): un usuario que solo
ve algunas recibe 403 `CONSULTA_REQUIERE_TODAS_LAS_CAJAS`. Necesitan la federación encendida (`scripts/federacion.ps1 iniciar`);
sin ella responden 503 `FEDERACION_NO_DISPONIBLE`.

| Ruta | Descripción |
|---|---|
| `GET /consultas-distribuidas` | Estado: `disponible`, el `motivo` si no lo está, las consultas y, por motor, las cajas que viven en él y cuántos ahorradores y movimientos tiene (conteos reales). |
| `GET /consultas-distribuidas/{q}?anio=&page=&pageSize=` | Una página del resultado de Q1 a Q5. Q4 y Q5 usan `anio` (si falta, el más reciente con ciclo; fuera de 2000-2100 es 400). Devuelve `sql` (lo ejecutado en Trino, con los parámetros sustituidos), `columnas` (nombre y tipo de Trino), `filas`, `total` (de un `count(*)` real), `page`, `pageSize` y `duracionMs` (medida). |
| `GET /consultas-distribuidas/{q}/csv?anio=` | El resultado completo como CSV (UTF-8 con BOM; un texto que empieza con `=`, `+`, `-` o `@` lleva una comilla simple para que Excel no lo evalúe). |
| `GET /consultas-distribuidas/{q}/equivalencia?anio=` | Ejecuta también la consulta directa sobre MariaDB y compara fila por fila: `coincide`, `filasFederadas`, `filasDirectas`, `diferencias`, un `ejemplo` si las hay, `sqlDirecto` y los tiempos de cada una. |
