// Tipos de las respuestas de la API (ver docs/api.md). Los enums llegan como códigos (ACTIVO, AHORRO_ORDINARIO...).

export type Rol = 'ADMINISTRADOR' | 'CAPTURISTA' | 'CONSULTA'
export type EstadoAhorrador = 'ACTIVO' | 'LICENCIA' | 'BAJA'
export type EstadoPeriodo = 'ABIERTO' | 'APLICADO' | 'CERRADO'
export type EstadoPrestamo = 'PENDIENTE' | 'VIGENTE' | 'LIQUIDADO' | 'RECHAZADO'
export type EstadoAmortizacion = 'PROYECTADO' | 'PROGRAMADO' | 'APLICADO'
export type EstadoCiclo = 'ABIERTO' | 'EN_CIERRE' | 'CERRADO'
export type TipoMovimiento =
  | 'AHORRO_ORDINARIO'
  | 'AHORRO_VOLUNTARIO'
  | 'DESEMBOLSO_PRESTAMO'
  | 'ABONO_CAPITAL'
  | 'ABONO_INTERES'
  | 'INTERES_ANUAL'
  | 'LIQUIDACION_CICLO'
export type TipoIncidencia = 'LICENCIA' | 'INCAPACIDAD' | 'BAJA' | 'CAMBIO_CUOTA' | 'SUSPENDE_VOLUNTARIO'
export type Frecuencia = 'QUINCENAL' | 'SEMANAL'

export interface Pagina<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
  totalPages: number
}

export interface CajaAcceso {
  id: number
  nombre: string
  empresa: string
}

export interface Usuario {
  id: number
  nombre: string
  correo: string
  rol: Rol
  accesoTodasCajas: boolean
  cajas: CajaAcceso[]
}

export interface Sesion {
  accessToken: string
  expiraEnSegundos: number
  usuario: Usuario
}

export interface Caja {
  id: number
  empresaId: number
  empresa: string
  nombre: string
  prefijo: string
  tasaInteresMensual: number
  frecuenciaNomina: Frecuencia
  inicioCiclo: string
  finCiclo: string
  activa: boolean
  ahorradoresActivos: number
  ahorroTotal: number
}

export interface Empresa {
  id: number
  nombre: string
  activa: boolean
}

export interface Periodo {
  id: number
  anio: number
  numero: number
  fechaInicio: string
  fechaFin: string
  estado: EstadoPeriodo
}

export interface AhorradorLista {
  id: number
  cajaId: number
  caja: string
  numEmpleado: string
  nombre: string
  apellidos: string
  departamento: string
  estado: EstadoAhorrador
  sueldoQuincenal: number
  ahorroTotal: number
}

export interface Cuota {
  id: number
  cuotaOrdinaria: number
  cuotaVoluntaria: number
  voluntarioActivo: boolean
  vigenteDesde: string
}

export interface AhorroResumen {
  ordinario: number
  voluntario: number
  total: number
  limitePrestamo: number
  saldoPrestamos: number
  disponibleSinAval: number
}

export interface PrestamoVigenteResumen {
  folio: string
  monto: number
  plazoQuincenas: number
  tasaMensual: number
  saldoVigente: number
  cuotasRestantes: number
}

export interface AhorradorDetalle {
  id: number
  cajaId: number
  caja: string
  numEmpleado: string
  nombre: string
  apellidos: string
  rfc: string
  fechaNacimiento: string
  fechaIngreso: string
  fechaBaja: string | null
  estado: EstadoAhorrador
  sueldoQuincenal: number
  clabe: string
  departamento: string
  cuotaVigente: Cuota | null
  ahorro: AhorroResumen
  prestamosVigentes: PrestamoVigenteResumen[]
}

export interface Movimiento {
  id: number
  fecha: string
  tipo: TipoMovimiento
  monto: number
  referencia: string | null
  periodoId: number | null
  folioPrestamo: string | null
  efectoAhorro: number
  saldoAhorro: number
}

export interface MovimientoCaja {
  id: number
  cajaId: number
  caja: string
  ahorradorId: number
  numEmpleado: string
  ahorrador: string
  fecha: string
  tipo: TipoMovimiento
  monto: number
  referencia: string | null
  periodoId: number | null
  folioPrestamo: string | null
}

export interface EstadoCuenta {
  ahorradorId: number
  numEmpleado: string
  nombre: string
  caja: string
  anio: number
  saldoInicial: number
  saldoFinal: number
  totales: {
    aportacionesOrdinarias: number
    aportacionesVoluntarias: number
    liquidaciones: number
    desembolsos: number
    abonosCapital: number
    abonosInteres: number
    rendimientoAnual: number
  }
  movimientos: Movimiento[]
}

