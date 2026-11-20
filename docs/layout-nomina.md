# Layout de incidencias para nómina

Archivo que la caja entrega al área de nómina de la empresa con lo que debe descontarse en un periodo. Se genera con
`POST /api/v1/nomina/periodos/{periodoId}/layout?formato=...` y cada descarga queda en el historial
(`layout_generado`: formato, nombre, renglones, importe, suma de control, usuario y fecha).

> **Qué es oficial y qué es supuesto.** Los sistemas de nómina de cada empresa definen sus propios formatos de carga y
> el enunciado no fija uno. Este layout es **un formato propio de la caja** que cumple lo pedido (un renglón por empleado y
> concepto, voluntario aparte, totales al final). Si la nómina de una empresa exige otro orden de columnas o anchos,
> se ajusta `Infrastructure/Archivos/LayoutNomina.cs`; las reglas de qué se descuenta no cambian.

## Contenido

Un renglón por empleado y concepto, ordenado por número de empleado y, dentro de cada uno, ordinario, voluntario y préstamo.
No se escriben renglones en cero.

| Concepto | Clave | Importe |
|---|---|---|
| Ahorro ordinario | `AHO-ORD` | Cuota ordinaria vigente del periodo |
| Ahorro voluntario | `AHO-VOL` | Cuota voluntaria, solo si está activa; siempre en su propio renglón |
| Préstamo | `PREST` | Capital + interés de la cuota programada en el periodo |

Quedan fuera quienes tienen una incidencia que suspende el descuento en el periodo (`LICENCIA`, `INCAPACIDAD`, `BAJA`).
Con `SUSPENDE_VOLUNTARIO` solo falta el renglón `AHO-VOL`. Es la misma regla de la vista `v_descuento_periodo`, de modo que
lo que se envía a nómina es lo que `aplicar` registra después.

El periodo se escribe como `Q20-2024` (quincenal) o `S12-2024` (semanal) y el tipo de periodo como `QUINCENAL` o `SEMANAL`.
Los importes llevan punto decimal y dos decimales, sin separador de miles y sin símbolo de moneda, sin importar la
configuración regional del equipo.

## Formatos

### `CSV`
UTF-8 sin marca BOM, separador coma y fin de línea `CRLF`. Encabezado, renglones y un renglón de cierre `TOTAL` cuya segunda
columna es el número de renglones y la tercera el importe total.

```
num_empleado,clave_concepto,importe,periodo,tipo_periodo
GIM-00123,AHO-ORD,1400.00,Q21-2024,QUINCENAL
GIM-00123,AHO-VOL,500.00,Q21-2024,QUINCENAL
GIM-00123,PREST,1362.50,Q21-2024,QUINCENAL
TOTAL,3,3262.50,Q21-2024,QUINCENAL
```

### `XLSX`
Hoja `Incidencias` con las mismas columnas del CSV, importes numéricos con formato `0.00` y el renglón `TOTAL` al final.

### `TXT_PIPE`
Texto delimitado por `|`, UTF-8 sin BOM, `CRLF`. Registro `H` de encabezado, `D` por renglón y `T` de cierre con el conteo y el
importe total.

```
H|NOMINA|GIM|Q21-2024|QUINCENAL
D|GIM-00123|AHO-ORD|1400.00|Q21-2024|QUINCENAL
D|GIM-00123|AHO-VOL|500.00|Q21-2024|QUINCENAL
D|GIM-00123|PREST|1362.50|Q21-2024|QUINCENAL
T|3|3262.50
```

### `TXT_ANCHO`
Ancho fijo de 44 posiciones, `CRLF`. Los campos de texto se alinean a la izquierda con espacios; el importe va en centavos,
sin punto, con ceros a la izquierda.

| Posiciones | Largo | Campo |
|---|---|---|
| 1 | 1 | Tipo de registro: `H`, `D` o `T` |
| 2–13 | 12 | Número de empleado (en `H`, `NOMINA`; en `T`, el número de renglones con ceros a la izquierda) |
| 14–21 | 8 | Clave de concepto (en blanco en `H` y `T`) |
| 22–33 | 12 | Importe en centavos (en `T`, el importe total; en `H`, ceros) |
| 34–43 | 10 | Periodo, por ejemplo `Q21-2024` |
| 44 | 1 | Tipo de periodo: `Q` o `S` |

```
HNOMINA              000000000000Q21-2024  Q
DGIM-00123   AHO-ORD 000000140000Q21-2024  Q
T00000003            000000326250Q21-2024  Q
```

## Suma de control

Cada generación calcula una suma de control: SHA-256 (en hexadecimal) de los renglones en su forma canónica
`empleado|concepto|importe|periodo|tipo`, en el orden del archivo, separados por salto de línea. Es **la misma en todos los
formatos** del mismo periodo y contenido, así que sirve para comprobar que un CSV, un TXT y un XLSX corresponden a los mismos
datos. Viaja en la cabecera `X-Suma-Control` y se guarda en `layout_generado.suma_control`. Cambia con cualquier cambio de
importe, de orden o de periodo.

## Nombre del archivo

`nomina_{prefijo de la caja}_{periodo}.{extensión}`, por ejemplo `nomina_GIM_Q21_2024.csv`.

## Cuándo se puede generar

Cualquier periodo con descuentos (abierto o ya aplicado). Si todos los empleados están suspendidos, responde 409
`LAYOUT_SIN_REGISTROS` en lugar de entregar un archivo vacío.
