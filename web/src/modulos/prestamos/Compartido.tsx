import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { dinero, fecha } from '../../lib/formato'
import type { CuotaPrestamo } from '../../lib/tipos'
import { StatusPill, tonoDe } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'

/** Tabla de amortización: la cuota actual (la próxima por cobrar) se resalta. */
export function TablaAmortizacion({ cuotas, actual, cargando, virtual }: { cuotas: CuotaPrestamo[] | undefined; actual?: number | null; cargando?: boolean; virtual?: boolean }) {
  const { t } = useTranslation('prestamos')
  const { t: tc } = useTranslation()
  const columnas = useMemo<Columnas<CuotaPrestamo>>(
    () => [
      { header: t('amort.numero'), accessorKey: 'numero', meta: { ancho: '56px' } },
      { header: t('amort.periodo'), accessorKey: 'periodo', cell: ({ row }) => row.original.periodo ?? '—' },
      { header: t('amort.fecha'), accessorKey: 'fecha', cell: ({ row }) => fecha(row.original.fecha), meta: { ocultoMovil: true } },
      { header: t('amort.saldoInicial'), accessorKey: 'saldoInicial', cell: ({ row }) => dinero(row.original.saldoInicial), meta: { derecha: true } },
      { header: t('amort.capital'), accessorKey: 'capital', cell: ({ row }) => dinero(row.original.capital), meta: { derecha: true } },
      { header: t('amort.interes'), accessorKey: 'interes', cell: ({ row }) => dinero(row.original.interes), meta: { derecha: true } },
      { header: t('amort.total'), accessorKey: 'total', cell: ({ row }) => <span className="font-extrabold text-vino">{dinero(row.original.total)}</span>, meta: { derecha: true } },
      { header: t('amort.saldoFinal'), accessorKey: 'saldoFinal', cell: ({ row }) => dinero(row.original.saldoFinal), meta: { derecha: true } },
      { header: t('amort.estado'), accessorKey: 'estado', cell: ({ row }) => (row.original.estado ? <StatusPill tono={tonoDe(row.original.estado)}>{tc(`enums.estadoAmortizacion.${row.original.estado}`)}</StatusPill> : null) },
    ],
    [t, tc],
  )
  return (
    <DataTable
      etiqueta={t('detalle.tabla')}
      columnas={columnas}
      datos={cuotas}
      cargando={cargando}
      virtual={virtual}
      alto={560}
      claseFila={(c) => (actual != null && c.numero === actual ? 'bg-tinte font-semibold ring-1 ring-inset ring-primary/40' : undefined)}
    />
  )
}
