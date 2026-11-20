import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdEdit } from 'react-icons/md'
import { useCaja } from '../../auth/CajaActiva'
import { useSesion } from '../../auth/Sesion'
import { api } from '../../lib/api'
import { codigosPorCampo, textoCodigo, textoError } from '../../lib/errores'
import { dinero, entero_, fecha } from '../../lib/formato'
import { useDebounce } from '../../lib/hooks'
import type { Pagina, Voluntario } from '../../lib/tipos'
import { Button, Card, KpiCard, PageHeader, StatusPill, tonoDe } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'
import { Input, SearchInput, Select, Toggle } from '../../ui/form'
import { Modal } from '../../ui/Modal'
import { useToast } from '../../ui/Toast'

function ModalVoluntario({ fila, onCerrar }: { fila: Voluntario; onCerrar: () => void }) {
  const { t } = useTranslation('voluntario')
  const { t: tc } = useTranslation()
  const cache = useQueryClient()
  const toast = useToast()
  const [activo, setActivo] = useState(fila.activo)
  const [cuota, setCuota] = useState(String(fila.cuotaVoluntaria || ''))
  const [desde, setDesde] = useState('')
  const [errores, setErrores] = useState<Record<string, string>>({})

  const guardar = useMutation({
    mutationFn: () => api.put(`/ahorro-voluntario/${fila.ahorradorId}`, { activo, cuotaVoluntaria: activo ? Number(cuota) : 0, vigenteDesde: desde || null }),
    onSuccess: () => {
      for (const k of ['voluntario', 'voluntario-resumen', 'ahorrador', 'descuentos']) void cache.invalidateQueries({ queryKey: [k] })
      toast.ok(t('guardado'))
      onCerrar()
    },
    onError: (e) => { setErrores(codigosPorCampo(e, { cuotaVoluntaria: 'cuota', vigenteDesde: 'desde' })); toast.error(textoError(tc, e)) },
  })
  const msg = (c?: string) => (c ? textoCodigo(tc, c) : undefined)

  return (
    <Modal abierto onCerrar={onCerrar} titulo={t('editar', { nombre: fila.nombre })} descripcion={fila.numEmpleado}
      pie={<><Button variante="secondary" onClick={onCerrar}>{tc('acciones.cancelar')}</Button><Button cargando={guardar.isPending} onClick={() => guardar.mutate()}>{tc('acciones.guardar')}</Button></>}>
      <div className="grid gap-4">
        <div className="flex items-center gap-3"><Toggle activo={activo} onCambio={setActivo} etiqueta={t('campos.activo')} /><span className="text-sm font-medium text-vino">{t('campos.activo')}</span></div>
        {activo && <Input etiqueta={t('campos.cuota')} type="number" step="0.01" inputMode="decimal" value={cuota} onChange={(e) => setCuota(e.target.value)} error={msg(errores.cuota)} />}
        <Input etiqueta={t('campos.desde')} ayuda={t('campos.desdeAyuda')} type="date" value={desde} onChange={(e) => setDesde(e.target.value)} error={msg(errores.desde)} />
      </div>
    </Modal>
  )
}

