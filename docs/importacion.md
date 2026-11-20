# Importación de altas y bajas

Permite dar de alta o de baja ahorradores por archivo. Es un proceso en tres pasos: **cargar** (se valida y se guarda como
vista previa, sin tocar el padrón), **revisar** los errores por renglón y **aplicar** (una sola transacción).

## Formatos admitidos

| Extensión | Formato |
|---|---|
| `.xlsx` | Primera hoja visible; el primer renglón es el encabezado. Las celdas de fecha y de número se leen sin depender del formato regional. |
| `.xml` | Raíz con un elemento por renglón; las columnas pueden ir como atributos o como elementos hijos (`numEmpleado` equivale a `num_empleado`). Se rechazan las definiciones de tipo (DTD). |
| `.txt` | Texto delimitado por `|` o por coma; se detecta con el encabezado. UTF-8 (con o sin BOM) o Latin-1. |
| `.csv` | Igual que `.txt`, con coma; admite comillas dobles. |

Límites: 2 MB por archivo, 5,000 renglones y 300 caracteres por valor. Un archivo mayor responde 413; una extensión
distinta, un `.xlsx` que no es un libro válido o un XML mal formado responden 400 `ARCHIVO_INVALIDO`.

## Columnas

Los nombres se normalizan (sin acentos, minúsculas, espacios y guiones como `_`), así que `NumEmpleado`, `Num Empleado` y
`num_empleado` son lo mismo. `tipo` es sinónimo de `movimiento`. Las columnas desconocidas se ignoran.

| Columna | Alta | Baja | Regla |
|---|:-:|:-:|---|
| `movimiento` | sí | sí | `ALTA` o `BAJA` |
| `num_empleado` | sí | sí | `PREFIJO-NNNNN` con el prefijo de la caja (por ejemplo `GIM-00123`) |
| `nombre`, `apellidos`, `departamento` | sí | | Obligatorios, hasta 100 caracteres |
| `rfc` | sí | | RFC de persona física (4 letras, fecha AAMMDD y homoclave) |
| `fecha_nacimiento` | sí | | `AAAA-MM-DD` o `DD/MM/AAAA`; anterior a hoy y no antes de 1900 |
| `fecha_ingreso` | sí | | Posterior al nacimiento |
| `sueldo_quincenal` | sí | | Importe con a lo más dos decimales |
| `clabe` | sí | | 18 dígitos con dígito verificador válido |
| `cuota_ordinaria` | sí | | Importe, cero o más |
| `cuota_voluntaria` | | | Opcional; si trae importe, el voluntario queda activo |
| `voluntario_activo` | | | Opcional: `SI`/`NO`, `1`/`0`, `true`/`false`. Inactivo con importe es error |
| `fecha_baja` | | sí | Fecha de la baja |

Los importes aceptan `1400`, `1,400.00` y `$1400.00`.

## Validación

Cada renglón se valida solo y contra el padrón de la caja. Los errores se devuelven como `{ campo, codigo }` y la interfaz
los traduce:

| Código | Significa |
|---|---|
| `REQUERIDO`, `LONGITUD_MAXIMA` | Falta el dato o es muy largo |
| `MOVIMIENTO_INVALIDO` | `movimiento` no es `ALTA` ni `BAJA` |
| `NUM_EMPLEADO_INVALIDO`, `NUM_EMPLEADO_PREFIJO` | Formato incorrecto o prefijo de otra caja |
| `RFC_INVALIDO`, `CLABE_INVALIDA`, `FECHA_INVALIDA` | Dato mal formado |
| `MONTO_INVALIDO`, `MONTO_NEGATIVO` | Importe no numérico, con más de dos decimales o negativo |
| `BOOLEANO_INVALIDO`, `VOLUNTARIO_INACTIVO` | `voluntario_activo` ilegible o contradictorio |
| `DUPLICADO_EN_ARCHIVO` | El mismo número aparece en más de un renglón (cuenta el primero) |
| `NUM_EMPLEADO_DUPLICADO` | El número ya existe en la caja con otra persona (otro RFC) o dado de baja |
| `EMPLEADO_INEXISTENTE` | Baja de un número que no está en la caja |
| `BAJA_FECHA_INVALIDA` | La fecha de baja no cae dentro del primer periodo abierto de la caja o es anterior al ingreso |
| `BAJA_CON_PRESTAMO`, `BAJA_CON_AVAL` | Tiene un préstamo sin liquidar o avala uno |
| `SIN_PERIODO_ABIERTO` | La caja no tiene un periodo abierto |

Cada renglón queda clasificado como `ALTA`, `BAJA` o `SIN_CAMBIO`. Volver a cargar un alta idéntica (mismo número y RFC) o una
baja ya registrada es `SIN_CAMBIO`, no error: cargar dos veces el mismo archivo es inofensivo.

## Qué hace aplicar

- **Alta**: crea al ahorrador activo con su primera cuota. La cuota rige desde el ingreso o, si es anterior, desde el primer
  periodo abierto (en los periodos ya aplicados no hubo descuentos).
- **Baja**: marca al ahorrador `BAJA` con la fecha del archivo y liquida su ahorro con un movimiento `LIQUIDACION_CICLO`
  (referencia `BAJA`); deja de descontarse desde ese periodo.

Aplicar vuelve a validar contra el padrón de ese momento, porque pudo cambiar desde la vista previa. Con renglones con error
responde 409 `IMPORTACION_CON_ERRORES` y no cambia nada; con `omitirErrores=true` aplica solo los válidos (todos en una
misma transacción). Un lote se aplica una sola vez (409 `IMPORTACION_YA_PROCESADA` después), aunque lleguen peticiones
simultáneas; también puede cancelarse mientras sea vista previa.

## Ejemplo (`.txt` con pipes)

```
movimiento|num_empleado|nombre|apellidos|rfc|fecha_nacimiento|fecha_ingreso|sueldo_quincenal|clabe|departamento|cuota_ordinaria|cuota_voluntaria|voluntario_activo|fecha_baja
ALTA|GIM-09901|Julia|Pérez|PEMJ900517AB1|1990-05-17|2024-10-01|9000.00|012180001234567899|Almacén|450.00|||
BAJA|GIM-00130||||||||||||2024-11-05
```
