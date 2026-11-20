import { useEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { MdClose } from 'react-icons/md'
import { cn } from './base'

/** Modal cerrado por defecto: no se renderiza nada mientras `abierto` sea falso. Esc y el fondo lo cierran. */
export function Modal({ abierto, onCerrar, titulo, descripcion, children, pie, ancho = 'md' }: {
  abierto: boolean
  onCerrar: () => void
  titulo: string
  descripcion?: string
  children: ReactNode
  pie?: ReactNode
  ancho?: 'sm' | 'md' | 'lg' | 'xl'
}) {
  const { t } = useTranslation()
  const caja = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!abierto) return
    const previo = document.activeElement as HTMLElement | null
    const alTeclear = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onCerrar()
      if (e.key === 'Tab' && caja.current) {
        const foco = caja.current.querySelectorAll<HTMLElement>('a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])')
        if (foco.length === 0) return
        const primero = foco[0]
        const ultimo = foco[foco.length - 1]
        if (e.shiftKey && document.activeElement === primero) { e.preventDefault(); ultimo.focus() }
        else if (!e.shiftKey && document.activeElement === ultimo) { e.preventDefault(); primero.focus() }
      }
    }
    document.addEventListener('keydown', alTeclear)
    document.body.style.overflow = 'hidden'
    const id = setTimeout(() => caja.current?.querySelector<HTMLElement>('input,select,textarea')?.focus(), 30)
    return () => {
      clearTimeout(id)
      document.removeEventListener('keydown', alTeclear)
      document.body.style.overflow = ''
      previo?.focus?.()
    }
  }, [abierto, onCerrar])

  if (!abierto) return null
  const anchos = { sm: 'max-w-md', md: 'max-w-xl', lg: 'max-w-3xl', xl: 'max-w-5xl' }
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-vino/40 p-0 sm:items-center sm:p-4" onMouseDown={(e) => e.target === e.currentTarget && onCerrar()}>
      <div
        ref={caja}
        role="dialog"
        aria-modal="true"
        aria-label={titulo}
        className={cn('flex max-h-[92vh] w-full flex-col rounded-t-modal bg-white shadow-n3 sm:rounded-modal', anchos[ancho])}
      >
        <div className="flex items-start justify-between gap-3 border-b border-borde px-6 py-4">
          <div>
            <h2 className="text-xl font-bold text-vino">{titulo}</h2>
            {descripcion && <p className="mt-0.5 text-sm text-suave">{descripcion}</p>}
          </div>
          <button type="button" onClick={onCerrar} aria-label={t('acciones.cerrar')} className="rounded-full p-1.5 text-xl text-suave hover:bg-tinte focus-visible:outline-none focus-visible:shadow-foco">
            <MdClose />
          </button>
        </div>
        <div className="overflow-y-auto px-6 py-5">{children}</div>
        {pie && <div className="flex flex-wrap justify-end gap-2 border-t border-borde px-6 py-4">{pie}</div>}
      </div>
    </div>,
    document.body,
  )
}
