import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdArrowBack, MdDownload, MdEdit, MdRequestQuote, MdSummarize } from 'react-icons/md'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useSesion } from '../../auth/Sesion'
import { api, guardar } from '../../lib/api'
import { textoError } from '../../lib/errores'
import { dinero, fecha } from '../../lib/formato'
import type { AhorradorDetalle, EstadoCuenta, Movimiento, Pagina, TipoMovimiento } from '../../lib/tipos'
import { Button, Card, ErrorState, KpiCard, PageHeader, Skeleton, StatusPill, tonoDe } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'
import { Input, Select, Toggle } from '../../ui/form'
import { Modal } from '../../ui/Modal'
import { useToast } from '../../ui/Toast'
import { FormularioAhorrador } from './Formulario'

const TIPOS: TipoMovimiento[] = ['AHORRO_ORDINARIO', 'AHORRO_VOLUNTARIO', 'DESEMBOLSO_PRESTAMO', 'ABONO_CAPITAL', 'ABONO_INTERES', 'INTERES_ANUAL', 'LIQUIDACION_CICLO']

function ModalEstadoCuenta({ ahorrador, abierto, onCerrar }: { ahorrador: AhorradorDetalle; abierto: boolean; onCerrar: () => void }) {
  const { t } = useTranslation('ahorradores')
  const { t: tc } = useTranslation()
  const toast = useToast()
  const actual = new Date().getFullYear()
  const desde = Math.min(Number(ahorrador.fechaIngreso.slice(0, 4)), actual)
  const anios = Array.from({ length: actual - desde + 1 }, (_, i) => actual - i)
  const [anio, setAnio] = useState(anios.includes(2024) ? 2024 : actual)
  const q = useQuery({ queryKey: ['estado-cuenta', ahorrador.id, anio], queryFn: () => api.get<EstadoCuenta>(`/ahorradores/${ahorrador.id}/estado-cuenta`, { anio }), enabled: abierto })

  const descargar = async (formato: 'PDF' | 'XLSX') => {
    try {
      guardar(await api.archivo(`/reportes/estado-cuenta/${ahorrador.id}`, { anio, formato }))
    } catch (e) {
      toast.error(textoError(tc, e))
    }
  }

  const columnas = useMemo<Columnas<Movimiento>>(
    () => [
      { header: t('perfil.fecha'), accessorKey: 'fecha', cell: ({ row }) => fecha(row.original.fecha) },
      { header: t('perfil.concepto'), accessorKey: 'tipo', cell: ({ row }) => <>{tc(`enums.tipoMovimiento.${row.original.tipo}`)}{row.original.referencia && <span className="ml-2 text-xs text-suave">{row.original.referencia}</span>}</> },
      { header: t('perfil.monto'), accessorKey: 'monto', cell: ({ row }) => dinero(row.original.monto), meta: { derecha: true } },
      { header: t('perfil.saldo'), accessorKey: 'saldoAhorro', cell: ({ row }) => <span className="font-semibold text-vino">{dinero(row.original.saldoAhorro)}</span>, meta: { derecha: true } },
    ],
    [t, tc],
  )
  const e = q.data
  return (
    <Modal
      abierto={abierto}
      onCerrar={onCerrar}
      titulo={`${t('ec.titulo')} · ${ahorrador.numEmpleado}`}
      descripcion={`${ahorrador.nombre} ${ahorrador.apellidos}`}
      ancho="xl"
      pie={
        <>
          <Button variante="secondary" icono={<MdDownload />} disabled={!e} onClick={() => void descargar('PDF')}>{t('ec.pdf')}</Button>
          <Button variante="secondary" icono={<MdDownload />} disabled={!e} onClick={() => void descargar('XLSX')}>{t('ec.excel')}</Button>
          <Button onClick={onCerrar}>{tc('acciones.cerrar')}</Button>
        </>
      }
    >
      <div className="mb-4 max-w-[200px]">
        <Select etiqueta={t('ec.anio')} value={anio} onChange={(ev) => setAnio(Number(ev.target.value))} opciones={anios.map((a) => ({ valor: a, texto: String(a) }))} />
      </div>
      {e && (
        <dl className="mb-4 grid gap-3 sm:grid-cols-3 lg:grid-cols-6">
          {[
            [t('ec.inicial'), e.saldoInicial],
            [t('ec.aportaciones'), e.totales.aportacionesOrdinarias + e.totales.aportacionesVoluntarias],
            [t('ec.liquidaciones'), e.totales.liquidaciones],
            [t('ec.prestamos'), e.totales.desembolsos],
            [t('ec.rendimiento'), e.totales.rendimientoAnual],
            [t('ec.final'), e.saldoFinal],
          ].map(([k, v]) => (
            <div key={k as string} className="rounded-control bg-lienzo p-3"><dt className="text-[11px] font-semibold text-suave">{k}</dt><dd className="mt-1 font-extrabold text-vino">{dinero(v as number)}</dd></div>
          ))}
        </dl>
      )}
      <DataTable etiqueta={t('ec.titulo')} columnas={columnas} datos={e?.movimientos} cargando={q.isLoading} error={q.isError ? q.error : undefined} onReintentar={() => void q.refetch()} virtual alto={360} />
    </Modal>
  )
}

