import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdHistory, MdPlayArrow, MdSchedule } from 'react-icons/md'
import { useSesion } from '../../auth/Sesion'
import { api } from '../../lib/api'
import { textoCodigo, textoError } from '../../lib/errores'
import { fechaHora, hora as horaCorta } from '../../lib/formato'
import { useTrabajo } from '../../lib/trabajos'
import type { Ejecucion, Pagina, Tarea, Trabajo } from '../../lib/tipos'
import { Button, Card, ErrorState, PageHeader, Skeleton, StatusPill, tonoDe } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'
import { Input, Select, Toggle } from '../../ui/form'
import { Modal } from '../../ui/Modal'
import { useToast } from '../../ui/Toast'

type Tipada = Tarea['frecuencia']

function Historial({ tarea, onCerrar }: { tarea: Tarea; onCerrar: () => void }) {
  const { t } = useTranslation('tareas')
  const { t: tc } = useTranslation()
  const [page, setPage] = useState(1)
  const q = useQuery({ queryKey: ['tarea-historial', tarea.id, page], queryFn: () => api.get<Pagina<Ejecucion>>(`/tareas/${tarea.id}/historial`, { page, pageSize: 8 }), placeholderData: keepPreviousData })
  const columnas = useMemo<Columnas<Ejecucion>>(() => [
    { header: t('columnas.inicio'), accessorKey: 'inicio', cell: ({ row }) => fechaHora(row.original.inicio) },
    { header: t('columnas.fin'), accessorKey: 'fin', cell: ({ row }) => fechaHora(row.original.fin) },
    { header: t('columnas.estado'), accessorKey: 'estado', cell: ({ row }) => <StatusPill tono={tonoDe(row.original.estado)}>{tc(`enums.estadoEjecucion.${row.original.estado}`)}</StatusPill> },
    { header: t('columnas.resumen'), accessorKey: 'resumen', cell: ({ row }) => <span className="text-xs">{row.original.resumen ?? '—'}</span> },
  ], [t, tc])
  return (
    <Modal abierto onCerrar={onCerrar} titulo={t('historial')} descripcion={t(`nombres.${tarea.clave}`, { defaultValue: tarea.nombre })} ancho="xl" pie={<Button onClick={onCerrar}>{tc('acciones.cerrar')}</Button>}>
      <DataTable etiqueta={t('historial')} columnas={columnas} datos={q.data?.items} cargando={q.isLoading} error={q.isError ? q.error : undefined} onReintentar={() => void q.refetch()}
        pagina={q.data && { page: q.data.page, pageSize: q.data.pageSize, total: q.data.total }} onPagina={setPage} />
    </Modal>
  )
}

