import { MdCheck } from 'react-icons/md'
import { cn } from './base'

export interface Paso {
  titulo: string
  detalle?: string
}

/** Indicador de pasos. Los pasos anteriores al actual aparecen completos y se pueden elegir para volver. */
export function Stepper({ pasos, actual, onElegir }: { pasos: Paso[]; actual: number; onElegir?: (i: number) => void }) {
  return (
    <ol className="flex flex-wrap items-center gap-2 sm:gap-4" aria-label="Pasos">
      {pasos.map((p, i) => {
        const hecho = i < actual
        const activo = i === actual
        const clic = onElegir && i <= actual
        return (
          <li key={p.titulo} className="flex items-center gap-2 sm:gap-4" aria-current={activo ? 'step' : undefined}>
            <button
              type="button"
              disabled={!clic}
              onClick={() => clic && onElegir?.(i)}
              className="flex items-center gap-3 rounded-control text-left focus-visible:outline-none focus-visible:shadow-foco disabled:cursor-default"
            >
              <span
                className={cn(
                  'flex h-9 w-9 shrink-0 items-center justify-center rounded-full border-2 text-sm font-bold',
                  hecho && 'border-estado-ok bg-estado-ok text-white',
                  activo && 'border-primary bg-primary-solido text-white',
                  !hecho && !activo && 'border-borde bg-white text-suave',
                )}
              >
                {hecho ? <MdCheck aria-hidden /> : i + 1}
              </span>
              <span className="min-w-0">
                <span className={cn('block text-sm font-semibold', activo ? 'text-vino' : 'text-suave')}>{p.titulo}</span>
                {p.detalle && <span className="hidden text-xs text-suave sm:block">{p.detalle}</span>}
              </span>
            </button>
            {i < pasos.length - 1 && <span aria-hidden className={cn('hidden h-0.5 w-8 sm:block lg:w-16', hecho ? 'bg-estado-ok' : 'bg-borde')} />}
          </li>
        )
      })}
    </ol>
  )
}
