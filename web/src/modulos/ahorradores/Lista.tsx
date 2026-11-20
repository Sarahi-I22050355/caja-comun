import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdPersonAdd } from 'react-icons/md'
import { useNavigate } from 'react-router-dom'
import { useCaja } from '../../auth/CajaActiva'
import { useSesion } from '../../auth/Sesion'
import { api } from '../../lib/api'
import { dinero, iniciales } from '../../lib/formato'
import { useDebounce } from '../../lib/hooks'
import type { AhorradorLista, EstadoAhorrador, Pagina } from '../../lib/tipos'
import { Button, Card, PageHeader, StatusPill, tonoDe } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'
import { SearchInput, Select, Toggle } from '../../ui/form'
import { FormularioAhorrador } from './Formulario'

const TAMANO = 25

export default function Lista() {
  const { t } = useTranslation('ahorradores')
  const { t: tc } = useTranslation()
  const { puede } = useSesion()
  const { cajaId } = useCaja()
  const navegar = useNavigate()
  const [q, setQ] = useState('')
  const [estado, setEstado] = useState<EstadoAhorrador | ''>('')
  const [orden, setOrden] = useState('nombre')
  const [desc, setDesc] = useState(false)
  const [page, setPage] = useState(1)
  const [alta, setAlta] = useState(false)
  const busqueda = useDebounce(q)

  const consulta = useQuery({
    queryKey: ['ahorradores', cajaId, busqueda, estado, orden, desc, page],
    queryFn: () => api.get<Pagina<AhorradorLista>>('/ahorradores', { cajaId, q: busqueda, estado, orden, desc, page, pageSize: TAMANO }),
    enabled: cajaId !== undefined,
    placeholderData: keepPreviousData,
  })

  const columnas = useMemo<Columnas<AhorradorLista>>(
    () => [
      { header: t('columnas.numero'), accessorKey: 'numEmpleado', meta: { ancho: '120px' } },
      {
        header: t('columnas.nombre'),
        accessorKey: 'nombre',
        cell: ({ row: { original: a } }) => (
          <div className="flex items-center gap-3">
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-tinte text-xs font-bold text-primary-texto">{iniciales(`${a.nombre} ${a.apellidos}`)}</span>
            <span className="font-semibold text-vino">{a.nombre} {a.apellidos}</span>
          </div>
        ),
      },
      { header: t('columnas.departamento'), accessorKey: 'departamento', meta: { ocultoMovil: true } },
      { header: t('columnas.estado'), accessorKey: 'estado', cell: ({ row }) => <StatusPill tono={tonoDe(row.original.estado)}>{tc(`enums.estadoAhorrador.${row.original.estado}`)}</StatusPill> },
      { header: t('columnas.sueldo'), accessorKey: 'sueldoQuincenal', cell: ({ row }) => dinero(row.original.sueldoQuincenal), meta: { derecha: true } },
      { header: t('columnas.ahorro'), accessorKey: 'ahorroTotal', cell: ({ row }) => <span className="font-semibold text-vino">{dinero(row.original.ahorroTotal)}</span>, meta: { derecha: true } },
    ],
    [t, tc],
  )

  const reiniciar = () => { setQ(''); setEstado(''); setPage(1) }
  const filtrado = q !== '' || estado !== ''

  return (
    <>
      <PageHeader
        titulo={t('titulo')}
        descripcion={t('descripcion')}
        acciones={puede('capturar') && <Button icono={<MdPersonAdd />} onClick={() => setAlta(true)}>{t('alta')}</Button>}
      />
      <Card className="!p-0">
        <div className="flex flex-wrap items-end gap-3 p-4">
          <SearchInput valor={q} onCambio={(v) => { setQ(v); setPage(1) }} placeholder={t('buscar')} />
          <Select
            nombre={t('filtros.estado')}
            value={estado}
            onChange={(e) => { setEstado(e.target.value as EstadoAhorrador | ''); setPage(1) }}
            className="!w-auto"
          >
            <option value="">{`${t('filtros.estado')}: ${tc('acciones.todos')}`}</option>
            {(['ACTIVO', 'LICENCIA', 'BAJA'] as const).map((e) => <option key={e} value={e}>{tc(`enums.estadoAhorrador.${e}`)}</option>)}
          </Select>
          <Select nombre={t('filtros.orden')} value={orden} onChange={(e) => { setOrden(e.target.value); setPage(1) }} className="!w-auto">
            {(['nombre', 'numEmpleado', 'ahorro', 'ingreso'] as const).map((o) => <option key={o} value={o}>{`${t('filtros.orden')}: ${t(`ordenes.${o}`)}`}</option>)}
          </Select>
          <label className="flex items-center gap-2 text-sm font-medium text-suave">
            <Toggle activo={desc} onCambio={(v) => { setDesc(v); setPage(1) }} etiqueta={t('filtros.desc')} />
            {t('filtros.desc')}
          </label>
          {filtrado && <Button variante="ghost" pequeno onClick={reiniciar}>{tc('acciones.limpiar')}</Button>}
        </div>
        <DataTable
          etiqueta={t('titulo')}
          columnas={columnas}
          datos={consulta.data?.items}
          cargando={consulta.isLoading}
          error={consulta.isError ? consulta.error : undefined}
          onReintentar={() => void consulta.refetch()}
          pagina={consulta.data && { page: consulta.data.page, pageSize: consulta.data.pageSize, total: consulta.data.total }}
          onPagina={setPage}
          onFila={(a) => navegar(`/ahorradores/${a.id}`)}
        />
      </Card>
      <FormularioAhorrador abierto={alta} onCerrar={() => setAlta(false)} cajaId={cajaId} />
    </>
  )
}
