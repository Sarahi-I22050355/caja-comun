import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdDownload } from 'react-icons/md'
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useCaja } from '../../auth/CajaActiva'
import { ApiError, api, guardar } from '../../lib/api'
import type { Archivo } from '../../lib/api'
import { textoError } from '../../lib/errores'
import { dinero, dineroCorto, fecha, nombreMes, porcentaje } from '../../lib/formato'
import type { Ciclo, Distribucion, EstadoCuenta, EstadoResultados, Movimiento, Pagina, Trabajo } from '../../lib/tipos'
import { AhorradorPicker } from '../../ui/AhorradorPicker'
import type { AhorradorElegido } from '../../ui/AhorradorPicker'
import { Button, Card, EmptyState, ErrorState, KpiCard, PageHeader, Skeleton, cn } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'
import { Select } from '../../ui/form'
import { useToast } from '../../ui/Toast'

type Tab = 'cuenta' | 'resultados' | 'distribucion'
const esperar = (ms: number) => new Promise((r) => setTimeout(r, ms))

/** Pide el reporte a la cola en segundo plano y espera a que termine para descargarlo. */
async function enSegundoPlano(cuerpo: Record<string, unknown>): Promise<Archivo> {
  const job = await api.post<Trabajo>('/reportes/trabajos', cuerpo)
  for (let i = 0; i < 150; i++) {
    const t = await api.get<Trabajo>(`/trabajos/${job.id}`)
    if (t.estado === 'EXITO') return api.archivo(`/trabajos/${job.id}/archivo`)
    if (t.estado === 'ERROR') throw new ApiError(500, t.codigoError ?? 'ERROR_INTERNO', t.mensajeError ?? '')
    await esperar(600)
  }
  throw new ApiError(504, 'TRABAJO_NO_TERMINADO', '')
}

function Exportar({ generar }: { generar: (formato: 'PDF' | 'XLSX') => Promise<Archivo> }) {
  const { t } = useTranslation('reportes')
  const { t: tc } = useTranslation()
  const toast = useToast()
  const m = useMutation({
    mutationFn: generar,
    onSuccess: (a) => { guardar(a); toast.ok(t('exportar.listo')) },
    onError: (e) => toast.error(textoError(tc, e)),
  })
  return (
    <div className="flex flex-wrap gap-2">
      <Button variante="secondary" icono={<MdDownload />} cargando={m.isPending && m.variables === 'PDF'} disabled={m.isPending} onClick={() => m.mutate('PDF')}>{t('exportar.pdf')}</Button>
      <Button variante="secondary" icono={<MdDownload />} cargando={m.isPending && m.variables === 'XLSX'} disabled={m.isPending} onClick={() => m.mutate('XLSX')}>{t('exportar.excel')}</Button>
    </div>
  )
}

function PanelCuenta() {
  const { t } = useTranslation('reportes')
  const { t: tc } = useTranslation()
  const { cajaId } = useCaja()
  const [ahorrador, setAhorrador] = useState<AhorradorElegido | null>(null)
  const actual = new Date().getFullYear()
  const [anio, setAnio] = useState(2024)
  const q = useQuery({ queryKey: ['reporte-cuenta', ahorrador?.id, anio], queryFn: () => api.get<EstadoCuenta>(`/reportes/estado-cuenta/${ahorrador!.id}`, { anio }), enabled: !!ahorrador })
  const columnas = useMemo<Columnas<Movimiento>>(() => [
    { header: t('cuenta.fecha'), accessorKey: 'fecha', cell: ({ row }) => fecha(row.original.fecha), meta: { ancho: '120px' } },
    { header: t('cuenta.concepto'), accessorKey: 'tipo', cell: ({ row }) => tc(`enums.tipoMovimiento.${row.original.tipo}`) },
    { header: t('cuenta.monto'), accessorKey: 'monto', cell: ({ row }) => dinero(row.original.monto), meta: { derecha: true } },
    { header: t('cuenta.saldo'), accessorKey: 'saldoAhorro', cell: ({ row }) => <span className="font-semibold text-vino">{dinero(row.original.saldoAhorro)}</span>, meta: { derecha: true } },
  ], [t, tc])
  const e = q.data
  return (
    <div className="flex flex-col gap-6">
      <Card className="grid items-end gap-4 md:grid-cols-3">
        <AhorradorPicker id="rep-ahorrador" etiqueta={t('ahorrador')} cajaId={cajaId} valor={ahorrador} onElegir={setAhorrador} />
        <Select etiqueta={t('anio')} value={anio} onChange={(ev) => setAnio(Number(ev.target.value))} opciones={Array.from({ length: Math.max(actual, 2024) - 2022 }, (_, i) => Math.max(actual, 2024) - i).map((a) => ({ valor: a, texto: String(a) }))} />
        {ahorrador && <Exportar generar={(f) => api.archivo(`/reportes/estado-cuenta/${ahorrador.id}`, { anio, formato: f })} />}
      </Card>
      {!ahorrador ? <Card><EmptyState detalle={t('cuenta.vacio')} /></Card> : q.isError ? <Card><ErrorState detalle={textoError(tc, q.error)} onReintentar={() => void q.refetch()} /></Card> : (
        <>
          <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-6">
            <KpiCard titulo={t('cuenta.inicial')} cargando={q.isLoading} valor={e && dinero(e.saldoInicial)} />
            <KpiCard titulo={t('cuenta.aportaciones')} cargando={q.isLoading} valor={e && dinero(e.totales.aportacionesOrdinarias + e.totales.aportacionesVoluntarias)} />
            <KpiCard titulo={t('cuenta.liquidaciones')} cargando={q.isLoading} valor={e && dinero(e.totales.liquidaciones)} />
            <KpiCard titulo={t('cuenta.prestamos')} cargando={q.isLoading} valor={e && dinero(e.totales.desembolsos)} />
            <KpiCard titulo={t('cuenta.rendimiento')} cargando={q.isLoading} valor={e && dinero(e.totales.rendimientoAnual)} />
            <KpiCard titulo={t('cuenta.final')} cargando={q.isLoading} valor={e && dinero(e.saldoFinal)} />
          </section>
          <Card className="!p-0"><DataTable etiqueta={t('tabs.cuenta')} columnas={columnas} datos={e?.movimientos} cargando={q.isLoading} virtual alto={420} /></Card>
        </>
      )}
    </div>
  )
}

