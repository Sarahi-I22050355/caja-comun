const es = {
  titulo: 'Ahorro voluntario',
  descripcion: 'Cuota adicional que cada ahorrador decide aportar por nómina, separada del ahorro obligatorio. Solo hay aportaciones: no hay retiros.',
  kpi: { activos: 'Ahorradores con voluntario', cuota: 'Cuota voluntaria por periodo', acumulado: 'Acumulado voluntario' },
  buscar: 'Buscar por nombre o número de empleado',
  filtro: { todos: 'Todos', activos: 'Con voluntario activo', sin: 'Sin voluntario' },
  columnas: { empleado: 'Empleado', nombre: 'Ahorrador', estado: 'Estado', activo: 'Voluntario', cuota: 'Cuota por periodo', desde: 'Desde', acumulado: 'Acumulado', ahorro: 'Ahorro total' },
  activo: 'Activo',
  inactivo: 'Inactivo',
  editar: 'Voluntario de {{nombre}}',
  campos: { activo: 'Ahorro voluntario activo', cuota: 'Cuota voluntaria por periodo', desde: 'Rige desde', desdeAyuda: 'Por omisión, desde el primer periodo abierto.' },
  guardado: 'Cuota voluntaria guardada.',
}
const en: typeof es = {
  titulo: 'Voluntary savings',
  descripcion: 'Additional contribution each saver chooses to make through payroll, separate from mandatory savings. Contributions only: no withdrawals.',
  kpi: { activos: 'Savers with voluntary savings', cuota: 'Voluntary contribution per period', acumulado: 'Accumulated voluntary savings' },
  buscar: 'Search by name or employee number',
  filtro: { todos: 'All', activos: 'Voluntary on', sin: 'No voluntary' },
  columnas: { empleado: 'Employee', nombre: 'Saver', estado: 'Status', activo: 'Voluntary', cuota: 'Contribution per period', desde: 'Since', acumulado: 'Accumulated', ahorro: 'Total savings' },
  activo: 'On',
  inactivo: 'Off',
  editar: 'Voluntary savings of {{nombre}}',
  campos: { activo: 'Voluntary savings on', cuota: 'Voluntary contribution per period', desde: 'Effective from', desdeAyuda: 'By default, from the first open period.' },
  guardado: 'Voluntary contribution saved.',
}
export default { es, en }
