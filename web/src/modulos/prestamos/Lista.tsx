import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdAdd, MdCheck, MdClose } from 'react-icons/md'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useCaja } from '../../auth/CajaActiva'
import { useSesion } from '../../auth/Sesion'
import { api } from '../../lib/api'
import { textoError } from '../../lib/errores'
import { dinero, entero_, fecha } from '../../lib/formato'
import { useDebounce } from '../../lib/hooks'
import type { CarteraResumen, EstadoPrestamo, Pagina, Prestamo } from '../../lib/tipos'
import { Button, Card, KpiCard, PageHeader, StatusPill, tonoDe } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'
import { SearchInput, Select } from '../../ui/form'
import { Modal } from '../../ui/Modal'
import { useToast } from '../../ui/Toast'

export default function Lista() {
  const { t } = useTranslation('prestamos')
  const { t: tc } = useTranslation()
  const { puede } = useSesion()
  const { cajaId } = useCaja()
  const navegar = useNavigate()
  const cache = useQueryClient()
  const toast = useToast()
  const [params] = useSearchParams()
  const [q, setQ] = useState(params.get('q') ?? '')
  const [estado, setEstado] = useState<EstadoPrestamo | ''>('')
  const [page, setPage] = useState(1)
  const [accion, setAccion] = useState<{ tipo: 'autorizar' | 'rechazar'; prestamo: Prestamo } | null>(null)
  const busqueda = useDebounce(q)

  const resumen = useQuery({ queryKey: ['prestamos-resumen', cajaId], queryFn: () => api.get<CarteraResumen>('/prestamos/resumen', { cajaId }), enabled: cajaId !== undefined })
  const lista = useQuery({
    queryKey: ['prestamos', cajaId, busqueda, estado, page],
    queryFn: () => api.get<Pagina<Prestamo>>('/prestamos', { cajaId, q: busqueda, estado, page, pageSize: 25 }),
    enabled: cajaId !== undefined,
    placeholderData: keepPreviousData,
  })

  const resolver = useMutation({
    mutationFn: ({ tipo, prestamo }: NonNullable<typeof accion>) => api.post(`/prestamos/${prestamo.id}/${tipo}`),
    onSuccess: (_r, v) => {
      toast.ok(v.tipo === 'autorizar' ? t('autorizado') : t('rechazado'))
      setAccion(null)
      for (const k of ['prestamos', 'prestamos-resumen', 'prestamo', 'dashboard', 'ahorrador']) void cache.invalidateQueries({ queryKey: [k] })
    },
    onError: (e) => { setAccion(null); toast.error(textoError(tc, e)) },
  })

  const columnas = useMemo<Columnas<Prestamo>>(
    () => [
      { header: t('columnas.folio'), accessorKey: 'folio', cell: ({ row }) => <span className="font-bold text-primary-texto">{row.original.folio}</span> },
      { header: t('columnas.ahorrador'), accessorKey: 'ahorrador', cell: ({ row }) => <><span className="block font-semibold text-vino">{row.original.ahorrador}</span><span className="text-xs text-suave">{row.original.numEmpleado}</span></> },
      { header: t('columnas.monto'), accessorKey: 'monto', cell: ({ row }) => dinero(row.original.monto), meta: { derecha: true } },
      { header: t('columnas.plazo'), accessorKey: 'plazoQuincenas', cell: ({ row }) => t('plazoValor', { n: row.original.plazoQuincenas }), meta: { ocultoMovil: true } },
      { header: t('columnas.garantia'), accessorKey: 'garantia', cell: ({ row }) => tc(`enums.garantia.${row.original.garantia}`) },
      { header: t('columnas.estado'), accessorKey: 'estado', cell: ({ row }) => <StatusPill tono={tonoDe(row.original.estado)}>{tc(`enums.estadoPrestamo.${row.original.estado}`)}</StatusPill> },
      { header: t('columnas.saldo'), accessorKey: 'saldoVigente', cell: ({ row }) => <span className="font-semibold text-vino">{dinero(row.original.saldoVigente)}</span>, meta: { derecha: true } },
      { header: t('columnas.solicitud'), accessorKey: 'fechaSolicitud', cell: ({ row }) => fecha(row.original.fechaSolicitud), meta: { ocultoMovil: true } },
      {
        header: t('columnas.acciones'), id: 'acciones', meta: { derecha: true },
        cell: ({ row }) => puede('administrar') && row.original.estado === 'PENDIENTE' && (
          <div className="flex justify-end gap-1.5" onClick={(e) => e.stopPropagation()}>
            <Button pequeno icono={<MdCheck />} onClick={() => setAccion({ tipo: 'autorizar', prestamo: row.original })}>{t('autorizar')}</Button>
            <Button pequeno variante="secondary" icono={<MdClose />} onClick={() => setAccion({ tipo: 'rechazar', prestamo: row.original })}>{t('rechazar')}</Button>
          </div>
        ),
      },
    ],
    [t, tc, puede],
  )

  const r = resumen.data
  return (
    <>
      <PageHeader titulo={t('titulo')} descripcion={t('descripcion')} acciones={puede('capturar') && <Button icono={<MdAdd />} onClick={() => navegar('/prestamos/nuevo')}>{t('nuevo')}</Button>} />
      <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
        <KpiCard titulo={t('kpi.vigentes')} cargando={resumen.isLoading} valor={r && entero_(r.vigentes)} />
        <KpiCard titulo={t('kpi.saldo')} cargando={resumen.isLoading} valor={r && dinero(r.saldoVigente, true)} />
        <KpiCard titulo={t('kpi.pendientes')} cargando={resumen.isLoading} valor={r && entero_(r.pendientes)} />
        <KpiCard titulo={t('kpi.colocado')} cargando={resumen.isLoading} valor={r && dinero(r.capitalColocado)} />
        <KpiCard titulo={t('kpi.interes')} cargando={resumen.isLoading} valor={r && dinero(r.interesCobrado)} />
      </section>
      <Card className="mt-6 !p-0">
        <div className="flex flex-wrap items-center gap-3 p-4">
          <SearchInput valor={q} onCambio={(v) => { setQ(v); setPage(1) }} placeholder={t('buscar')} />
          <Select nombre={t('filtroEstado')} value={estado} onChange={(e) => { setEstado(e.target.value as EstadoPrestamo | ''); setPage(1) }} className="!w-auto">
            <option value="">{`${t('filtroEstado')}: ${tc('acciones.todos')}`}</option>
            {(['PENDIENTE', 'VIGENTE', 'LIQUIDADO', 'RECHAZADO'] as const).map((e) => <option key={e} value={e}>{tc(`enums.estadoPrestamo.${e}`)}</option>)}
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
          onFila={(p) => navegar(`/prestamos/${p.id}`)}
        />
      </Card>
      <Modal
        abierto={!!accion}
        onCerrar={() => setAccion(null)}
        ancho="sm"
        titulo={accion ? t(accion.tipo === 'autorizar' ? 'confirmarAutorizar.titulo' : 'confirmarRechazar.titulo', { folio: accion.prestamo.folio }) : ''}
        descripcion={accion ? t(accion.tipo === 'autorizar' ? 'confirmarAutorizar.texto' : 'confirmarRechazar.texto', { monto: dinero(accion.prestamo.monto) }) : undefined}
        pie={<><Button variante="secondary" onClick={() => setAccion(null)}>{tc('acciones.cancelar')}</Button><Button variante={accion?.tipo === 'rechazar' ? 'destructive' : 'primary'} cargando={resolver.isPending} onClick={() => accion && resolver.mutate(accion)}>{tc('acciones.confirmar')}</Button></>}
      >
        {accion && <p className="text-sm text-vino"><b>{accion.prestamo.ahorrador}</b> · {dinero(accion.prestamo.monto, true)} · {t('plazoValor', { n: accion.prestamo.plazoQuincenas })}</p>}
      </Modal>
    </>
  )
}