function PanelResultados() {
  const { t } = useTranslation('reportes')
  const { t: tc } = useTranslation()
  const { cajaId } = useCaja()
  const [anio, setAnio] = useState(2024)
  const actual = new Date().getFullYear()
  const q = useQuery({ queryKey: ['reporte-resultados', cajaId, anio], queryFn: () => api.get<EstadoResultados>('/reportes/estado-resultados', { cajaId, anio }), enabled: cajaId !== undefined })
  const e = q.data
  const datos = useMemo(() => (e?.meses ?? []).map((m) => ({ mes: nombreMes(m.mes), cobrados: m.cobrados, devengados: m.devengados })),
    // El nombre del mes depende del idioma.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [e?.meses, tc])
  return (
    <div className="flex flex-col gap-6">
      <Card className="grid items-end gap-4 md:grid-cols-3">
        <Select etiqueta={t('anio')} value={anio} onChange={(ev) => setAnio(Number(ev.target.value))} opciones={Array.from({ length: Math.max(actual, 2024) - 2022 }, (_, i) => Math.max(actual, 2024) - i).map((a) => ({ valor: a, texto: String(a) }))} />
        <p className="text-xs text-suave md:col-span-1">{t('resultados.nota')}</p>
        {cajaId !== undefined && <Exportar generar={(f) => api.archivo('/reportes/estado-resultados', { cajaId, anio, formato: f })} />}
      </Card>
      {q.isError ? <Card><ErrorState detalle={textoError(tc, q.error)} onReintentar={() => void q.refetch()} /></Card> : (
        <>
          <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
            <KpiCard titulo={t('resultados.cobrados')} cargando={q.isLoading} valor={e && dinero(e.interesesCobrados)} />
            <KpiCard titulo={t('resultados.devengados')} cargando={q.isLoading} valor={e && dinero(e.interesesDevengados)} />
            <KpiCard titulo={t('resultados.utilidad')} cargando={q.isLoading} valor={e && dinero(e.utilidadADistribuir, true)} detalle={e && (e.estadoCiclo ? t('resultados.ciclo', { estado: tc(`enums.estadoCiclo.${e.estadoCiclo}`) }) : t('resultados.sinCiclo'))} />
            <KpiCard titulo={t('resultados.distribuido')} cargando={q.isLoading} valor={e && dinero(e.interesDistribuido)} />
            <KpiCard titulo={t('resultados.cartera')} cargando={q.isLoading} valor={e && dinero(e.saldoCartera)} detalle={e && `${e.prestamosVigentes}`} />
          </section>
          <Card>
            <h2 className="mb-3 text-lg font-bold text-vino">{t('resultados.grafica')}</h2>
            {q.isLoading ? <Skeleton className="h-64 w-full" /> : (
              <div role="img" aria-label={t('resultados.grafica')} className="h-64 w-full">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={datos} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" stroke="#F8BBD0" vertical={false} />
                    <XAxis dataKey="mes" tickLine={false} axisLine={false} tick={{ fill: '#5B3F43', fontSize: 12 }} />
                    <YAxis tickFormatter={dineroCorto} tickLine={false} axisLine={false} width={60} tick={{ fill: '#5B3F43', fontSize: 12 }} />
                    <Tooltip formatter={(v) => dinero(Number(v), true)} cursor={{ fill: '#FCE4EC' }} />
                    <Legend iconType="circle" />
                    <Bar dataKey="cobrados" name={t('resultados.cobrados')} fill="#E91E63" radius={[6, 6, 0, 0]} />
                    <Bar dataKey="devengados" name={t('resultados.devengados')} fill="#F48FB1" radius={[6, 6, 0, 0]} />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            )}
          </Card>
        </>
      )}
    </div>
  )
}

function PanelDistribucion() {
  const { t } = useTranslation('reportes')
  const { t: tc } = useTranslation()
  const { cajaId } = useCaja()
  const [cicloId, setCicloId] = useState<number | null>(null)
  const [page, setPage] = useState(1)
  const ciclos = useQuery({ queryKey: ['ciclos', cajaId], queryFn: () => api.get<Ciclo[]>('/ciclos', { cajaId }), enabled: cajaId !== undefined })
  const conDistribucion = (ciclos.data ?? []).filter((c) => c.participantes > 0)
  const elegido = cicloId ?? conDistribucion[0]?.id ?? null
  const ciclo = conDistribucion.find((c) => c.id === elegido)
  const q = useQuery({
    queryKey: ['reporte-distribucion', elegido, page],
    queryFn: () => api.get<Pagina<Distribucion>>('/reportes/distribucion', { cicloId: elegido, page, pageSize: 25 }),
    enabled: elegido !== null,
    placeholderData: keepPreviousData,
  })
  const columnas = useMemo<Columnas<Distribucion>>(() => [
    { header: '#', accessorKey: 'numEmpleado', meta: { ancho: '120px' } },
    { header: t('ahorrador'), accessorKey: 'nombre', cell: ({ row }) => <span className="font-semibold text-vino">{row.original.nombre}</span> },
    { header: '%', accessorKey: 'participacion', cell: ({ row }) => porcentaje(row.original.participacion, 4), meta: { derecha: true } },
    { header: t('distribucion.total'), accessorKey: 'interesAsignado', cell: ({ row }) => <span className="font-extrabold text-vino">{dinero(row.original.interesAsignado)}</span>, meta: { derecha: true } },
    { header: t('distribucion.liquidado'), accessorKey: 'ahorroLiquidado', cell: ({ row }) => dinero(row.original.ahorroLiquidado), meta: { derecha: true } },
    { header: t('distribucion.retenido'), accessorKey: 'ahorroRetenido', cell: ({ row }) => (row.original.ahorroRetenido > 0 ? dinero(row.original.ahorroRetenido) : '—'), meta: { derecha: true } },
  ], [t])
  return (
    <div className="flex flex-col gap-6">
      <Card className="grid items-end gap-4 md:grid-cols-3">
        <Select etiqueta={t('ciclo')} value={elegido ?? ''} onChange={(e) => { setCicloId(Number(e.target.value)); setPage(1) }} opciones={conDistribucion.map((c) => ({ valor: c.id, texto: `${c.anio} · ${tc(`enums.estadoCiclo.${c.estado}`)}` }))} />
        <p className="text-xs text-suave">{t('segundoPlano')}</p>
        {elegido !== null && <Exportar generar={(f) => enSegundoPlano({ tipo: 'DISTRIBUCION', formato: f, cicloId: elegido })} />}
      </Card>
      {elegido === null && !ciclos.isLoading ? <Card><EmptyState detalle={t('distribucion.vacio')} /></Card> : (
        <>
          {ciclo && (
            <section className="grid gap-4 sm:grid-cols-3">
              <KpiCard titulo={t('distribucion.total')} valor={dinero(ciclo.interesTotal, true)} />
              <KpiCard titulo={t('distribucion.participantes')} valor={String(ciclo.participantes)} />
              <KpiCard titulo={t('ciclo')} valor={String(ciclo.anio)} />
            </section>
          )}
          <Card className="!p-0">
            <DataTable etiqueta={t('tabs.distribucion')} columnas={columnas} datos={q.data?.items} cargando={q.isLoading || ciclos.isLoading} error={q.isError ? q.error : undefined} onReintentar={() => void q.refetch()}
              pagina={q.data && { page: q.data.page, pageSize: q.data.pageSize, total: q.data.total }} onPagina={setPage} />
          </Card>
        </>
      )}
    </div>
  )
}

export default function Pagina() {
  const { t } = useTranslation('reportes')
  const [tab, setTab] = useState<Tab>('cuenta')
  const tabs: Tab[] = ['cuenta', 'resultados', 'distribucion']
  return (
    <>
      <PageHeader titulo={t('titulo')} descripcion={t('descripcion')} />
      <div role="tablist" aria-label={t('titulo')} className="mb-6 flex gap-1 overflow-x-auto rounded-full border border-borde bg-white p-1">
        {tabs.map((x) => (
          <button
            key={x}
            role="tab"
            type="button"
            id={`tab-${x}`}
            aria-selected={tab === x}
            aria-controls={`panel-${x}`}
            onClick={() => setTab(x)}
            className={cn('whitespace-nowrap rounded-full px-4 py-2 text-sm font-semibold transition focus-visible:outline-none focus-visible:shadow-foco', tab === x ? 'bg-primary-solido text-white' : 'text-suave hover:bg-tinte')}
          >
            {t(`tabs.${x}`)}
          </button>
        ))}
      </div>
      <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
        {tab === 'cuenta' && <PanelCuenta />}
        {tab === 'resultados' && <PanelResultados />}
        {tab === 'distribucion' && <PanelDistribucion />}
      </div>
    </>
  )
}