export default function Pagina() {
  const { t } = useTranslation('voluntario')
  const { t: tc } = useTranslation()
  const { puede } = useSesion()
  const { cajaId } = useCaja()
  const [q, setQ] = useState('')
  const [filtro, setFiltro] = useState('')
  const [page, setPage] = useState(1)
  const [editando, setEditando] = useState<Voluntario | null>(null)
  const busqueda = useDebounce(q)

  const resumen = useQuery({ queryKey: ['voluntario-resumen', cajaId], queryFn: () => api.get<{ ahorradoresActivos: number; cuotaPorPeriodo: number; acumulado: number }>('/ahorro-voluntario/resumen', { cajaId }), enabled: cajaId !== undefined })
  const lista = useQuery({
    queryKey: ['voluntario', cajaId, busqueda, filtro, page],
    queryFn: () => api.get<Pagina<Voluntario>>('/ahorro-voluntario', { cajaId, q: busqueda, activo: filtro === '' ? undefined : filtro === 'a', page, pageSize: 25 }),
    enabled: cajaId !== undefined,
    placeholderData: keepPreviousData,
  })

  const columnas = useMemo<Columnas<Voluntario>>(
    () => [
      { header: t('columnas.empleado'), accessorKey: 'numEmpleado', meta: { ancho: '120px' } },
      { header: t('columnas.nombre'), accessorKey: 'nombre', cell: ({ row }) => <span className="font-semibold text-vino">{row.original.nombre}</span> },
      { header: t('columnas.estado'), accessorKey: 'estado', cell: ({ row }) => <StatusPill tono={tonoDe(row.original.estado)}>{tc(`enums.estadoAhorrador.${row.original.estado}`)}</StatusPill> },
      { header: t('columnas.activo'), accessorKey: 'activo', cell: ({ row }) => <StatusPill tono={row.original.activo ? 'ok' : 'pausado'}>{row.original.activo ? t('activo') : t('inactivo')}</StatusPill> },
      { header: t('columnas.cuota'), accessorKey: 'cuotaVoluntaria', cell: ({ row }) => (row.original.activo ? dinero(row.original.cuotaVoluntaria) : '—'), meta: { derecha: true } },
      { header: t('columnas.desde'), accessorKey: 'vigenteDesde', cell: ({ row }) => fecha(row.original.vigenteDesde), meta: { ocultoMovil: true } },
      { header: t('columnas.acumulado'), accessorKey: 'acumuladoVoluntario', cell: ({ row }) => <span className="font-semibold text-vino">{dinero(row.original.acumuladoVoluntario)}</span>, meta: { derecha: true } },
      {
        header: '', id: 'acciones', meta: { derecha: true, ancho: '70px' },
        cell: ({ row }) => puede('capturar') && (
          <button type="button" aria-label={tc('acciones.editar')} onClick={(e) => { e.stopPropagation(); setEditando(row.original) }} className="rounded-full p-1.5 text-lg text-primary-texto hover:bg-tinte"><MdEdit aria-hidden /></button>
        ),
      },
    ],
    [t, tc, puede],
  )

  const r = resumen.data
  return (
    <>
      <PageHeader titulo={t('titulo')} descripcion={t('descripcion')} />
      <section className="grid gap-4 sm:grid-cols-3">
        <KpiCard titulo={t('kpi.activos')} cargando={resumen.isLoading} valor={r && entero_(r.ahorradoresActivos)} />
        <KpiCard titulo={t('kpi.cuota')} cargando={resumen.isLoading} valor={r && dinero(r.cuotaPorPeriodo)} />
        <KpiCard titulo={t('kpi.acumulado')} cargando={resumen.isLoading} valor={r && dinero(r.acumulado, true)} />
      </section>
      <Card className="mt-6 !p-0">
        <div className="flex flex-wrap items-center gap-3 p-4">
          <SearchInput valor={q} onCambio={(v) => { setQ(v); setPage(1) }} placeholder={t('buscar')} />
          <Select nombre={t('filtro.todos')} value={filtro} onChange={(e) => { setFiltro(e.target.value); setPage(1) }} className="!w-auto">
            <option value="">{t('filtro.todos')}</option>
            <option value="a">{t('filtro.activos')}</option>
            <option value="s">{t('filtro.sin')}</option>
          </Select>
        </div>
        <DataTable
          etiqueta={t('titulo')}
          columnas={columnas}
          datos={lista.data?.items}
          cargando={lista.isLoading}
          error={lista.isError ? lista.error : undefined}
          onReintentar={() => void lista.refetch()}
          pagina={lista.data && { page: lista.data.page, pageSize: lista.data.pageSize, total: lista.data.total }}
          onPagina={setPage}
        />
      </Card>
      {editando && <ModalVoluntario key={editando.ahorradorId} fila={editando} onCerrar={() => setEditando(null)} />}
    </>
  )
}