function TarjetaTarea({ tarea }: { tarea: Tarea }) {
  const { t } = useTranslation('tareas')
  const { t: tc } = useTranslation()
  const { puede } = useSesion()
  const cache = useQueryClient()
  const toast = useToast()
  const editable = puede('administrar')
  const [frecuencia, setFrecuencia] = useState<Tipada>(tarea.frecuencia)
  const [hora, setHora] = useState(horaCorta(tarea.hora))
  const [historial, setHistorial] = useState(false)
  const [trabajoId, setTrabajoId] = useState<string | null>(null)
  const sucio = frecuencia !== tarea.frecuencia || hora !== horaCorta(tarea.hora)
  useEffect(() => { setFrecuencia(tarea.frecuencia); setHora(horaCorta(tarea.hora)) }, [tarea.frecuencia, tarea.hora])

  const guardar = useMutation({
    mutationFn: (v: { frecuencia: Tipada; hora: string; activa: boolean }) => api.put<Tarea>(`/tareas/${tarea.id}`, { frecuencia: v.frecuencia, hora: `${v.hora}:00`, activa: v.activa }),
    // El interruptor es optimista: se refleja al instante y se revierte si el servidor lo rechaza.
    onMutate: async (v) => {
      await cache.cancelQueries({ queryKey: ['tareas'] })
      const previo = cache.getQueryData<Tarea[]>(['tareas'])
      cache.setQueryData<Tarea[]>(['tareas'], (l) => l?.map((x) => (x.id === tarea.id ? { ...x, activa: v.activa } : x)))
      return { previo }
    },
    onError: (e, _v, ctx) => { cache.setQueryData(['tareas'], ctx?.previo); toast.error(textoError(tc, e)) },
    onSuccess: () => toast.ok(t('guardada')),
    onSettled: () => void cache.invalidateQueries({ queryKey: ['tareas'] }),
  })

  const ejecutar = useMutation({
    mutationFn: () => api.post<Trabajo>(`/tareas/${tarea.id}/ejecutar`),
    onSuccess: (j) => setTrabajoId(j.id),
    onError: (e) => toast.error(textoError(tc, e)),
  })
  const trabajo = useTrabajo(trabajoId)
  useEffect(() => {
    const tr = trabajo.data
    if (!tr) return
    if (tr.estado === 'EXITO') toast.ok(t('ejecutada'))
    if (tr.estado === 'ERROR') toast.error(textoCodigo(tc, tr.codigoError ?? 'generico'))
    if (tr.estado === 'EXITO' || tr.estado === 'ERROR') { setTrabajoId(null); void cache.invalidateQueries({ queryKey: ['tareas'] }); void cache.invalidateQueries({ queryKey: ['tarea-historial'] }); void cache.invalidateQueries({ queryKey: ['dashboard'] }) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [trabajo.data?.estado])

  return (
    <Card className="flex flex-col gap-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-control bg-tinte text-2xl text-primary-texto"><MdSchedule aria-hidden /></span>
          <div className="min-w-0">
            <h2 className="text-lg font-bold text-vino">{t(`nombres.${tarea.clave}`, { defaultValue: tarea.nombre })}</h2>
            <p className="text-xs text-suave">{tarea.clave}</p>
          </div>
        </div>
        <label className="flex items-center gap-2 text-sm font-medium text-vino">
          <Toggle activo={tarea.activa} deshabilitado={!editable || guardar.isPending} onCambio={(v) => guardar.mutate({ frecuencia: tarea.frecuencia, hora: horaCorta(tarea.hora), activa: v })} etiqueta={t('activa')} />
          {tarea.activa ? t('activa') : t('pausada')}
        </label>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <Select etiqueta={t('frecuencia')} disabled={!editable} value={frecuencia} onChange={(e) => setFrecuencia(e.target.value as Tipada)} opciones={(['DIARIA', 'QUINCENAL', 'MENSUAL'] as const).map((f) => ({ valor: f, texto: tc(`enums.frecuenciaTarea.${f}`) }))} />
        <Input etiqueta={t('hora')} type="time" disabled={!editable} value={hora} onChange={(e) => setHora(e.target.value)} />
      </div>
      <p className="-mt-3 text-xs text-suave">{t(`ayudaFrecuencia.${frecuencia}`)}</p>

      <dl className="grid gap-3 text-sm sm:grid-cols-2">
        <div className="rounded-control bg-lienzo p-3"><dt className="text-xs text-suave">{t('proxima')}</dt><dd className="font-semibold text-vino">{tarea.activa ? fechaHora(tarea.proximaEjecucion) : t('pausada')}</dd></div>
        <div className="rounded-control bg-lienzo p-3">
          <dt className="text-xs text-suave">{t('ultima')}</dt>
          <dd className="flex flex-wrap items-center gap-2 font-semibold text-vino">{tarea.ultimaEjecucion ? fechaHora(tarea.ultimaEjecucion) : t('nunca')}{tarea.ultimoEstado && <StatusPill tono={tonoDe(tarea.ultimoEstado)}>{tc(`enums.estadoEjecucion.${tarea.ultimoEstado}`)}</StatusPill>}</dd>
        </div>
      </dl>
      {tarea.ultimoResumen && <p className="rounded-control bg-tinte/60 px-3 py-2 text-xs text-suave">{tarea.ultimoResumen}</p>}

      <div className="flex flex-wrap gap-2">
        {editable && <Button disabled={!sucio} cargando={guardar.isPending && sucio} onClick={() => guardar.mutate({ frecuencia, hora, activa: tarea.activa })}>{t('guardar')}</Button>}
        {editable && <Button variante="secondary" icono={<MdPlayArrow />} cargando={ejecutar.isPending || !!trabajoId} onClick={() => ejecutar.mutate()}>{trabajoId ? t('ejecutando') : t('ejecutar')}</Button>}
        <Button variante="ghost" icono={<MdHistory />} onClick={() => setHistorial(true)}>{t('verHistorial')}</Button>
      </div>
      {historial && <Historial tarea={tarea} onCerrar={() => setHistorial(false)} />}
    </Card>
  )
}

export default function Pagina() {
  const { t } = useTranslation('tareas')
  const { t: tc } = useTranslation()
  const q = useQuery({ queryKey: ['tareas'], queryFn: () => api.get<Tarea[]>('/tareas') })
  return (
    <>
      <PageHeader titulo={t('titulo')} descripcion={t('descripcion')} />
      {q.isError ? <Card><ErrorState detalle={textoError(tc, q.error)} onReintentar={() => void q.refetch()} /></Card> : q.isLoading ? <Skeleton className="h-72 w-full max-w-3xl" /> : (
        <div className="grid max-w-5xl gap-6 lg:grid-cols-2">{q.data?.map((x) => <TarjetaTarea key={x.id} tarea={x} />)}</div>
      )}
    </>
  )
}
