import { useQuery } from '@tanstack/react-query'
import { Suspense, lazy, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { MdAccountBalance, MdGroups, MdPayments, MdTrendingUp } from 'react-icons/md'
import { useCaja } from '../../auth/CajaActiva'
import { api } from '../../lib/api'
import { usePeriodos, referenciaPeriodo } from '../../lib/consultas'
import { textoError } from '../../lib/errores'
import { dinero, entero_, fecha, iniciales, nombreMes } from '../../lib/formato'
import type { Dashboard, DescuentosPeriodo, MovimientoCaja } from '../../lib/tipos'
import { Card, EmptyState, ErrorState, KpiCard, PageHeader, Skeleton } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'

const Grafica = lazy(() => import('./Grafica'))

export default function Pagina() {
  const { t } = useTranslation('inicio')
  const { t: tc } = useTranslation()
  const { cajaId, caja } = useCaja()

  const tablero = useQuery({
    queryKey: ['dashboard', cajaId],
    queryFn: () => api.get<Dashboard>('/dashboard', { cajaId }),
    enabled: cajaId !== undefined,
  })
  const d = tablero.data

  // "Último periodo aplicado": se toma de los periodos del ciclo en curso y se consulta su resumen de nómina.
  const periodos = usePeriodos(cajaId, d?.anioCiclo)
  const ultimo = useMemo(() => [...(periodos.data ?? [])].reverse().find((p) => p.estado === 'APLICADO'), [periodos.data])
  const corte = useQuery({
    queryKey: ['descuentos-resumen', ultimo?.id],
    queryFn: () => api.get<DescuentosPeriodo>(`/nomina/periodos/${ultimo!.id}/descuentos`, { pageSize: 1 }),
    enabled: !!ultimo,
  })

  const serie = useMemo(
    () => (d?.serie ?? []).map((p) => ({ mes: nombreMes(p.mes), ahorro: p.ahorroAportado, prestamos: p.prestamosDesembolsados })),
    // El nombre del mes depende del idioma.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [d?.serie, tc],
  )

  const columnas = useMemo<Columnas<MovimientoCaja>>(
    () => [
      { header: t('movimientos.fecha'), accessorKey: 'fecha', cell: ({ row }) => fecha(row.original.fecha), meta: { ancho: '120px' } },
      {
        header: t('movimientos.ahorrador'),
        accessorKey: 'ahorrador',
        cell: ({ row }) => (
          <div className="flex items-center gap-3">
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-tinte text-xs font-bold text-primary-texto">{iniciales(row.original.ahorrador)}</span>
            <span className="min-w-0">
              <span className="block truncate font-semibold text-vino">{row.original.ahorrador}</span>
              <span className="block text-xs text-suave">{row.original.numEmpleado}</span>
            </span>
          </div>
        ),
      },
      {
        header: t('movimientos.concepto'),
        accessorKey: 'tipo',
        cell: ({ row }) => (
          <span>
            {tc(`enums.tipoMovimiento.${row.original.tipo}`)}
            {row.original.referencia && <span className="ml-2 text-xs text-suave">{row.original.referencia}</span>}
          </span>
        ),
      },
      { header: t('movimientos.monto'), accessorKey: 'monto', cell: ({ row }) => <span className="font-semibold text-vino">{dinero(row.original.monto)}</span>, meta: { derecha: true } },
    ],
    [t, tc],
  )

  if (cajaId === undefined) return <EmptyState titulo={t('titulo')} detalle={t('sinCaja')} />

  return (
    <>
      <PageHeader titulo={t('titulo')} descripcion={t('descripcion')} />

      <section aria-label="KPI" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard titulo={t('kpi.fondo')} icono={<MdAccountBalance />} cargando={tablero.isLoading} valor={d && dinero(d.fondoTotal, true)} detalle={t('kpi.fondoDetalle')} />
        <KpiCard titulo={t('kpi.ahorradores')} icono={<MdGroups />} cargando={tablero.isLoading} valor={d && entero_(d.ahorradoresActivos)} detalle={t('kpi.ahorradoresDetalle')} />
        <KpiCard
          titulo={t('kpi.prestamos')}
          icono={<MdPayments />}
          cargando={tablero.isLoading}
          valor={d && dinero(d.saldoPrestamos, true)}
          detalle={d && t('kpi.prestamosDetalle', { n: d.prestamosVigentes })}
        />
        <KpiCard
          titulo={t('kpi.interes')}
          icono={<MdTrendingUp />}
          cargando={tablero.isLoading}
          valor={d && dinero(d.interesAcumuladoCiclo, true)}
          detalle={d && t('kpi.interesDetalle', { anio: d.anioCiclo })}
        />
      </section>

      {tablero.isError && <Card className="mt-6"><ErrorState detalle={textoError(tc, tablero.error)} onReintentar={() => void tablero.refetch()} /></Card>}

      <div className="mt-6 grid gap-6 xl:grid-cols-3">
        <Card className="xl:col-span-2">
          <h2 className="text-xl font-bold text-vino">{t('grafica.titulo')}</h2>
          <p className="mb-4 text-sm text-suave">{t('grafica.descripcion')}</p>
          {tablero.isLoading ? (
            <Skeleton className="h-72 w-full" />
          ) : serie.length === 0 ? (
            <EmptyState />
          ) : (
            <Suspense fallback={<Skeleton className="h-72 w-full" />}>
              <Grafica serie={serie} titulo={t('grafica.titulo')} ahorro={t('grafica.ahorro')} prestamos={t('grafica.prestamos')} />
            </Suspense>
          )}
        </Card>

        <Card>
          <p className="inline-block rounded-full bg-estado-ok-bg px-2.5 py-0.5 text-[11px] font-bold uppercase tracking-wide text-estado-ok">{t('corte.etiqueta')}</p>
          {periodos.isLoading || (ultimo && corte.isLoading) ? (
            <div className="mt-4 flex flex-col gap-3"><Skeleton className="h-7 w-2/3" /><Skeleton className="h-4 w-full" /><Skeleton className="h-4 w-full" /><Skeleton className="h-16 w-full" /></div>
          ) : !ultimo || !corte.data ? (
            <p className="mt-4 text-sm text-suave">{t('corte.sinAplicar')}</p>
          ) : (
            <>
              <h2 className="mt-3 text-xl font-bold text-vino">{t('corte.titulo', { periodo: referenciaPeriodo(ultimo, caja?.frecuenciaNomina) })}</h2>
              <p className="text-xs text-suave">{t('corte.detalle', { fecha: fecha(ultimo.fechaFin) })}</p>
              <dl className="mt-4 flex flex-col gap-2 text-sm">
                {[
                  ['corte.ordinario', corte.data.resumen.ordinaria],
                  ['corte.voluntario', corte.data.resumen.voluntaria],
                  ['corte.prestamo', corte.data.resumen.prestamo],
                ].map(([k, v]) => (
                  <div key={k as string} className="flex justify-between gap-3">
                    <dt className="text-suave">{t(k as string)}</dt>
                    <dd className="font-semibold text-vino">{dinero(v as number)}</dd>
                  </div>
                ))}
              </dl>
              <div className="mt-4 rounded-tarjeta bg-tinte p-4">
                <p className="text-[11px] font-bold uppercase tracking-wide text-vino">{t('corte.total')}</p>
                <p className="mt-1 text-2xl font-extrabold text-vino">{dinero(corte.data.resumen.total, true)}</p>
                <p className="mt-1 text-xs text-suave">{t('corte.ahorradores', { n: entero_(corte.data.resumen.ahorradores - corte.data.resumen.suspendidos) })}</p>
              </div>
            </>
          )}
        </Card>
      </div>

      <Card className="mt-6 !p-0">
        <div className="px-5 pt-5">
          <h2 className="text-xl font-bold text-vino">{t('movimientos.titulo')}</h2>
          <p className="mb-3 text-sm text-suave">{t('movimientos.descripcion')}</p>
        </div>
        <DataTable
          etiqueta={t('movimientos.titulo')}
          columnas={columnas}
          datos={d?.ultimosMovimientos}
          cargando={tablero.isLoading}
          error={tablero.isError ? tablero.error : undefined}
          onReintentar={() => void tablero.refetch()}
        />
      </Card>
    </>
  )
}