function ModalCuota({ ahorrador, abierto, onCerrar }: { ahorrador: AhorradorDetalle; abierto: boolean; onCerrar: () => void }) {
  const { t } = useTranslation('ahorradores')
  const { t: tc } = useTranslation()
  const cache = useQueryClient()
  const toast = useToast()
  const [ordinaria, setOrdinaria] = useState(String(ahorrador.cuotaVigente?.cuotaOrdinaria ?? 0))
  const [activo, setActivo] = useState(ahorrador.cuotaVigente?.voluntarioActivo ?? false)
  const [voluntaria, setVoluntaria] = useState(String(ahorrador.cuotaVigente?.cuotaVoluntaria ?? 0))
  const [desde, setDesde] = useState('')
  const periodos = useQuery({ queryKey: ['periodos', ahorrador.cajaId, 'todos'], queryFn: () => api.get<{ estado: string; fechaInicio: string }[]>(`/cajas/${ahorrador.cajaId}/periodos`), enabled: abierto })
  const primero = periodos.data?.find((p) => p.estado === 'ABIERTO')?.fechaInicio ?? ''

  const guardarCuota = useMutation({
    mutationFn: () => api.post(`/ahorradores/${ahorrador.id}/cuotas`, {
      cuotaOrdinaria: Number(ordinaria), voluntarioActivo: activo, cuotaVoluntaria: activo ? Number(voluntaria) : 0, vigenteDesde: desde || primero,
    }),
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: ['ahorrador', ahorrador.id] })
      toast.ok(t('perfil.cuotaGuardada'))
      onCerrar()
    },
    onError: (e) => toast.error(textoError(tc, e)),
  })

  return (
    <Modal abierto={abierto} onCerrar={onCerrar} titulo={t('perfil.cambiarCuota')}
      pie={<><Button variante="secondary" onClick={onCerrar}>{tc('acciones.cancelar')}</Button><Button cargando={guardarCuota.isPending} disabled={!(desde || primero)} onClick={() => guardarCuota.mutate()}>{tc('acciones.guardar')}</Button></>}>
      <div className="grid gap-4">
        <Input etiqueta={t('campos.cuotaOrdinaria')} type="number" step="0.01" value={ordinaria} onChange={(e) => setOrdinaria(e.target.value)} />
        <div className="flex items-center gap-3"><Toggle activo={activo} onCambio={setActivo} etiqueta={t('campos.voluntarioActivo')} /><span className="text-sm font-medium text-vino">{t('campos.voluntarioActivo')}</span></div>
        {activo && <Input etiqueta={t('campos.cuotaVoluntaria')} type="number" step="0.01" value={voluntaria} onChange={(e) => setVoluntaria(e.target.value)} />}
        <Input etiqueta={t('campos.vigenteDesde')} ayuda={t('campos.vigenteAyuda')} type="date" value={desde || primero} onChange={(e) => setDesde(e.target.value)} />
      </div>
    </Modal>
  )
}