export interface Dashboard {
  fondoTotal: number
  ahorradoresActivos: number
  prestamosVigentes: number
  saldoPrestamos: number
  anioCiclo: number
  interesAcumuladoCiclo: number
  serie: { anio: number; mes: number; ahorroAportado: number; prestamosDesembolsados: number; capitalCobrado: number }[]
  ultimosMovimientos: MovimientoCaja[]
}

// ---------- Nómina ----------
export interface Descuento {
  ahorradorId: number
  numEmpleado: string
  nombre: string
  ordinaria: number
  voluntaria: number
  prestamo: number
  total: number
  suspendido: boolean
  incidencias: TipoIncidencia[]
}

export interface DescuentosPeriodo {
  periodo: Periodo
  cajaId: number
  caja: string
  frecuencia: Frecuencia
  referencia: string
  resumen: { ahorradores: number; suspendidos: number; ordinaria: number; voluntaria: number; prestamo: number; total: number }
  detalle: Pagina<Descuento>
}

export interface Incidencia {
  id: number
  ahorradorId: number
  numEmpleado: string
  nombre: string
  periodoId: number
  tipo: TipoIncidencia
  valor: number | null
  nota: string | null
}

export interface AplicarPeriodoResultado {
  periodoId: number
  referencia: string
  ahorradoresConDescuento: number
  suspendidos: number
  movimientosGenerados: number
  ahorroOrdinario: number
  ahorroVoluntario: number
  capitalPrestamos: number
  interesPrestamos: number
  prestamosLiquidados: number
  cuotasDiferidas: number
}

export interface Layout {
  id: number
  tipo: 'NOMINA' | 'SPEI'
  cajaId: number
  periodoId: number | null
  periodo: string | null
  formato: string
  nombreArchivo: string
  registros: number
  importeTotal: number
  sumaControl: string | null
  usuario: string
  fecha: string
}

// ---------- Importaciones ----------
export interface ErrorFila {
  campo: string
  codigo: string
}

export interface ImportacionResumen {
  filas: number
  altas: number
  bajas: number
  sinCambio: number
  errores: number
}

export interface Importacion {
  id: number
  cajaId: number
  caja: string
  archivo: string
  formato: string
  usuario: string
  fecha: string
  estado: 'PREVIA' | 'APLICADA' | 'CANCELADA'
  resumen: ImportacionResumen
}

export interface ImportacionDetalle {
  id: number
  numeroFila: number
  tipo: 'ALTA' | 'BAJA' | 'SIN_CAMBIO'
  numEmpleado: string
  validacion: 'VALIDO' | 'ERROR'
  errores: ErrorFila[]
  datos: Record<string, string | null>
}

export interface VistaPreviaImportacion {
  importacion: Importacion
  detalle: Pagina<ImportacionDetalle>
}

export interface AplicarImportacionResultado {
  id: number
  altasAplicadas: number
  bajasAplicadas: number
  sinCambio: number
  omitidas: number
  ahorroLiquidado: number
}

// ---------- Voluntario ----------
export interface Voluntario {
  ahorradorId: number
  cajaId: number
  numEmpleado: string
  nombre: string
  estado: EstadoAhorrador
  activo: boolean
  cuotaVoluntaria: number
  vigenteDesde: string | null
  acumuladoVoluntario: number
  ahorroTotal: number
}

// ---------- Préstamos ----------
export interface AvalEvaluacion {
  ahorradorId: number
  numEmpleado: string
  nombre: string
  disponible: number
}

export interface Evaluacion {
  ahorro: number
  limite: number
  saldoPrestamos: number
  disponibleSinAval: number
  requiereAval: boolean
  excedenteAval: number
  garantia: 'AHORRO_80' | 'PAGARE_AVAL'
  aprobada: boolean
  motivo: string | null
  aval: AvalEvaluacion | null
}

export interface CuotaPrestamo {
  numero: number
  periodoId: number | null
  periodo: string | null
  fecha: string | null
  saldoInicial: number
  capital: number
  interes: number
  total: number
  saldoFinal: number
  estado: EstadoAmortizacion | null
}

export interface Simulacion {
  cajaId: number
  frecuencia: Frecuencia
  tasaMensual: number
  plazoQuincenas: number
  evaluacion: Evaluacion
  primeraCuota: number
  totalIntereses: number
  totalAPagar: number
  amortizacion: CuotaPrestamo[]
}

