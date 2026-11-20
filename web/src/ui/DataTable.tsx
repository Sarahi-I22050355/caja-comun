import { flexRender, getCoreRowModel, useReactTable } from '@tanstack/react-table'
import type { ColumnDef, RowData } from '@tanstack/react-table'
import { useVirtualizer } from '@tanstack/react-virtual'
import { useRef } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { textoError } from '../lib/errores'
import { Button, EmptyState, ErrorState, Skeleton, cn } from './base'

declare module '@tanstack/react-table' {
  interface ColumnMeta<TData extends RowData, TValue> {
    // Los parámetros de tipo deben coincidir con la declaración original de la librería.
    _tipos?: [TData, TValue]
    derecha?: boolean
    ancho?: string
    ocultoMovil?: boolean
  }
}

export interface Paginado {
  page: number
  pageSize: number
  total: number
}

export function Paginacion({ pagina, onPagina }: { pagina: Paginado; onPagina: (p: number) => void }) {
  const { t } = useTranslation()
  const paginas = Math.max(1, Math.ceil(pagina.total / pagina.pageSize))
  const desde = pagina.total === 0 ? 0 : (pagina.page - 1) * pagina.pageSize + 1
  const hasta = Math.min(pagina.total, pagina.page * pagina.pageSize)
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 border-t border-borde px-4 py-3 text-sm text-suave">
      <span>{t('paginacion.mostrando', { desde, hasta, total: pagina.total.toLocaleString('en-US') })}</span>
      <div className="flex items-center gap-2">
        <span className="hidden sm:inline">{t('paginacion.pagina', { pagina: pagina.page, total: paginas })}</span>
        <Button variante="secondary" pequeno disabled={pagina.page <= 1} onClick={() => onPagina(pagina.page - 1)}>
          {t('paginacion.anterior')}
        </Button>
        <Button variante="secondary" pequeno disabled={pagina.page >= paginas} onClick={() => onPagina(pagina.page + 1)}>
          {t('paginacion.siguiente')}
        </Button>
      </div>
    </div>
  )
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export type Columnas<T> = ColumnDef<T, any>[]

interface Props<T> {
  columnas: Columnas<T>
  datos: T[] | undefined
  etiqueta: string
  cargando?: boolean
  error?: unknown
  onReintentar?: () => void
  pagina?: Paginado
  onPagina?: (p: number) => void
  onFila?: (fila: T) => void
  claseFila?: (fila: T) => string | undefined
  vacio?: ReactNode
  /** Renderiza solo las filas visibles (para listas largas dentro de un contenedor con alto fijo). */
  virtual?: boolean
  alto?: number
}

export function DataTable<T>({ columnas, datos, etiqueta, cargando, error, onReintentar, pagina, onPagina, onFila, claseFila, vacio, virtual, alto = 520 }: Props<T>) {
  const { t } = useTranslation()
  const tabla = useReactTable({ data: datos ?? [], columns: columnas, getCoreRowModel: getCoreRowModel() })
  const filas = tabla.getRowModel().rows
  const contenedor = useRef<HTMLDivElement>(null)
  const virtualizador = useVirtualizer({
    count: virtual ? filas.length : 0,
    getScrollElement: () => contenedor.current,
    estimateSize: () => 46,
    overscan: 8,
  })

  if (error && !datos) return <ErrorState detalle={textoError(t, error)} onReintentar={onReintentar} />

  const celda = (derecha?: boolean, extra?: string) => cn('px-4 py-3', derecha && 'text-right', extra)
  const cabeza = tabla.getHeaderGroups()
  const visibles = virtual ? virtualizador.getVirtualItems() : []
  const arriba = visibles.length > 0 ? visibles[0].start : 0
  const abajo = visibles.length > 0 ? virtualizador.getTotalSize() - visibles[visibles.length - 1].end : 0
  const lista = virtual ? visibles.map((v) => filas[v.index]) : filas

  return (
    <div>
      <div ref={contenedor} className="overflow-x-auto" style={virtual ? { maxHeight: alto, overflowY: 'auto' } : undefined}>
        <table className="w-full min-w-[640px] border-collapse text-left text-[13px]" aria-label={etiqueta} aria-busy={cargando || undefined}>
          <thead className="sticky top-0 z-10 bg-tinte">
            {cabeza.map((g) => (
              <tr key={g.id}>
                {g.headers.map((h) => (
                  <th
                    key={h.id}
                    scope="col"
                    style={{ width: h.column.columnDef.meta?.ancho }}
                    className={cn('px-4 py-3 text-xs font-bold uppercase tracking-wide text-vino', h.column.columnDef.meta?.derecha && 'text-right')}
                  >
                    {flexRender(h.column.columnDef.header, h.getContext())}
                  </th>
                ))}
              </tr>
            ))}
          </thead>
          <tbody>
            {cargando && !datos
              ? Array.from({ length: 6 }, (_, i) => (
                  <tr key={i} className="border-t border-borde/60">
                    {columnas.map((_c, j) => (
                      <td key={j} className="px-4 py-3.5">
                        <Skeleton className="h-4 w-full max-w-[160px]" />
                      </td>
                    ))}
                  </tr>
                ))
              : (
                <>
                  {virtual && arriba > 0 && <tr style={{ height: arriba }} aria-hidden />}
                  {lista.map((fila) => (
                    <tr
                      key={fila.id}
                      tabIndex={onFila ? 0 : undefined}
                      onClick={onFila ? () => onFila(fila.original) : undefined}
                      onKeyDown={onFila ? (e) => { if (e.key === 'Enter') onFila(fila.original) } : undefined}
                      className={cn('border-t border-borde/60 transition', onFila && 'cursor-pointer hover:bg-lienzo focus-visible:bg-lienzo focus-visible:outline-none', claseFila?.(fila.original))}
                    >
                      {fila.getVisibleCells().map((c) => (
                        <td key={c.id} className={celda(c.column.columnDef.meta?.derecha, 'tabular-nums')}>
                          {flexRender(c.column.columnDef.cell, c.getContext())}
                        </td>
                      ))}
                    </tr>
                  ))}
                  {virtual && abajo > 0 && <tr style={{ height: abajo }} aria-hidden />}
                </>
              )}
          </tbody>
        </table>
      </div>
      {!cargando && datos && datos.length === 0 && (vacio ?? <EmptyState />)}
      {pagina && onPagina && datos && <Paginacion pagina={pagina} onPagina={onPagina} />}
    </div>
  )
}
