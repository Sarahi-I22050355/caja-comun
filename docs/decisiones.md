# Decisiones del proyecto

Cada decisión indica si es **verificada** (con fuente) o **supuesto**.

## Plataforma
- **.NET 10 LTS con EF Core 9 y Pomelo 9.** .NET 8 terminó su soporte el 10-nov-2026 ([anuncio](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)). Pomelo no publica versión para EF Core 10; se usa Pomelo 9 con EF Core 9, que según sus mantenedores funciona en .NET 10. *Verificado en la fase de base de datos con el primer build.*
- **React 18** (la plantilla de Vite instala 19; se fijó 18 por requerimiento) y **Tailwind 3** (permite `tailwind.config.ts` con los tokens de diseño).
- **MariaDB 11** confirmada como la base de la materia.
- Las pruebas .NET se ejecutan dentro de Docker (`scripts/test.ps1`): en el equipo de desarrollo la directiva de control de aplicaciones de Windows bloquea los ensamblados de prueba compilados localmente. *Supuesto operativo.*

## Negocio
- **Ciclo = año natural**, de la quincena Q1 a la Q24 (corte 31 de diciembre). *Decisión de la autora.*
- **Layout SPEI**: por defecto dispersa ahorro acumulado más rendimientos, como pide el enunciado; al cerrar el ciclo el ahorro se liquida y el del ciclo siguiente inicia en cero. Parámetro opcional para dispersar solo el rendimiento. Se agrega el movimiento `LIQUIDACION_CICLO`.
- **Roles**: Administrador todo; Capturista captura altas/bajas, nómina, incidencias, ahorro voluntario y solicita préstamos, pero no autoriza préstamos, no cierra ciclos ni administra usuarios o tareas; Consulta solo lee y exporta.
- **Tareas programadas**: un módulo genérico; la única tarea real es el cálculo de interés.
- Prefijos de número de empleado: `GIM-`, `ACE-`, `TDB-`. Login por correo.
- Datos de prueba: el año en curso es 2024 hasta la quincena Q20 (31-oct-2024) por especificación del proyecto.

## Parte 2
- Trino como federador con MariaDB (GIMSA), PostgreSQL (Aceros) y ClickHouse (Transportes); MariaDB principal conserva todo. *Se valida la memoria disponible en esa fase.*
