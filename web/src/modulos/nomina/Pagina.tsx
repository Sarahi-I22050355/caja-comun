import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdAdd, MdDelete, MdDownload, MdPlaylistAddCheck } from 'react-icons/md'
import { useCaja } from '../../auth/CajaActiva'
import { useSesion } from '../../auth/Sesion'
import { api, guardar } from '../../lib/api'
import { usePeriodosCaja, referenciaPeriodo } from '../../lib/consultas'
import { textoError } from '../../lib/errores'
import { dinero, entero_, fecha, fechaHora } from '../../lib/formato'
import { useDebounce } from '../../lib/hooks'
import type { AplicarPeriodoResultado, Descuento, DescuentosPeriodo, Incidencia, Layout, Pagina, TipoIncidencia } from '../../lib/tipos'
import { AhorradorPicker } from '../../ui/AhorradorPicker'
import type { AhorradorElegido } from '../../ui/AhorradorPicker'
import { Button, Card, KpiCard, PageHeader, StatusPill, tonoDe } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'
import { Input, SearchInput, Select } from '../../ui/form'
import { Modal } from '../../ui/Modal'
import { useToast } from '../../ui/Toast'

const FORMATOS = ['CSV', 'XLSX', 'TXT_PIPE', 'TXT_ANCHO'] as const
const TIPOS: TipoIncidencia[] = ['LICENCIA', 'INCAPACIDAD', 'SUSPENDE_VOLUNTARIO', 'CAMBIO_CUOTA', 'BAJA']
const NO_ELIMINABLES: TipoIncidencia[] = ['BAJA', 'CAMBIO_CUOTA']

function ModalIncidencia({ abierto, onCerrar, cajaId, periodoId, abiertoPeriodo, inicioPeriodo }: { abierto: boolean; onCerrar: () => void; cajaId?: number; periodoId?: number; abiertoPeriodo: boolean; inicioPeriodo?: string }) {
  const { t } = useTranslation('nomina')
  const { t: tc } = useTranslation()
  const cache = useQueryClient()
  const toast = useToast()
  const [ahorrador, setAhorrador] = useState<AhorradorElegido | null>(null)
  const [tipo, setTipo] = useState<TipoIncidencia>('LICENCIA')
  const [valor, setValor] = useState('')
  const [fechaBaja, setFechaBaja] = useState('')
  const [nota, setNota] = useState('')

  const crear = useMutation({
    mutationFn: () => api.post('/nomina/incidencias', {
      ahorradorId: ahorrador!.id, periodoId, tipo, valor: tipo === 'CAMBIO_CUOTA' ? Number(valor) : null,
      nota: nota.trim() || null, fechaBaja: tipo === 'BAJA' ? fechaBaja || inicioPeriodo : null,
    }),
    onSuccess: () => {
      for (const k of ['incidencias', 'descuentos', 'ahorradores', 'ahorrador', 'dashboard']) void cache.invalidateQueries({ queryKey: [k] })
      toast.ok(t('incidencias.creada'))
      setAhorrador(null); setNota(''); setValor('')
      onCerrar()
    },
    onError: (e) => toast.error(textoError(tc, e)),
  })

  return (
    <Modal abierto={abierto} onCerrar={onCerrar} titulo={t('incidencias.nueva')} descripcion={abiertoPeriodo ? undefined : t('incidencias.soloAbierto')}
      pie={<><Button variante="secondary" onClick={onCerrar}>{tc('acciones.cancelar')}</Button><Button cargando={crear.isPending} disabled={!ahorrador || !abiertoPeriodo} onClick={() => crear.mutate()}>{tc('acciones.guardar')}</Button></>}>
      <div className="grid gap-4">
        <AhorradorPicker id="inc-ahorrador" etiqueta={t('incidencias.ahorrador')} cajaId={cajaId} valor={ahorrador} onElegir={setAhorrador} />
        <Select etiqueta={t('incidencias.tipo')} value={tipo} onChange={(e) => setTipo(e.target.value as TipoIncidencia)} opciones={TIPOS.map((x) => ({ valor: x, texto: tc(`enums.tipoIncidencia.${x}`) }))} />
        <p className="-mt-2 text-xs text-suave">{t(`incidencias.ayuda.${tipo}`)}</p>
        {tipo === 'CAMBIO_CUOTA' && <Input etiqueta={t('incidencias.valor')} type="number" step="0.01" value={valor} onChange={(e) => setValor(e.target.value)} />}
        {tipo === 'BAJA' && <Input etiqueta={t('incidencias.fechaBaja')} type="date" value={fechaBaja || inicioPeriodo || ''} onChange={(e) => setFechaBaja(e.target.value)} />}
        <Input etiqueta={t('incidencias.nota')} maxLength={500} value={nota} onChange={(e) => setNota(e.target.value)} />
      </div>
    </Modal>
  )
}

