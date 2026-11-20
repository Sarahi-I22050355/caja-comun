import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdDownload, MdLock } from 'react-icons/md'
import { useCaja } from '../../auth/CajaActiva'
import { useSesion } from '../../auth/Sesion'
import { ApiError, api, guardar } from '../../lib/api'
import { textoCodigo, textoError } from '../../lib/errores'
import { decimal, dinero, entero_, fecha, fechaHora, porcentaje } from '../../lib/formato'
import { useDebounce } from '../../lib/hooks'
import { useTrabajo } from '../../lib/trabajos'
import type { Ciclo, CicloResumen, Distribucion, Layout, Pagina } from '../../lib/tipos'
import { Button, Card, EmptyState, ErrorState, KpiCard, PageHeader, Progreso, StatusPill, tonoDe } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'
import { SearchInput, Select, Toggle } from '../../ui/form'
import { Modal } from '../../ui/Modal'
import { Stepper } from '../../ui/Stepper'
import { useToast } from '../../ui/Toast'

const FORMATOS = ['GENERICO_CSV', 'BANORTE_SIMPLIFICADO'] as const

export default function Pagina() {
  const { t } = useTranslation('cierre')
  const { t: tc } = useTranslation()
  const { puede } = useSesion()
  const { cajaId } = useCaja()
  const cache = useQueryClient()
  const toast = useToast()
  const [anio, setAnio] = useState<number | null>(null)
  const [paso, setPaso] = useState(0)
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageLayouts, setPageLayouts] = useState(1)
  const [confirmar, setConfirmar] = useState(false)
  const [trabajoId, setTrabajoId] = useState<string | null>(null)
  const [formato, setFormato] = useState<(typeof FORMATOS)[number]>('GENERICO_CSV')
  const [incluirAhorro, setIncluirAhorro] = useState(true)
  const [detalleSpei, setDetalleSpei] = useState<string | null>(null)
  const busqueda = useDebounce(q)

  const ciclos = useQuery({ queryKey: ['ciclos', cajaId], queryFn: () => api.get<Ciclo[]>('/ciclos', { cajaId }), enabled: cajaId !== undefined })
  const lista = ciclos.data ?? []
  useEffect(() => { setAnio(null); setPaso(0); setTrabajoId(null) }, [cajaId])
  // Por omisión: el ciclo más reciente que todavía no se cierra.
  useEffect(() => {
    if (anio !== null || !ciclos.isSuccess) return
    // Una caja sin ciclos todavía propone el año en curso para poder calcular el primero.
    setAnio(lista.length === 0 ? new Date().getFullYear() : (lista.find((c) => c.estado !== 'CERRADO') ?? lista[0]).anio)
  }, [lista, anio, ciclos.isSuccess])

  const ciclo = lista.find((c) => c.anio === anio)
  const resumen = useQuery({ queryKey: ['ciclo', ciclo?.id], queryFn: () => api.get<CicloResumen>(`/ciclos/${ciclo!.id}`), enabled: !!ciclo })
  const cerrado = ciclo?.estado === 'CERRADO'
  const r = resumen.data

  const distribucion = useQuery({
    queryKey: ['ciclo-distribucion', ciclo?.id, busqueda, page],
    queryFn: () => api.get<Pagina<Distribucion>>(`/ciclos/${ciclo!.id}/distribucion`, { q: busqueda, page, pageSize: 25 }),
    enabled: !!ciclo && paso === 1,
    placeholderData: keepPreviousData,
  })
  const layouts = useQuery({
    queryKey: ['ciclo-layouts', ciclo?.id, pageLayouts],
    queryFn: () => api.get<Pagina<Layout>>(`/ciclos/${ciclo!.id}/layouts`, { page: pageLayouts, pageSize: 5 }),
    enabled: !!ciclo && paso === 2,
    placeholderData: keepPreviousData,
  })

  const refrescar = () => { for (const k of ['ciclos', 'ciclo', 'ciclo-distribucion', 'ciclo-layouts', 'periodos', 'dashboard', 'ahorradores', 'ahorrador', 'cajas']) void cache.invalidateQueries({ queryKey: [k] }) }

  const borrador = useMutation({
    mutationFn: () => api.post<CicloResumen>('/ciclos/borrador', { cajaId, anio }),
    onSuccess: () => { toast.ok(t('paso1.calculado')); refrescar() },
    onError: (e) => toast.error(textoError(tc, e)),
  })

  const cerrar = useMutation({
    mutationFn: () => api.post<{ id: string }>(`/ciclos/${ciclo!.id}/cerrar`),
    onSuccess: (j) => { setTrabajoId(j.id); setConfirmar(false) },
    onError: (e) => { setConfirmar(false); toast.error(textoError(tc, e)) },
  })
  const trabajo = useTrabajo(trabajoId)
  useEffect(() => {
    const tr = trabajo.data
    if (!tr) return
    if (tr.estado === 'EXITO') { toast.ok(t('paso2.cerrado')); setTrabajoId(null); refrescar(); setPaso(2) }
    if (tr.estado === 'ERROR') { toast.error(textoCodigo(tc, tr.codigoError ?? 'generico')); setTrabajoId(null) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [trabajo.data?.estado])

  const spei = useMutation({
    mutationFn: () => api.archivo(`/ciclos/${ciclo!.id}/layout-spei`, { formato, incluirAhorro }, 'POST'),
    onSuccess: (a) => {
      guardar(a)
      setDetalleSpei(null)
      toast.ok(t('paso3.generado', { registros: a.cabeceras.get('X-Registros'), importe: dinero(Number(a.cabeceras.get('X-Importe-Total'))) }))
      void cache.invalidateQueries({ queryKey: ['ciclo-layouts'] })
    },
    onError: (e) => {
      const lista = e instanceof ApiError ? e.errors?.renglones : undefined
      setDetalleSpei(lista ? t('paso3.detalleError', { lista: lista.slice(0, 5).join(', ') }) : null)
      toast.error(textoError(tc, e))
    },
  })

  const columnas = useMemo<Columnas<Distribucion>>(
    () => [
      { header: t('paso2.columnas.empleado'), accessorKey: 'numEmpleado', meta: { ancho: '120px' } },
      { header: t('paso2.columnas.nombre'), accessorKey: 'nombre', cell: ({ row }) => <span className="font-semibold text-vino">{row.original.nombre}</span> },
      { header: t('paso2.columnas.saldoDias'), accessorKey: 'saldoDias', cell: ({ row }) => decimal(row.original.saldoDias), meta: { derecha: true } },
      { header: t('paso2.columnas.promedio'), accessorKey: 'saldoPromedio', cell: ({ row }) => dinero(row.original.saldoPromedio), meta: { derecha: true } },
      { header: t('paso2.columnas.dias'), accessorKey: 'diasActivos', meta: { derecha: true } },
      { header: t('paso2.columnas.participacion'), accessorKey: 'participacion', cell: ({ row }) => porcentaje(row.original.participacion, 4), meta: { derecha: true } },
      { header: t('paso2.columnas.interes'), accessorKey: 'interesAsignado', cell: ({ row }) => <span className="font-extrabold text-vino">{dinero(row.original.interesAsignado)}</span>, meta: { derecha: true } },
      { header: t('paso2.columnas.liquidado'), accessorKey: 'ahorroLiquidado', cell: ({ row }) => dinero(row.original.ahorroLiquidado), meta: { derecha: true } },
      { header: t('paso2.columnas.retenido'), accessorKey: 'ahorroRetenido', cell: ({ row }) => (row.original.ahorroRetenido > 0 ? dinero(row.original.ahorroRetenido) : '—'), meta: { derecha: true } },
    ],
    [t],
  )

  const columnasLayouts = useMemo<Columnas<Layout>>(
    () => [
      { header: t('paso3.columnas.archivo'), accessorKey: 'nombreArchivo', cell: ({ row }) => <span className="font-semibold text-vino">{row.original.nombreArchivo}</span> },
      { header: t('paso3.columnas.formato'), accessorKey: 'formato', cell: ({ row }) => tc(`enums.formatoSpei.${row.original.formato}`, { defaultValue: row.original.formato }) },
      { header: t('paso3.columnas.registros'), accessorKey: 'registros', cell: ({ row }) => entero_(row.original.registros), meta: { derecha: true } },
      { header: t('paso3.columnas.importe'), accessorKey: 'importeTotal', cell: ({ row }) => dinero(row.original.importeTotal), meta: { derecha: true } },
      { header: t('paso3.columnas.control'), accessorKey: 'sumaControl', cell: ({ row }) => <span className="font-mono text-xs">{row.original.sumaControl?.slice(0, 12)}…</span> },
      { header: t('paso3.columnas.fecha'), accessorKey: 'fecha', cell: ({ row }) => fechaHora(row.original.fecha) },
      { header: t('paso3.columnas.usuario'), accessorKey: 'usuario' },
    ],
    [t, tc],
  )

  const aniosOpciones = useMemo(() => {
    const anios = new Set(lista.map((c) => c.anio))
    if (anio !== null) anios.add(anio)
    return [...anios].sort((a, b) => b - a)
  }, [lista, anio])

  const impedimento = (codigo: string) => {
    const [base, n] = codigo.split(':')
    return t(`paso1.impedimentos.${base}`, { n, defaultValue: textoCodigo(tc, base) })
  }
  const cerrando = !!trabajoId

  return (
    <>
      <PageHeader
        titulo={t('titulo')}
        descripcion={t('descripcion')}
        acciones={
          <>
            {ciclo && <StatusPill tono={tonoDe(ciclo.estado)}>{tc(`enums.estadoCiclo.${ciclo.estado}`)}</StatusPill>}
            <div className="flex items-center gap-2">
              <Select nombre={t('anio')} value={anio ?? ''} onChange={(e) => { setAnio(Number(e.target.value)); setPaso(0); setPage(1) }} opciones={aniosOpciones.map((a) => ({ valor: a, texto: `${t('anio')} ${a}` }))} className="!w-auto" />
            </div>
          </>
        }
      />
      <Card className="mb-6">
        <Stepper
          actual={paso}
          onElegir={(i) => setPaso(i)}
          pasos={[
            { titulo: t('pasos.interes'), detalle: t('pasos.interesDetalle') },
            { titulo: t('pasos.distribucion'), detalle: t('pasos.distribucionDetalle') },
            { titulo: t('pasos.layout'), detalle: t('pasos.layoutDetalle') },
          ]}
        />
      </Card>

      {ciclos.isError ? <Card><ErrorState detalle={textoError(tc, ciclos.error)} onReintentar={() => void ciclos.refetch()} /></Card> : null}

      {/* Paso 1: interés generado */}
      {paso === 0 && (
        <>
          <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <KpiCard titulo={t('paso1.cobrado')} cargando={!!ciclo && resumen.isLoading} valor={dinero(r?.interesCobrado ?? 0, true)} />
            <KpiCard titulo={t('paso1.devengado')} cargando={!!ciclo && resumen.isLoading} valor={dinero(r?.interesDevengado ?? 0)} />
            <KpiCard titulo={t('paso1.periodos')} cargando={!!ciclo && resumen.isLoading} valor={t('paso1.periodosValor', { aplicados: r?.periodosAplicados ?? 0, total: r?.periodosTotales ?? 0 })} />
            <KpiCard titulo={t('paso1.prestamos')} cargando={!!ciclo && resumen.isLoading} valor={entero_(r?.prestamosVigentes ?? 0)} />
          </section>
          <Card className="mt-6">
            {!ciclo && !ciclos.isLoading && <p className="mb-3 text-sm text-suave">{t('paso1.sinCiclo', { anio })}</p>}
            {r && r.impedimentos.length > 0 && (
              <ul className="mb-4 flex flex-col gap-1.5">
                {r.impedimentos.map((i) => <li key={i} className={`rounded-control px-3 py-2 text-sm font-medium ${i === 'CICLO_YA_CERRADO' ? 'bg-estado-ok-bg text-estado-ok' : 'bg-estado-pendiente-bg text-estado-pendiente'}`}>{impedimento(i)}</li>)}
              </ul>
            )}
            {cerrado && ciclo?.fechaCierre && <p className="mb-3 text-sm text-suave">{t('cerradoEl', { fecha: fechaHora(ciclo.fechaCierre) })}</p>}
            <div className="flex flex-wrap gap-2">
              {puede('administrar') && !cerrado && <Button cargando={borrador.isPending} disabled={anio === null || cajaId === undefined} onClick={() => borrador.mutate()}>{ciclo?.estado === 'EN_CIERRE' ? t('paso1.recalcular') : t('paso1.calcular')}</Button>}
              <Button variante="secondary" disabled={!ciclo || ciclo.participantes === 0} onClick={() => setPaso(1)}>{tc('acciones.continuar')}</Button>
            </div>
          </Card>
        </>
      )}

      {/* Paso 2: distribución */}
      {paso === 1 && (
        <>
          <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
            <KpiCard titulo={t('paso2.total')} cargando={resumen.isLoading} valor={r && dinero(r.ciclo.interesTotal, true)} />
            <KpiCard titulo={t('paso2.distribuido')} cargando={resumen.isLoading} valor={r && dinero(r.interesDistribuido)} />
            <KpiCard titulo={t('paso2.participantes')} cargando={resumen.isLoading} valor={r && entero_(r.ciclo.participantes)} />
            <KpiCard titulo={t('paso2.liquidado')} cargando={resumen.isLoading} valor={r && dinero(r.ahorroLiquidado)} />
            <KpiCard titulo={t('paso2.retenido')} cargando={resumen.isLoading} valor={r && dinero(r.ahorroRetenido)} detalle={t('paso2.explicacionRetenido')} />
          </section>
          {!ciclo || ciclo.participantes === 0 ? (
            <Card className="mt-6"><EmptyState detalle={t('paso1.sinCiclo', { anio })} /></Card>
          ) : (
            <Card className="mt-6 !p-0">
              <div className="flex flex-wrap items-center justify-between gap-3 p-4">
                <SearchInput valor={q} onCambio={(v) => { setQ(v); setPage(1) }} placeholder={t('paso2.buscar')} />
                {puede('administrar') && !cerrado && (
                  <Button icono={<MdLock />} disabled={!r?.puedeCerrar || cerrando} cargando={cerrando} onClick={() => setConfirmar(true)}>{t('paso2.cerrar')}</Button>
                )}
              </div>
              {cerrando && (
                <div className="px-4 pb-4" role="status">
                  <p className="mb-1 text-sm font-medium text-vino">{t('paso2.cerrando')}</p>
                  <Progreso valor={trabajo.data?.estado === 'EN_CURSO' ? 0.6 : 0.25} etiqueta={t('paso2.cerrando')} />
                </div>
              )}
              {r && !r.puedeCerrar && !cerrado && r.impedimentos.length > 0 && <p className="px-4 pb-3 text-sm font-medium text-estado-pendiente">{impedimento(r.impedimentos[0])}</p>}
              <DataTable
                etiqueta={t('pasos.distribucion')}
                columnas={columnas}
                datos={distribucion.data?.items}
                cargando={distribucion.isLoading}
                error={distribucion.isError ? distribucion.error : undefined}
                onReintentar={() => void distribucion.refetch()}
                pagina={distribucion.data && { page: distribucion.data.page, pageSize: distribucion.data.pageSize, total: distribucion.data.total }}
                onPagina={setPage}
              />
            </Card>
          )}
        </>
      )}

      {/* Paso 3: layout SPEI */}
      {paso === 2 && (
        <>
          <Card>
            {!cerrado ? (
              <p className="text-sm font-medium text-estado-pendiente">{t('paso3.noCerrado')}</p>
            ) : (
              <div className="grid items-end gap-4 md:grid-cols-3">
                <Select etiqueta={t('paso3.formato')} value={formato} onChange={(e) => setFormato(e.target.value as (typeof FORMATOS)[number])} opciones={FORMATOS.map((f) => ({ valor: f, texto: tc(`enums.formatoSpei.${f}`) }))} />
                <label className="flex items-center gap-3 text-sm font-medium text-vino">
                  <Toggle activo={incluirAhorro} onCambio={setIncluirAhorro} etiqueta={t('paso3.incluirAhorro')} />
                  <span>{t('paso3.incluirAhorro')}</span>
                </label>
                {puede('administrar') && <Button icono={<MdDownload />} cargando={spei.isPending} onClick={() => spei.mutate()}>{t('paso3.generar')}</Button>}
              </div>
            )}
            {detalleSpei && <p role="alert" className="mt-3 rounded-control bg-estado-baja-bg px-3 py-2 text-sm font-medium text-estado-baja">{detalleSpei}</p>}
          </Card>
          {r && <p className="mt-3 text-sm text-suave">{t('paso2.total')}: <b className="text-vino">{dinero(r.ciclo.interesTotal)}</b> · {t('paso2.liquidado')}: <b className="text-vino">{dinero(r.ahorroLiquidado)}</b>{ciclo?.fechaCierre && <> · {t('cerradoEl', { fecha: fecha(ciclo.fechaCierre) })}</>}</p>}
          <Card className="mt-6 !p-0">
            <h2 className="p-5 pb-2 text-xl font-bold text-vino">{t('paso3.historial')}</h2>
            <DataTable
              etiqueta={t('paso3.historial')}
              columnas={columnasLayouts}
              datos={layouts.data?.items}
              cargando={layouts.isLoading}
              error={layouts.isError ? layouts.error : undefined}
              onReintentar={() => void layouts.refetch()}
              pagina={layouts.data && { page: layouts.data.page, pageSize: layouts.data.pageSize, total: layouts.data.total }}
              onPagina={setPageLayouts}
            />
          </Card>
        </>
      )}

      <Modal abierto={confirmar} onCerrar={() => setConfirmar(false)} ancho="sm" titulo={t('paso2.confirmarTitulo', { anio })} descripcion={t('paso2.confirmarTexto')}
        pie={<><Button variante="secondary" onClick={() => setConfirmar(false)}>{tc('acciones.cancelar')}</Button><Button cargando={cerrar.isPending} onClick={() => cerrar.mutate()}>{t('paso2.cerrar')}</Button></>}>
        {r && <p className="text-sm text-vino">{t('paso2.total')}: <b>{dinero(r.ciclo.interesTotal, true)}</b> · {t('paso2.participantes')}: <b>{r.ciclo.participantes}</b></p>}
      </Modal>
    </>
  )
}
