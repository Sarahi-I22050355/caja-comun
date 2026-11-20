# Seguridad: revisión y controles

Revisión hecha en la Fase 8 sobre el código, la configuración y la aplicación en ejecución. Lo que sigue dice qué hay, dónde y cómo se comprobó.

## Autenticación y sesión

| Control | Dónde | Comprobación |
|---|---|---|
| Contraseñas con BCrypt (costo 11; 4 solo en pruebas) y verificación de tiempo parejo aunque el correo no exista | `AuthService`, `UsuarioService` | Pruebas unitarias |
| JWT de acceso corto, firmado HS256, con emisor, audiencia y caducidad validados; el algoritmo se fija (sin `alg: none`) | `ConfiguracionSeguridad` | Pruebas de `TokenService` |
| Secreto JWT de al menos 32 caracteres, solo en `.env` | `OpcionesJwt` | `scripts/up` lo genera |
| Refresh token en cookie `HttpOnly`, `SameSite=Lax`, ruta limitada a `/api/v1/auth` y `Secure` cuando la petición original es HTTPS; se guarda solo su hash y se rota en cada uso | `AuthController`, `AuthService` | Pruebas de integración |
| El token de acceso vive solo en memoria del navegador (nunca en `localStorage`) | `web/src/lib/api.ts` | Revisión de código |
| Límite de 8 intentos de inicio de sesión por minuto por IP | `ConfiguracionSeguridad` (rate limiting) | Se vio el 429 en el recorrido |
| Un usuario desactivado o al que se le quita una caja pierde el acceso al instante (se consulta la base, no el token) | `CajaAcceso` | Pruebas de integración |

## Autorización

- Todo exige sesión salvo login, refresh, logout, `/health` y el reporte de web vitals (política de reserva `RequireAuthenticatedUser`).
- Tres políticas (Lectura, Captura, Administración) salen de una sola matriz rol → acción en `Domain.Permisos`; hay pruebas por ruta.
- Alcance por caja: un usuario solo ve las cajas asignadas. Pedir una caja ajena responde 403; un recurso ajeno por id responde 404 (no confirma que exista).
- Las consultas distribuidas abarcan todas las cajas y solo las ve quien tiene acceso a todas (403 `CONSULTA_REQUIERE_TODAS_LAS_CAJAS`).
- La interfaz oculta lo que el rol no puede hacer, pero la autoridad es siempre la API.

## Datos y entradas

- **Consultas parametrizadas** (EF Core y Dapper con parámetros). En la Parte 2 el SQL de Trino solo recibe enteros y una fecha generados por el servidor; la lista de métricas, rutas y calificaciones del reporte de web vitals es cerrada.
- Validación con FluentValidation en cada petición; los errores salen como códigos estables, sin trazas ni mensajes internos.
- Archivos subidos: tope de 2 MB y 5,000 renglones, extensión validada y lectura sin ejecutar nada; el límite de cuerpo de nginx es 3 MB.
- CSV exportados: un texto que empieza con `=`, `+`, `-` o `@` lleva una comilla simple para que Excel no lo evalúe como fórmula.
- Sin registro de contraseñas, tokens ni datos personales en el log (el log de solicitudes no incluye cuerpos).

## Red y cabeceras

- **CORS cerrado**: la API no habilita ningún origen; la interfaz y la API comparten origen (nginx).
- nginx añade a lo que sirve: `Content-Security-Policy` (solo recursos del mismo origen, sin scripts en línea, `frame-ancestors 'none'`, `object-src 'none'`),
  `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, `Cross-Origin-Opener-Policy` y HSTS cuando la petición llegó por HTTPS.
  La API añade `nosniff`, `X-Frame-Options`, `Referrer-Policy` y `Cache-Control: no-store` en todo `/api`.
- `server_tokens off` en nginx; Swagger no se expone.
- **IP real del cliente**: nginx *sobrescribe* `X-Forwarded-For` con `CF-Connecting-IP` (o la IP del socket en local), así no se puede falsear para saltarse el límite de intentos.
  Esto depende de que en producción nginx solo sea alcanzable por el túnel (`docker-compose.prod.yml` lo deja en `127.0.0.1`).
- **Puertos**: MariaDB y Trino solo en `127.0.0.1`; PostgreSQL y ClickHouse no publican puertos; en producción MariaDB deja de publicarlo.
- La API corre sin root (`USER $APP_UID`). Trino no tiene autenticación: solo escucha en la red interna y en `127.0.0.1`.
- Federación: Trino entra a MariaDB con un usuario de **solo lectura**, y a PostgreSQL y ClickHouse no se copian RFC, CLABE ni sueldos.

## Dependencias y secretos

- `dotnet list package --vulnerable --include-transitive`: sin vulnerabilidades conocidas en los cinco proyectos.
- `npm audit --omit=dev`: 0 vulnerabilidades (se subió `react-router-dom` a 7 por avisos de la 6.x sin parche).
- Imágenes de Trino y ClickHouse fijadas por huella; `.env` y los respaldos están en `.gitignore`; el repositorio solo trae `.env.example` sin valores.
- Sin scripts de terceros en la interfaz: lo impide la política de contenido.

## Revisión de la política de contenido

Al activarla se encontraron y corrigieron dos cosas que la violaban: Vite incrustaba las fuentes pequeñas como `data:` URI (ahora `assetsInlineLimit: 0`, se sirven como archivos) y
React Router mostraba un aviso por no tener un marcador de carga inicial (ahora lo tiene). Comprobado en el navegador: sin violaciones de la política en las 15 pantallas.

## Pendiente o fuera de alcance

- No hay 2FA, bloqueo de cuenta tras N fallos ni rotación forzada de contraseñas (el límite por IP es el freno actual).
- No se firmó ni se cifró nada en la base de datos; la protección de los datos en reposo es la del disco del servidor.
- Las pruebas de penetración formales no se hicieron.
