# Frontend

React 18 + Vite + TypeScript + Tailwind, en `web/`. El diseño usa rosa `#E91E63`, vino `#880E4F`, fondo
`#FFF8FA`, Plus Jakarta Sans) y los tokens están en `tailwind.config.ts`.

## Cómo correrlo
```powershell
cd web
npm ci
npm run dev        # servidor de desarrollo con proxy a la API en http://localhost:8080
npm test           # vitest (componentes, formato y traducciones)
npm run build      # tsc + vite build
```
En Docker lo sirve nginx (`infra/Dockerfile.web`), que también reenvía `/api` a la API.

## Estructura
| Carpeta | Contenido |
|---|---|
| `src/lib` | cliente de la API (`api.ts`), tipos de las respuestas, formato de importes y fechas, traducción de errores, polling de trabajos |
| `src/ui` | botones, tarjetas, chips de estado, tabla (TanStack Table, con virtualización opcional), modales, formularios, selector de ahorrador, avisos |
| `src/auth` | sesión (token en memoria) y caja activa |
| `src/layout` | barra lateral (se colapsa a 80 px en tablet y es un panel en móvil) y encabezado (caja, ciclo, idioma, perfil) |
| `src/modulos` | una carpeta por pantalla con su página y su `i18n.ts` |
| `src/i18n` | inicialización de i18next; une los textos de todos los módulos |

## Pantallas y rutas
`/login`, `/` (inicio), `/cajas`, `/ahorradores`, `/ahorradores/:id`, `/sincronizacion`, `/nomina`, `/ahorro-voluntario`,
`/prestamos`, `/prestamos/nuevo`, `/prestamos/:id`, `/cierre-ciclo`, `/reportes`, `/tareas`, `/consultas` (Parte 2, ver [consultas-parte2.md](consultas-parte2.md)), `/usuarios`.
Todas excepto `/login` exigen sesión.

## Idioma
Español por omisión; el idioma elegido se guarda en el navegador (`caja-comun.idioma`). Para agregar un texto se escribe en
el `i18n.ts` del módulo en los dos idiomas; la prueba `i18n.test.ts` falla si falta una clave en alguno.

## Roles
| Pantalla / acción | Administrador | Capturista | Consulta |
|---|---|---|---|
| Ver todo, exportar | sí | sí | sí |
| Capturar (altas, incidencias, voluntario, solicitar préstamo) | sí | sí | no |
| Aplicar periodo, autorizar o rechazar préstamo, cerrar ciclo, SPEI | sí | no | no |
| Editar tareas, usuarios, cajas | sí | no | no |

## Verificación manual
Con el stack levantado (`scripts/up.ps1`) y datos de `scripts/seed.ps1`:
1. Entrar como Administrador, recorrer el menú completo y confirmar que cada pantalla carga datos reales.
2. Cambiar a EN en `/prestamos/nuevo` con un ahorrador y un monto escritos: el texto se traduce y los valores se conservan.
3. Entrar como Capturista: no aparece "Usuarios", las tareas son de solo lectura, no hay "Aplicar periodo" ni "Calcular distribución".
4. Reducir a 375 px de ancho: ninguna pantalla debe tener desplazamiento horizontal de la página (las tablas se desplazan dentro de su tarjeta).
