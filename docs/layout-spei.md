# Layout de dispersión SPEI

Al cerrar un ciclo, la caja entrega al banco un archivo para depositar a la cuenta de cada ahorrador lo que le corresponde
(su ahorro liquidado más el rendimiento asignado, o solo el rendimiento). Se genera con
`POST /api/v1/ciclos/{id}/layout-spei` y cada generación queda en `layout_generado`.

## Qué se investigó y qué se pudo verificar

Se buscaron layouts públicos de dispersión de bancos mexicanos. **Resultado, con honestidad:**

| Banco | Qué hay público | Qué se verificó |
|---|---|---|
| **BBVA** (Net Cash) | Guía de pagos SPEI individuales ([PDF](https://www.bbva.mx/content/dam/public-web/mexico/images/digitalizacion-2020/pagos-spei-v2-05-2020.pdf)) y páginas de producto. **El layout del archivo masivo no es público**: lo entrega el banco a clientes con contrato. | CLABE de 18 dígitos, SPEI en moneda nacional entre cuentas de distintos bancos, horario de operación de 06:00 a 17:30. No hay posiciones ni longitudes del archivo. |
| **Banorte** (Banca Empresarial) | Layouts publicados: [pago de nómina](https://www.banorte.com/cms/empresas/tutoriales/pdf/Layout%20Pago%20de%20Nomina.pdf), [pagos](https://www.banorte.com/cms/empresas/tutoriales/pdf/bem-layouts/layout-pagos.pdf) y [pagos simplificado](https://www.banorte.com/cms/empresas/tutoriales/pdf/bem-layouts/layout-simplificado-pagos.pdf). | Del *simplificado* (texto delimitado por tabuladores): cuenta o CLABE destino como texto de 20, importe numérico de hasta 16 posiciones con punto decimal y a lo más dos decimales, referencia numérica de 10, descripción de 30; para conservar ceros a la izquierda se antepone apóstrofo (en Excel) y el archivo se guarda sin encabezado; el beneficiario debe existir antes en el catálogo de proveedores. Del de *nómina* (posición fija): importe de 15 posiciones (13 enteros y 2 decimales) y fecha de aplicación AAAAMMDD de 8. |
| Banamex | Banca Net permite definir el layout de carga de longitud fija o variable. | No se revisó el detalle. |

Los PDF de Banorte se leyeron solo por los extractos de las búsquedas (la descarga de los PDF completos agotó el tiempo
de espera), así que **el orden exacto de las columnas y las longitudes que no aparecen arriba no están verificados**.

## Qué se implementó (interfaz `ILayoutBancario`)

Cada layout recibe las dispersiones del ciclo (ahorrador, CLABE, importe) y devuelve el archivo. Se validan, para cada renglón:
CLABE de 18 dígitos con dígito verificador, importe mayor a cero con dos decimales, nombre sin caracteres no permitidos
(se normaliza a mayúsculas sin acentos y solo `A-Z 0-9 espacio . - &`; la coma se quita porque separa los campos del CSV) y que la suma del archivo sea la suma de los
renglones (suma de control). Un renglón inválido detiene la generación y se informa cuál es, no se omite en silencio.

### `GENERICO_CSV` (propio, no es de ningún banco)
UTF-8 sin BOM, coma, `CRLF`, con encabezado. Es la forma de revisar el archivo a simple vista y de cargarlo en otro sistema.

```
clabe,beneficiario,importe,concepto,referencia,fecha_aplicacion
012180001234567899,AXEL CARLIN GUZMAN,48650.00,AHORRO Y RENDIMIENTO 2024,2024000001,2025-01-02
```

| Campo | Regla |
|---|---|
| `clabe` | 18 dígitos con dígito verificador |
| `beneficiario` | Hasta 40 caracteres, normalizado |
| `importe` | Punto decimal, dos decimales, sin separador de miles |
| `concepto` | Hasta 30 caracteres: `AHORRO Y RENDIMIENTO AAAA` (o `RENDIMIENTO AAAA`) |
| `referencia` | Numérica de 10 dígitos: año y consecutivo de 6 dígitos del renglón (`AAAANNNNNN`) |
| `fecha_aplicacion` | `AAAA-MM-DD`: el siguiente día hábil (lunes a viernes, sin considerar festivos) a la generación del archivo, nunca antes del cierre del ciclo |

### `BANORTE_SIMPLIFICADO` (basado en el layout simplificado de Banorte)
Texto delimitado por tabuladores, sin encabezado, `CRLF`, una dispersión por renglón. **Qué es verificado y qué supuesto:**

| Columna | Contenido | Estado |
|---|---|---|
| 1. ID del beneficiario | Número de empleado (`GIM-00123`) | **Supuesto**: Banorte pide un ID de proveedor del catálogo, pero no se verificó su posición |
| 2. Cuenta/CLABE destino | CLABE de 18 dígitos completada con ceros a la izquierda hasta 20 | Longitud de 20 verificada; el relleno con ceros es supuesto |
| 3. Importe | Punto decimal y dos decimales (máximo 16 posiciones) | Verificado |
| 4. Referencia | Numérica de 10 dígitos (`AAAA` + consecutivo de 6) | Longitud verificada |
| 5. Descripción | Hasta 30 caracteres, normalizada | Longitud verificada |

Antes de usarlo con Banorte hay que confirmar con su ejecutivo el orden de las columnas y dar de alta a los beneficiarios
en el catálogo (su layout de alta de cuentas es otro archivo, que esta fase no genera).

## Qué se dispersa

Parámetro `incluirAhorro` (por omisión **verdadero**): el importe de cada ahorrador es lo que se le liquidó al cerrar el
ciclo más su rendimiento. Con `incluirAhorro=false` el archivo solo lleva el rendimiento asignado. Ver `docs/decisiones.md`
(el enunciado pide dispersar "ahorro más rendimientos"). Quien tiene préstamo vigente conserva parte de su ahorro como
garantía; esa parte no se dispersa.

## Estados

El archivo solo se genera de un ciclo `CERRADO`. Se puede volver a generar (queda cada generación en el historial con su
suma de control, que es la misma mientras no cambien los datos); un ciclo cerrado no cambia.