export default function Perfil() {
  const { t } = useTranslation('ahorradores')
  const { t: tc } = useTranslation()
  const { id } = useParams()
  const navegar = useNavigate()
  const { puede } = useSesion()
  const [tipo, setTipo] = useState('')
  const [page, setPage] = useState(1)
  const [editar, setEditar] = useState(false)
  const [estadoCuenta, setEstadoCuenta] = useState(false)
  const [cuota, setCuota] = useState(false)

  const q = useQuery({ queryKey: ['ahorrador', Number(id)], queryFn: () => api.get<AhorradorDetalle>(`/ahorradores/${id}`) })
  const movs = useQuery({
    queryKey: ['ahorrador-movimientos', Number(id), tipo, page],
    queryFn: () => api.get<Pagina<Movimiento>>(`/ahorradores/${id}/movimientos`, { tipo, page, pageSize: 15 }),
    placeholderData: keepPreviousData,
  })
  const a = q.data

  const columnas = useMemo<Columnas<Movimiento>>(
    () => [
      { header: t('perfil.fecha'), accessorKey: 'fecha', cell: ({ row }) => fecha(row.original.fecha), meta: { ancho: '120px' } },
      { header: t('perfil.concepto'), accessorKey: 'tipo', cell: ({ row }) => <>{tc(`enums.tipoMovimiento.${row.original.tipo}`)}{row.original.folioPrestamo && <Link to={`/prestamos?q=${row.original.folioPrestamo}`} className="ml-2 text-xs font-semibold text-primary-texto hover:underline">{row.original.folioPrestamo}</Link>}{row.original.referencia && !row.original.folioPrestamo && <span className="ml-2 text-xs text-suave">{row.original.referencia}</span>}</> },
      { header: t('perfil.monto'), accessorKey: 'monto', cell: ({ row }) => dinero(row.original.monto), meta: { derecha: true } },
      { header: t('perfil.saldo'), accessorKey: 'saldoAhorro', cell: ({ row }) => <span className="font-semibold text-vino">{dinero(row.original.saldoAhorro)}</span>, meta: { derecha: true } },
    ],
    [t, tc],
  )

  if (q.isError) return <Card><ErrorState detalle={textoError(tc, q.error)} onReintentar={() => void q.refetch()} /></Card>

  return (
    <>
      <Button variante="ghost" pequeno icono={<MdArrowBack />} onClick={() => navegar('/ahorradores')} className="mb-3">{t('perfil.volver')}</Button>
      {!a ? (
        <Skeleton className="mb-6 h-20 w-full" />
      ) : (
        <PageHeader
          titulo={`${a.nombre} ${a.apellidos}`}
          descripcion={`${a.numEmpleado} · ${a.caja} · ${a.departamento}`}
          acciones={
            <>
              <StatusPill tono={tonoDe(a.estado)} className="!text-sm">{tc(`enums.estadoAhorrador.${a.estado}`)}</StatusPill>
              <Button variante="secondary" icono={<MdSummarize />} onClick={() => setEstadoCuenta(true)}>{t('perfil.estadoCuenta')}</Button>
              {puede('capturar') && a.estado !== 'BAJA' && <Button variante="secondary" icono={<MdEdit />} onClick={() => setEditar(true)}>{t('perfil.editar')}</Button>}
              {puede('capturar') && a.estado === 'ACTIVO' && <Button icono={<MdRequestQuote />} onClick={() => navegar(`/prestamos/nuevo?ahorrador=${a.id}`)}>{t('perfil.nuevoPrestamo')}</Button>}
            </>
          }
        />
      )}

      <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
        <KpiCard titulo={t('perfil.ordinario')} cargando={!a} valor={a && dinero(a.ahorro.ordinario)} />
        <KpiCard titulo={t('perfil.voluntario')} cargando={!a} valor={a && dinero(a.ahorro.voluntario)} />
        <KpiCard titulo={t('perfil.total')} cargando={!a} valor={a && dinero(a.ahorro.total, true)} />
        <KpiCard titulo={t('perfil.limite')} cargando={!a} valor={a && dinero(a.ahorro.limitePrestamo)} detalle={a && t('perfil.saldoPrestamos', { monto: dinero(a.ahorro.saldoPrestamos) })} />
        <KpiCard titulo={t('perfil.disponible')} cargando={!a} valor={a && dinero(a.ahorro.disponibleSinAval)} />
      </section>

      <div className="mt-6 grid gap-6 lg:grid-cols-3">
        <Card>
          <div className="flex items-center justify-between"><h2 className="text-lg font-bold text-vino">{t('perfil.cuota')}</h2>{a && puede('capturar') && a.estado !== 'BAJA' && <Button variante="ghost" pequeno onClick={() => setCuota(true)}>{t('perfil.cambiarCuota')}</Button>}</div>
          {a?.cuotaVigente ? (
            <dl className="mt-3 grid gap-2 text-sm">
              <div className="flex justify-between"><dt className="text-suave">{t('perfil.cuotaOrdinaria')}</dt><dd className="font-semibold text-vino">{dinero(a.cuotaVigente.cuotaOrdinaria)}</dd></div>
              <div className="flex justify-between"><dt className="text-suave">{t('perfil.cuotaVoluntaria')}</dt><dd className="font-semibold text-vino">{a.cuotaVigente.voluntarioActivo ? dinero(a.cuotaVigente.cuotaVoluntaria) : '—'}</dd></div>
              <p className="text-xs text-suave">{t('perfil.cuotaDesde', { fecha: fecha(a.cuotaVigente.vigenteDesde) })}</p>
            </dl>
          ) : <p className="mt-3 text-sm text-suave">{a ? t('perfil.sinCuota') : '…'}</p>}
        </Card>
        <Card>
          <h2 className="text-lg font-bold text-vino">{t('perfil.prestamos')}</h2>
          {a && a.prestamosVigentes.length === 0 && <p className="mt-3 text-sm text-suave">{t('perfil.sinPrestamos')}</p>}
          <ul className="mt-3 flex flex-col gap-3">
            {a?.prestamosVigentes.map((p) => (
              <li key={p.folio} className="rounded-control bg-lienzo p-3 text-sm">
                <Link to={`/prestamos?q=${p.folio}`} className="font-bold text-primary-texto hover:underline">{p.folio}</Link>
                <p className="text-suave">{dinero(p.monto)} · {p.tasaMensual.toFixed(2)} % · {t('perfil.cuotasRestantes', { n: p.cuotasRestantes })}</p>
                <p className="font-semibold text-vino">{dinero(p.saldoVigente)}</p>
              </li>
            ))}
          </ul>
        </Card>
        <Card>
          <h2 className="text-lg font-bold text-vino">{t('perfil.datos')}</h2>
          {a && (
            <dl className="mt-3 grid grid-cols-2 gap-x-3 gap-y-2 text-sm">
              {[
                [t('campos.rfc'), a.rfc], [t('campos.clabe'), a.clabe], [t('campos.fechaNacimiento'), fecha(a.fechaNacimiento)], [t('perfil.ingreso'), fecha(a.fechaIngreso)],
                [t('campos.sueldoQuincenal'), dinero(a.sueldoQuincenal)], ...(a.fechaBaja ? [[t('perfil.baja'), fecha(a.fechaBaja)]] : []),
              ].map(([k, v]) => <div key={k}><dt className="text-xs text-suave">{k}</dt><dd className="break-all font-semibold text-vino">{v}</dd></div>)}
            </dl>
          )}
        </Card>
      </div>

      <Card className="mt-6 !p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 p-5">
          <h2 className="text-xl font-bold text-vino">{t('perfil.movimientos')}</h2>
          <Select nombre={t('perfil.tipo')} value={tipo} onChange={(e) => { setTipo(e.target.value); setPage(1) }} className="!w-auto">
            <option value="">{`${t('perfil.tipo')}: ${t('perfil.todosTipos')}`}</option>
            {TIPOS.map((x) => <option key={x} value={x}>{tc(`enums.tipoMovimiento.${x}`)}</option>)}
          </Select>
        </div>
        <DataTable
          etiqueta={t('perfil.movimientos')}
          columnas={columnas}
          datos={movs.data?.items}
          cargando={movs.isLoading}
          error={movs.isError ? movs.error : undefined}
          onReintentar={() => void movs.refetch()}
          pagina={movs.data && { page: movs.data.page, pageSize: movs.data.pageSize, total: movs.data.total }}
          onPagina={setPage}
        />
      </Card>

      {a && <FormularioAhorrador abierto={editar} onCerrar={() => setEditar(false)} ahorrador={a} />}
      {a && estadoCuenta && <ModalEstadoCuenta ahorrador={a} abierto onCerrar={() => setEstadoCuenta(false)} />}
      {a && cuota && <ModalCuota ahorrador={a} abierto onCerrar={() => setCuota(false)} />}
    </>
  )
}