export interface Prestamo {
  id: number
  folio: string
  cajaId: number
  caja: string
  ahorradorId: number
  numEmpleado: string
  ahorrador: string
  monto: number
  plazoQuincenas: number
  tasaMensual: number
  garantia: 'AHORRO_80' | 'PAGARE_AVAL'
  aval: { ahorradorId: number; numEmpleado: string; nombre: string } | null
  excedenteAval: number
  estado: EstadoPrestamo
  fechaSolicitud: string
  fechaAutorizacion: string | null
  autorizadoPor: string | null
  capitalPagado: number
  interesPagado: number
  saldoVigente: number
  cuotasAplicadas: number
  cuotasRestantes: number
}

export interface PrestamoDetalle {
  prestamo: Prestamo
  amortizacion: CuotaPrestamo[]
  cuotaActual: number | null
}

export interface CarteraResumen {
  vigentes: number
  pendientes: number
  liquidados: number
  rechazados: number
  capitalColocado: number
  saldoVigente: number
  interesCobrado: number
}

// ---------- Ciclos, reportes, tareas ----------
export interface Ciclo {
  id: number
  cajaId: number
  caja: string
  anio: number
  estado: EstadoCiclo
  interesTotal: number
  fechaCierre: string | null
  participantes: number
}

export interface CicloResumen {
  ciclo: Ciclo
  interesCobrado: number
  interesDevengado: number
  periodosTotales: number
  periodosAplicados: number
  prestamosVigentes: number
  saldoDiasTotal: number
  interesDistribuido: number
  ahorroLiquidado: number
  ahorroRetenido: number
  puedeCerrar: boolean
  impedimentos: string[]
}

export interface Distribucion {
  ahorradorId: number
  numEmpleado: string
  nombre: string
  saldoDias: number
  saldoPromedio: number
  diasActivos: number
  participacion: number
  interesAsignado: number
  ahorroLiquidado: number
  ahorroRetenido: number
}

export interface EstadoResultados {
  cajaId: number
  caja: string
  anio: number
  estadoCiclo: EstadoCiclo | null
  interesesCobrados: number
  interesesDevengados: number
  utilidadADistribuir: number
  interesDistribuido: number
  saldoCartera: number
  prestamosVigentes: number
  meses: { mes: number; cobrados: number; devengados: number }[]
}

export interface Tarea {
  id: number
  clave: string
  nombre: string
  frecuencia: 'DIARIA' | 'QUINCENAL' | 'MENSUAL'
  hora: string
  activa: boolean
  ultimaEjecucion: string | null
  proximaEjecucion: string | null
  ultimoEstado: 'EN_CURSO' | 'EXITO' | 'ERROR' | null
  ultimoResumen: string | null
}

export interface Ejecucion {
  id: number
  inicio: string
  fin: string | null
  estado: 'EN_CURSO' | 'EXITO' | 'ERROR'
  resumen: string | null
}

export interface Trabajo {
  id: string
  tipo: string
  estado: 'PENDIENTE' | 'EN_CURSO' | 'EXITO' | 'ERROR'
  creado: string
  inicio: string | null
  fin: string | null
  resultado: Record<string, unknown> | null
  codigoError: string | null
  mensajeError: string | null
  archivo: string | null
}

export interface UsuarioAdmin {
  id: number
  nombre: string
  correo: string
  rol: Rol
  accesoTodasCajas: boolean
  activo: boolean
  ultimoAcceso: string | null
  cajas: CajaAcceso[]
}

// --- Parte 2: consultas distribuidas ---
export interface ColumnaConsulta {
  nombre: string
  tipo: string
}

export type CeldaConsulta = string | number | boolean | null

export interface ResultadoConsulta {
  consulta: number
  anio: number | null
  sql: string
  columnas: ColumnaConsulta[]
  filas: CeldaConsulta[][]
  total: number
  page: number
  pageSize: number
  duracionMs: number
}

export interface EquivalenciaConsulta {
  consulta: number
  anio: number | null
  filasFederadas: number
  filasDirectas: number
  coincide: boolean
  diferencias: number
  ejemplo: string | null
  sqlDirecto: string
  duracionFederadaMs: number
  duracionDirectaMs: number
}

export interface MotorFederacion {
  motor: string
  catalogo: string
  cajas: string
  ahorradores: number
  movimientos: number
}

export interface EstadoFederacion {
  disponible: boolean
  motivo: string | null
  motores: MotorFederacion[]
  consultas: { numero: number; usaAnio: boolean; usaFechaCorte: boolean }[]
}