export default function Pagina() {
  const { t } = useTranslation('nomina')
  const { t: tc } = useTranslation()
  const { puede } = useSesion()
  const { cajaId, caja } = useCaja()
  const cache = useQueryClient()
  const toast = useToast()
  const periodos = usePeriodosCaja(cajaId)
  const [anio, setAnio] = useState<number | null>(null)
  const [periodoId, setPeriodoId] = useState<number | null>(null)
  const [q, setQ] = useState('')
  const [filtro, setFiltro] = useState('')
  const [page, setPage] = useState(1)
  const [formato, setFormato] = useState<(typeof FORMATOS)[number]>('CSV')
  const [nuevaInc, setNuevaInc] = useState(false)
  const [confirmar, setConfirmar] = useState(false)
  const [resultado, setResultado] = useState<AplicarPeriodoResultado | null>(null)
  const [pageLayouts, setPageLayouts] = useState(1)
  const busqueda = useDebounce(q)

  const todos = periodos.data ?? []
  const anios = useMemo(() => [...new Set(todos.map((p) => p.anio))].sort((a, b) => b - a), [todos])
  const primeroAbierto = todos.find((p) => p.estado === 'ABIERTO')

  // Por omisión: el primer periodo abierto o, si no hay, el último aplicado.
  useEffect(() => {
    if (periodoId !== null || todos.length === 0) return
    const base = primeroAbierto ?? [...todos].reverse().find((p) => p.estado !== 'ABIERTO') ?? todos[0]
    setPeriodoId(base.id)
    setAnio(base.anio)
  }, [todos, periodoId, primeroAbierto])
  useEffect(() => { setPeriodoId(null); setAnio(null); setResultado(null) }, [cajaId])

  const periodo = todos.find((p) => p.id === periodoId)
  const frecuencia = caja?.frecuenciaNomina ?? 'QUINCENAL'
  const ref = periodo ? referenciaPeriodo(periodo, frecuencia) : ''

  const desc = useQuery({
    queryKey: ['descuentos', periodoId, busqueda, filtro, page],
    queryFn: () => api.get<DescuentosPeriodo>(`/nomina/periodos/${periodoId}/descuentos`, { q: busqueda, suspendido: filtro === '' ? undefined : filtro === 's', page, pageSize: 25 }),
    enabled: periodoId !== null,
    placeholderData: keepPreviousData,
  })
  const inc = useQuery({ queryKey: ['incidencias', periodoId], queryFn: () => api.get<Incidencia[]>(`/nomina/periodos/${periodoId}/incidencias`), enabled: periodoId !== null })
  const layouts = useQuery({
    queryKey: ['layouts', periodoId, pageLayouts],
    queryFn: () => api.get<Pagina<Layout>>('/nomina/layouts', { periodoId, page: pageLayouts, pageSize: 5 }),
    enabled: periodoId !== null,
    placeholderData: keepPreviousData,
  })

  const generar = useMutation({
    mutationFn: () => api.archivo(`/nomina/periodos/${periodoId}/layout`, { formato }, 'POST'),
    onSuccess: (a) => {
      guardar(a)
      toast.ok(t('layout.generado', { registros: a.cabeceras.get('X-Registros'), importe: dinero(Number(a.cabeceras.get('X-Importe-Total'))) }))
      void cache.invalidateQueries({ queryKey: ['layouts'] })
    },
    onError: (e) => toast.error(textoError(tc, e)),
  })

  const aplicar = useMutation({
    mutationFn: () => api.post<AplicarPeriodoResultado>(`/nomina/periodos/${periodoId}/aplicar`),
    onSuccess: (r) => {
      setResultado(r)
      setConfirmar(false)
      toast.ok(t('aplicar.resultado', { periodo: r.referencia }))
      for (const k of ['periodos', 'descuentos', 'dashboard', 'ahorradores', 'ahorrador', 'prestamos', 'cajas', 'descuentos-resumen']) void cache.invalidateQueries({ queryKey: [k] })
    },
    onError: (e) => { setConfirmar(false); toast.error(textoError(tc, e)) },
  })

  const eliminar = useMutation({
    mutationFn: (id: number) => api.delete(`/nomina/incidencias/${id}`),
    onSuccess: () => { toast.ok(t('incidencias.eliminada')); for (const k of ['incidencias', 'descuentos']) void cache.invalidateQueries({ queryKey: [k] }) },
    onError: (e) => toast.error(textoError(tc, e)),
  })

  const columnas = useMemo<Columnas<Descuento>>(
    () => [
      { header: t('columnas.empleado'), accessorKey: 'numEmpleado', meta: { ancho: '120px' } },
      { header: t('columnas.nombre'), accessorKey: 'nombre', cell: ({ row }) => <span className="font-semibold text-vino">{row.original.nombre}</span> },
      { header: t('columnas.ordinaria'), accessorKey: 'ordinaria', cell: ({ row }) => dinero(row.original.ordinaria), meta: { derecha: true } },
      { header: t('columnas.voluntaria'), accessorKey: 'voluntaria', cell: ({ row }) => dinero(row.original.voluntaria), meta: { derecha: true } },
      { header: t('columnas.prestamo'), accessorKey: 'prestamo', cell: ({ row }) => dinero(row.original.prestamo), meta: { derecha: true } },
      { header: t('columnas.total'), accessorKey: 'total', cell: ({ row }) => <span className="font-extrabold text-vino">{dinero(row.original.total)}</span>, meta: { derecha: true } },
      {
        header: t('columnas.estado'),
        id: 'estado',
        cell: ({ row }) => (
          <div className="flex flex-wrap gap-1">
            <StatusPill tono={row.original.suspendido ? 'pendiente' : 'ok'}>{row.original.suspendido ? t('suspendido') : t('conDescuento')}</StatusPill>
            {row.original.incidencias.map((i) => <StatusPill key={i} tono="info">{tc(`enums.tipoIncidencia.${i}`)}</StatusPill>)}
          </div>
        ),
      },
    ],
    [t, tc],
  )

  const columnasInc = useMemo<Columnas<Incidencia>>(
    () => [
      { header: t('columnas.empleado'), accessorKey: 'numEmpleado', meta: { ancho: '120px' } },
      { header: t('columnas.nombre'), accessorKey: 'nombre' },
      { header: t('incidencias.tipo'), accessorKey: 'tipo', cell: ({ row }) => <StatusPill tono="info">{tc(`enums.tipoIncidencia.${row.original.tipo}`)}</StatusPill> },
      { header: t('incidencias.valor'), accessorKey: 'valor', cell: ({ row }) => (row.original.valor === null ? '—' : dinero(row.original.valor)), meta: { derecha: true } },
      { header: t('incidencias.nota'), accessorKey: 'nota', cell: ({ row }) => row.original.nota ?? '—' },
      {
        header: '', id: 'acciones', meta: { derecha: true, ancho: '60px' },
        cell: ({ row }) => puede('capturar') && periodo?.estado === 'ABIERTO' && !NO_ELIMINABLES.includes(row.original.tipo) && (
          <button type="button" aria-label={tc('acciones.eliminar')} onClick={() => eliminar.mutate(row.original.id)} className="rounded-full p-1.5 text-lg text-estado-baja hover:bg-estado-baja-bg"><MdDelete aria-hidden /></button>
        ),
      },
    ],
    [t, tc, puede, periodo?.estado, eliminar],
  )

  const columnasLayouts = useMemo<Columnas<Layout>>(
    () => [
      { header: t('historial.archivo'), accessorKey: 'nombreArchivo', cell: ({ row }) => <span className="font-semibold text-vino">{row.original.nombreArchivo}</span> },
      { header: t('historial.formato'), accessorKey: 'formato', cell: ({ row }) => tc(`enums.formatoLayout.${row.original.formato}`, { defaultValue: row.original.formato }) },
      { header: t('historial.registros'), accessorKey: 'registros', cell: ({ row }) => entero_(row.original.registros), meta: { derecha: true } },
      { header: t('historial.importe'), accessorKey: 'importeTotal', cell: ({ row }) => dinero(row.original.importeTotal), meta: { derecha: true } },
      { header: t('historial.fecha'), accessorKey: 'fecha', cell: ({ row }) => fechaHora(row.original.fecha) },
      { header: t('historial.usuario'), accessorKey: 'usuario' },
    ],
    [t, tc],
  )

  const r = desc.data?.resumen
  const esSiguiente = !!periodo && periodo.estado === 'ABIERTO' && primeroAbierto?.id === periodo.id
  const periodosDelAnio = todos.filter((p) => p.anio === anio)

  if (periodos.isSuccess && todos.length === 0) return <Card><p className="p-6 text-sm text-suave">{t('sinPeriodos')}</p></Card>

  return (
    <>
      <PageHeader
        titulo={t('titulo')}
        descripcion={t('descripcion')}
        acciones={
          <>
            <Select nombre={t('anio')} value={anio ?? ''} onChange={(e) => { const a = Number(e.target.value); setAnio(a); const p = todos.filter((x) => x.anio === a); setPeriodoId((p.find((x) => x.estado === 'ABIERTO') ?? p[p.length - 1])?.id ?? null); setPage(1) }} opciones={anios.map((a) => ({ valor: a, texto: `${t('anio')} ${a}` }))} className="!w-auto" />
            <Select nombre={t('periodo')} value={periodoId ?? ''} onChange={(e) => { setPeriodoId(Number(e.target.value)); setPage(1); setResultado(null) }} className="!w-auto"
              opciones={periodosDelAnio.map((p) => ({ valor: p.id, texto: `${referenciaPeriodo(p, frecuencia)} · ${tc(`enums.estadoPeriodo.${p.estado}`)}` }))} />
          </>
        }
      />

      {periodo && (
        <p className="mb-4 flex flex-wrap items-center gap-2 text-sm text-suave">
          <StatusPill tono={tonoDe(periodo.estado)}>{tc(`enums.estadoPeriodo.${periodo.estado}`)}</StatusPill>
          <span><b className="text-vino">{ref}</b> · {fecha(periodo.fechaInicio)} – {fecha(periodo.fechaFin)}</span>
        </p>
      )}

      <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
        <KpiCard titulo={t('kpi.ahorradores')} cargando={desc.isLoading} valor={r && entero_(r.ahorradores - r.suspendidos)} detalle={r && t('kpi.suspendidos', { n: r.suspendidos })} />
        <KpiCard titulo={t('kpi.ordinaria')} cargando={desc.isLoading} valor={r && dinero(r.ordinaria)} />
        <KpiCard titulo={t('kpi.voluntaria')} cargando={desc.isLoading} valor={r && dinero(r.voluntaria)} />
        <KpiCard titulo={t('kpi.prestamo')} cargando={desc.isLoading} valor={r && dinero(r.prestamo)} />
        <KpiCard titulo={t('kpi.total')} cargando={desc.isLoading} valor={r && dinero(r.total, true)} />
      </section>

      {resultado && (
        <Card className="mt-6 border-estado-ok">
          <h2 className="font-bold text-estado-ok">{t('aplicar.resultado', { periodo: resultado.referencia })}</h2>
          <p className="text-sm text-suave">{t('aplicar.resumen', { movs: entero_(resultado.movimientosGenerados), liquidados: resultado.prestamosLiquidados, diferidas: resultado.cuotasDiferidas })}</p>
        </Card>
      )}

      <div className="mt-6 grid gap-6 xl:grid-cols-3">
        <Card className="xl:col-span-2 !p-0">
          <div className="flex flex-wrap items-center gap-3 p-4">
            <SearchInput valor={q} onCambio={(v) => { setQ(v); setPage(1) }} placeholder={t('buscar')} />
            <Select nombre={t('filtro.todos')} value={filtro} onChange={(e) => { setFiltro(e.target.value); setPage(1) }} className="!w-auto">
              <option value="">{t('filtro.todos')}</option>
              <option value="s">{t('filtro.suspendidos')}</option>
              <option value="n">{t('filtro.conDescuento')}</option>
            </Select>
          </div>
          <DataTable
            etiqueta={t('titulo')}
            columnas={columnas}
            datos={desc.data?.detalle.items}
            cargando={desc.isLoading}
            error={desc.isError ? desc.error : undefined}
            onReintentar={() => void desc.refetch()}
            pagina={desc.data && { page: desc.data.detalle.page, pageSize: desc.data.detalle.pageSize, total: desc.data.detalle.total }}
            onPagina={setPage}
          />
        </Card>

        <div className="flex flex-col gap-6">
          <Card>
            <h2 className="text-lg font-bold text-vino">{t('layout.titulo')}</h2>
            <div className="mt-3 flex flex-col gap-3">
              <Select etiqueta={t('layout.formato')} value={formato} onChange={(e) => setFormato(e.target.value as (typeof FORMATOS)[number])} opciones={FORMATOS.map((f) => ({ valor: f, texto: tc(`enums.formatoLayout.${f}`) }))} />
              <Button icono={<MdDownload />} cargando={generar.isPending} disabled={!puede('capturar') || periodoId === null} onClick={() => generar.mutate()}>{t('layout.generar')}</Button>
            </div>
          </Card>
          {puede('administrar') && (
            <Card>
              <h2 className="text-lg font-bold text-vino">{t('aplicar.boton')}</h2>
              <p className="mt-1 text-xs text-suave">{periodo?.estado !== 'ABIERTO' ? t('aplicar.yaAplicado') : !esSiguiente ? t('aplicar.noSiguiente') : t('aplicar.texto')}</p>
              <Button className="mt-3 w-full" icono={<MdPlaylistAddCheck />} disabled={!esSiguiente} onClick={() => setConfirmar(true)}>{t('aplicar.boton')}</Button>
            </Card>
          )}
        </div>
      </div>

      <Card className="mt-6 !p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 p-5">
          <h2 className="text-xl font-bold text-vino">{t('incidencias.titulo')}</h2>
          {puede('capturar') && <Button variante="secondary" icono={<MdAdd />} disabled={periodo?.estado !== 'ABIERTO'} onClick={() => setNuevaInc(true)}>{t('incidencias.nueva')}</Button>}
        </div>
        <DataTable etiqueta={t('incidencias.titulo')} columnas={columnasInc} datos={inc.data} cargando={inc.isLoading} error={inc.isError ? inc.error : undefined} onReintentar={() => void inc.refetch()} vacio={<p className="px-5 pb-6 text-sm text-suave">{t('incidencias.vacio')}</p>} />
      </Card>

      <Card className="mt-6 !p-0">
        <h2 className="p-5 pb-2 text-xl font-bold text-vino">{t('historial.titulo')}</h2>
        <DataTable
          etiqueta={t('historial.titulo')}
          columnas={columnasLayouts}
          datos={layouts.data?.items}
          cargando={layouts.isLoading}
          error={layouts.isError ? layouts.error : undefined}
          onReintentar={() => void layouts.refetch()}
          pagina={layouts.data && { page: layouts.data.page, pageSize: layouts.data.pageSize, total: layouts.data.total }}
          onPagina={setPageLayouts}
        />
      </Card>

      <ModalIncidencia abierto={nuevaInc} onCerrar={() => setNuevaInc(false)} cajaId={cajaId} periodoId={periodoId ?? undefined} abiertoPeriodo={periodo?.estado === 'ABIERTO'} inicioPeriodo={periodo?.fechaInicio} />
      <Modal abierto={confirmar} onCerrar={() => setConfirmar(false)} titulo={t('aplicar.titulo', { periodo: ref })} descripcion={t('aplicar.texto')} ancho="sm"
        pie={<><Button variante="secondary" onClick={() => setConfirmar(false)}>{tc('acciones.cancelar')}</Button><Button cargando={aplicar.isPending} onClick={() => aplicar.mutate()}>{t('aplicar.boton')}</Button></>}>
        {r && <p className="text-sm text-vino">{t('kpi.total')}: <b>{dinero(r.total, true)}</b></p>}
      </Modal>
    </>
  )
}
