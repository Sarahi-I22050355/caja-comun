import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdArrowBack } from 'react-icons/md'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useCaja } from '../../auth/CajaActiva'
import { useSesion } from '../../auth/Sesion'
import { api } from '../../lib/api'
import { textoCodigo, textoError } from '../../lib/errores'
import { dinero } from '../../lib/formato'
import { useDebounce } from '../../lib/hooks'
import type { AhorradorDetalle, PrestamoDetalle, Simulacion } from '../../lib/tipos'
import { AhorradorPicker } from '../../ui/AhorradorPicker'
import type { AhorradorElegido } from '../../ui/AhorradorPicker'
import { Button, Card, ErrorState, PageHeader, Skeleton } from '../../ui/base'
import { Input } from '../../ui/form'
import { useToast } from '../../ui/Toast'
import { TablaAmortizacion } from './Compartido'

export default function Nuevo() {
  const { t } = useTranslation('prestamos')
  const { t: tc } = useTranslation()
  const { puede } = useSesion()
  const { cajaId } = useCaja()
  const navegar = useNavigate()
  const cache = useQueryClient()
  const toast = useToast()
  const [params] = useSearchParams()
  const [ahorrador, setAhorrador] = useState<AhorradorElegido | null>(null)
  const [aval, setAval] = useState<AhorradorElegido | null>(null)
  const [monto, setMonto] = useState('')
  const [plazo, setPlazo] = useState('12')

  // Si se llega desde el perfil de un ahorrador, se precarga.
  const previo = params.get('ahorrador')
  const precarga = useQuery({ queryKey: ['ahorrador', Number(previo)], queryFn: () => api.get<AhorradorDetalle>(`/ahorradores/${previo}`), enabled: !!previo })
  useEffect(() => {
    if (precarga.data && !ahorrador) setAhorrador({ id: precarga.data.id, numEmpleado: precarga.data.numEmpleado, nombre: `${precarga.data.nombre} ${precarga.data.apellidos}` })
  }, [precarga.data, ahorrador])

  const montoN = Number(monto)
  const plazoN = Number(plazo)
  const validos = !!ahorrador && montoN > 0 && Number.isInteger(plazoN) && plazoN >= 1 && plazoN <= 36
  const solicitud = useDebounce(validos ? { ahorradorId: ahorrador!.id, monto: montoN, plazoQuincenas: plazoN, avalAhorradorId: aval?.id ?? null } : null, 400)

  // La simulación se pide al servidor (garantía y tabla); no se calcula nada del dinero en el navegador.
  const sim = useQuery({
    queryKey: ['simulacion', solicitud],
    queryFn: () => api.post<Simulacion>('/prestamos/simular', solicitud),
    enabled: !!solicitud,
    retry: false,
  })
  const ev = sim.data?.evaluacion

  const crear = useMutation({
    mutationFn: () => api.post<PrestamoDetalle>('/prestamos', { ahorradorId: ahorrador!.id, monto: montoN, plazoQuincenas: plazoN, avalAhorradorId: ev?.requiereAval ? aval?.id ?? null : null }),
    onSuccess: (d) => {
      toast.ok(t('nuevoForm.creado', { folio: d.prestamo.folio }))
      for (const k of ['prestamos', 'prestamos-resumen']) void cache.invalidateQueries({ queryKey: [k] })
      navegar(`/prestamos/${d.prestamo.id}`)
    },
    onError: (e) => toast.error(textoError(tc, e)),
  })

  if (!puede('capturar')) return <Card><ErrorState detalle={textoCodigo(tc, 'SIN_PERMISO')} /></Card>

  return (
    <>
      <Button variante="ghost" pequeno icono={<MdArrowBack />} onClick={() => navegar('/prestamos')} className="mb-3">{t('detalle.volver')}</Button>
      <PageHeader titulo={t('nuevoForm.titulo')} descripcion={t('nuevoForm.descripcion')} />

      <div className="grid gap-6 xl:grid-cols-3">
        <Card className="flex flex-col gap-4 xl:col-span-1">
          <AhorradorPicker id="np-ahorrador" etiqueta={t('nuevoForm.ahorrador')} cajaId={cajaId} valor={ahorrador} onElegir={(a) => { setAhorrador(a); setAval(null) }} estado="ACTIVO" />
          <Input etiqueta={t('nuevoForm.monto')} type="number" min="1" step="0.01" inputMode="decimal" value={monto} onChange={(e) => setMonto(e.target.value)} />
          <Input etiqueta={t('nuevoForm.plazo')} type="number" min="1" max="36" step="1" value={plazo} onChange={(e) => setPlazo(e.target.value)} />
          {(ev?.requiereAval || aval) && (
            <div>
              <AhorradorPicker id="np-aval" etiqueta={t('nuevoForm.aval')} cajaId={cajaId} valor={aval} onElegir={setAval} estado="ACTIVO" excluir={ahorrador?.id} />
              <p className="mt-1 text-xs text-suave">{t('nuevoForm.avalAyuda')}</p>
            </div>
          )}
          <Button cargando={crear.isPending} disabled={!ev?.aprobada} onClick={() => crear.mutate()}>{t('nuevoForm.solicitar')}</Button>
        </Card>

        <div className="flex flex-col gap-6 xl:col-span-2">
          <Card>
            <h2 className="text-lg font-bold text-vino">{t('nuevoForm.evaluacion')}</h2>
            {!solicitud ? (
              <p className="mt-3 text-sm text-suave">{t('nuevoForm.sinDatos')}</p>
            ) : sim.isError ? (
              <ErrorState detalle={textoError(tc, sim.error)} onReintentar={() => void sim.refetch()} />
            ) : !ev || !sim.data ? (
              <div className="mt-3 grid gap-3 sm:grid-cols-4"><Skeleton className="h-14" /><Skeleton className="h-14" /><Skeleton className="h-14" /><Skeleton className="h-14" /></div>
            ) : (
              <>
                <dl className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                  {[
                    [t('nuevoForm.ahorro'), ev.ahorro], [t('nuevoForm.limite'), ev.limite], [t('nuevoForm.saldo'), ev.saldoPrestamos], [t('nuevoForm.disponible'), ev.disponibleSinAval],
                  ].map(([k, v]) => <div key={k as string} className="rounded-control bg-lienzo p-3"><dt className="text-[11px] font-semibold text-suave">{k}</dt><dd className="mt-1 font-extrabold text-vino">{dinero(v as number)}</dd></div>)}
                </dl>
                <div className={`mt-4 rounded-control px-4 py-3 text-sm font-medium ${ev.aprobada ? 'bg-estado-ok-bg text-estado-ok' : 'bg-estado-pendiente-bg text-estado-pendiente'}`} role="status">
                  <p>{tc(`enums.garantia.${ev.garantia}`)} — {ev.aprobada ? t('nuevoForm.aprobada') : t(`nuevoForm.motivos.${ev.motivo ?? 'AVAL_REQUERIDO'}`)}</p>
                  {ev.requiereAval && <p className="mt-0.5 text-xs">{t('nuevoForm.excedente')}: <b>{dinero(ev.excedenteAval)}</b>{ev.aval && ` · ${t('nuevoForm.avalDisponible', { monto: dinero(ev.aval.disponible) })}`}</p>}
                </div>
                <dl className="mt-4 grid gap-3 sm:grid-cols-4">
                  {[
                    [t('nuevoForm.resumen.primera'), dinero(sim.data.primeraCuota)], [t('nuevoForm.resumen.intereses'), dinero(sim.data.totalIntereses)],
                    [t('nuevoForm.resumen.total'), dinero(sim.data.totalAPagar)], [t('nuevoForm.resumen.tasa'), `${sim.data.tasaMensual.toFixed(2)} %`],
                  ].map(([k, v]) => <div key={k as string}><dt className="text-xs text-suave">{k}</dt><dd className="text-lg font-extrabold text-vino">{v}</dd></div>)}
                </dl>
              </>
            )}
          </Card>
          {sim.data && (
            <Card className="!p-0">
              <h2 className="p-5 pb-2 text-lg font-bold text-vino">{t('nuevoForm.tabla')}</h2>
              <TablaAmortizacion cuotas={sim.data.amortizacion} virtual />
            </Card>
          )}
        </div>
      </div>
    </>
  )
}
