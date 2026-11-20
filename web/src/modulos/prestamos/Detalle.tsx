import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdArrowBack, MdCheck, MdClose } from 'react-icons/md'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useSesion } from '../../auth/Sesion'
import { api } from '../../lib/api'
import { textoError } from '../../lib/errores'
import { dinero, fecha } from '../../lib/formato'
import type { PrestamoDetalle } from '../../lib/tipos'
import { Button, Card, ErrorState, KpiCard, PageHeader, Progreso, Skeleton, StatusPill, tonoDe } from '../../ui/base'
import { Modal } from '../../ui/Modal'
import { useToast } from '../../ui/Toast'
import { TablaAmortizacion } from './Compartido'

export default function Detalle() {
  const { t } = useTranslation('prestamos')
  const { t: tc } = useTranslation()
  const { id } = useParams()
  const navegar = useNavigate()
  const { puede } = useSesion()
  const cache = useQueryClient()
  const toast = useToast()
  const [accion, setAccion] = useState<'autorizar' | 'rechazar' | null>(null)
  const q = useQuery({ queryKey: ['prestamo', Number(id)], queryFn: () => api.get<PrestamoDetalle>(`/prestamos/${id}`) })
  const p = q.data?.prestamo

  const resolver = useMutation({
    mutationFn: (tipo: 'autorizar' | 'rechazar') => api.post(`/prestamos/${id}/${tipo}`),
    onSuccess: (_r, tipo) => {
      toast.ok(tipo === 'autorizar' ? t('autorizado') : t('rechazado'))
      setAccion(null)
      for (const k of ['prestamo', 'prestamos', 'prestamos-resumen', 'dashboard', 'ahorrador']) void cache.invalidateQueries({ queryKey: [k] })
    },
    onError: (e) => { setAccion(null); toast.error(textoError(tc, e)) },
  })

  if (q.isError) return <Card><ErrorState detalle={textoError(tc, q.error)} onReintentar={() => void q.refetch()} /></Card>
  const total = p ? p.cuotasAplicadas + p.cuotasRestantes : 0

  return (
    <>
      <Button variante="ghost" pequeno icono={<MdArrowBack />} onClick={() => navegar('/prestamos')} className="mb-3">{t('detalle.volver')}</Button>
      {!p ? <Skeleton className="mb-6 h-20 w-full" /> : (
        <PageHeader
          titulo={p.folio}
          descripcion={`${p.ahorrador} · ${p.caja}`}
          acciones={
            <>
              <StatusPill tono={tonoDe(p.estado)} className="!text-sm">{tc(`enums.estadoPrestamo.${p.estado}`)}</StatusPill>
              {puede('administrar') && p.estado === 'PENDIENTE' && (
                <>
                  <Button icono={<MdCheck />} onClick={() => setAccion('autorizar')}>{t('autorizar')}</Button>
                  <Button variante="secondary" icono={<MdClose />} onClick={() => setAccion('rechazar')}>{t('rechazar')}</Button>
                </>
              )}
            </>
          }
        />
      )}

      <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard titulo={t('detalle.monto')} cargando={!p} valor={p && dinero(p.monto, true)} />
        <KpiCard titulo={t('detalle.saldo')} cargando={!p} valor={p && dinero(p.saldoVigente, true)} />
        <KpiCard titulo={t('detalle.capital')} cargando={!p} valor={p && dinero(p.capitalPagado)} />
        <KpiCard titulo={t('detalle.interes')} cargando={!p} valor={p && dinero(p.interesPagado)} />
      </section>

      <div className="mt-6 grid gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          {p && (
            <>
              <p className="mb-2 text-sm font-semibold text-vino">{t('detalle.avance', { aplicadas: p.cuotasAplicadas, total })}</p>
              <Progreso valor={total ? p.cuotasAplicadas / total : 0} etiqueta={t('detalle.avance', { aplicadas: p.cuotasAplicadas, total })} />
              {q.data?.cuotaActual != null && <p className="mt-2 text-xs text-suave">{t('detalle.actual')}: #{q.data.cuotaActual}</p>}
            </>
          )}
        </Card>
        <Card>
          {p && (
            <dl className="grid grid-cols-2 gap-x-3 gap-y-2 text-sm">
              <div><dt className="text-xs text-suave">{t('detalle.ahorrador')}</dt><dd><Link to={`/ahorradores/${p.ahorradorId}`} className="font-semibold text-primary-texto hover:underline">{p.numEmpleado}</Link></dd></div>
              <div><dt className="text-xs text-suave">{t('detalle.tasa')}</dt><dd className="font-semibold text-vino">{p.tasaMensual.toFixed(2)} %</dd></div>
              <div><dt className="text-xs text-suave">{t('detalle.plazo')}</dt><dd className="font-semibold text-vino">{t('plazoValor', { n: p.plazoQuincenas })}</dd></div>
              <div><dt className="text-xs text-suave">{t('detalle.garantia')}</dt><dd className="font-semibold text-vino">{tc(`enums.garantia.${p.garantia}`)}</dd></div>
              {p.aval && <div className="col-span-2"><dt className="text-xs text-suave">{t('detalle.aval')}</dt><dd className="font-semibold text-vino">{p.aval.nombre} ({p.aval.numEmpleado})<span className="block text-xs font-normal text-suave">{t('detalle.excedente', { monto: dinero(p.excedenteAval) })}</span></dd></div>}
              <div><dt className="text-xs text-suave">{t('detalle.solicitud')}</dt><dd className="font-semibold text-vino">{fecha(p.fechaSolicitud)}</dd></div>
              <div><dt className="text-xs text-suave">{t('detalle.autorizacion')}</dt><dd className="font-semibold text-vino">{fecha(p.fechaAutorizacion)}{p.autorizadoPor && <span className="block text-xs font-normal text-suave">{t('detalle.autorizadoPor', { nombre: p.autorizadoPor })}</span>}</dd></div>
            </dl>
          )}
        </Card>
      </div>

      <Card className="mt-6 !p-0">
        <h2 className="p-5 pb-2 text-xl font-bold text-vino">{t('detalle.tabla')}</h2>
        <TablaAmortizacion cuotas={q.data?.amortizacion} actual={q.data?.cuotaActual} cargando={q.isLoading} virtual />
      </Card>

      <Modal abierto={!!accion} onCerrar={() => setAccion(null)} ancho="sm"
        titulo={accion && p ? t(accion === 'autorizar' ? 'confirmarAutorizar.titulo' : 'confirmarRechazar.titulo', { folio: p.folio }) : ''}
        descripcion={accion && p ? t(accion === 'autorizar' ? 'confirmarAutorizar.texto' : 'confirmarRechazar.texto', { monto: dinero(p.monto) }) : undefined}
        pie={<><Button variante="secondary" onClick={() => setAccion(null)}>{tc('acciones.cancelar')}</Button><Button variante={accion === 'rechazar' ? 'destructive' : 'primary'} cargando={resolver.isPending} onClick={() => accion && resolver.mutate(accion)}>{tc('acciones.confirmar')}</Button></>}>
        <span />
      </Modal>
    </>
  )
}
