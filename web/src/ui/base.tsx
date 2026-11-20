import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { MdErrorOutline, MdInbox, MdRefresh } from 'react-icons/md'

export const cn = (...partes: (string | false | null | undefined)[]) => partes.filter(Boolean).join(' ')

// ---------- Botón ----------
type Variante = 'primary' | 'secondary' | 'ghost' | 'destructive'

const VARIANTES: Record<Variante, string> = {
  primary: 'bg-primary-solido text-white hover:bg-primary-pressed active:bg-vino shadow-n1',
  secondary: 'bg-white text-vino border border-borde hover:bg-tinte',
  ghost: 'bg-transparent text-vino hover:bg-tinte',
  destructive: 'bg-estado-baja text-white hover:bg-red-800',
}

interface BotonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variante?: Variante
  cargando?: boolean
  icono?: ReactNode
  pequeno?: boolean
}

export function Button({ variante = 'primary', cargando, icono, pequeno, className, children, disabled, type = 'button', ...resto }: BotonProps) {
  return (
    <button
      type={type}
      disabled={disabled || cargando}
      aria-busy={cargando || undefined}
      className={cn(
        'inline-flex items-center justify-center gap-2 rounded-control font-semibold transition focus-visible:outline-none focus-visible:shadow-foco disabled:cursor-not-allowed disabled:opacity-50',
        pequeno ? 'px-3 py-1.5 text-xs' : 'px-4 py-2.5 text-sm',
        VARIANTES[variante],
        className,
      )}
      {...resto}
    >
      {cargando ? <Spinner /> : icono}
      {children}
    </button>
  )
}

export function Spinner({ className }: { className?: string }) {
  return (
    <span
      aria-hidden
      className={cn('inline-block h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent', className)}
    />
  )
}

// ---------- Tarjeta ----------
export function Card({ className, children, ...r }: { className?: string; children: ReactNode } & React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('min-w-0 rounded-tarjeta border border-borde bg-white p-5 shadow-n1', className)} {...r}>
      {children}
    </div>
  )
}

export function Skeleton({ className }: { className?: string }) {
  return <div aria-hidden className={cn('animate-pulse rounded-control bg-tinte', className)} />
}

// ---------- Estados ----------
export type Tono = 'ok' | 'pendiente' | 'baja' | 'pausado' | 'info'

const TONOS: Record<Tono, string> = {
  ok: 'bg-estado-ok-bg text-estado-ok',
  pendiente: 'bg-estado-pendiente-bg text-estado-pendiente',
  baja: 'bg-estado-baja-bg text-estado-baja',
  pausado: 'bg-estado-pausado-bg text-estado-pausado',
  info: 'bg-tinte text-vino',
}

export function StatusPill({ tono, children, className }: { tono: Tono; children: ReactNode; className?: string }) {
  return (
    <span className={cn('inline-flex items-center whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-semibold', TONOS[tono], className)}>
      {children}
    </span>
  )
}

/** Tono de cada estado del dominio. */
export const tonoDe = (codigo: string): Tono => {
  switch (codigo) {
    case 'ACTIVO': case 'APLICADO': case 'VIGENTE': case 'LIQUIDADO': case 'EXITO': case 'VALIDO': case 'ALTA': case 'CERRADO': case 'ABIERTO_OK':
      return 'ok'
    case 'PENDIENTE': case 'LICENCIA': case 'EN_CURSO': case 'EN_CIERRE': case 'PROGRAMADO': case 'PROYECTADO': case 'PREVIA': case 'ABIERTO': case 'SIN_CAMBIO':
      return 'pendiente'
    case 'BAJA': case 'RECHAZADO': case 'ERROR': case 'CANCELADA':
      return 'baja'
    default:
      return 'pausado'
  }
}

export function KpiCard({ titulo, valor, detalle, icono, cargando }: { titulo: string; valor: ReactNode; detalle?: ReactNode; icono?: ReactNode; cargando?: boolean }) {
  return (
    <Card className="flex min-w-0 flex-col gap-3">
      <div className="flex items-start justify-between gap-3">
        <p className="text-sm font-medium text-suave">{titulo}</p>
        {icono && <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-control bg-tinte text-xl text-primary-texto">{icono}</span>}
      </div>
      {cargando ? (
        <Skeleton className="h-9 w-3/4" />
      ) : (
        <p className="break-words text-2xl font-extrabold tracking-tight text-vino xl:text-3xl">{valor}</p>
      )}
      {detalle && <p className="text-xs text-suave">{detalle}</p>}
    </Card>
  )
}

export function PageHeader({ titulo, descripcion, acciones }: { titulo: string; descripcion?: string; acciones?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0">
        <h1 className="text-3xl font-extrabold tracking-tight text-vino">{titulo}</h1>
        {descripcion && <p className="mt-1 max-w-2xl text-sm text-suave">{descripcion}</p>}
      </div>
      {acciones && <div className="flex flex-wrap items-center gap-2">{acciones}</div>}
    </div>
  )
}

export function EmptyState({ titulo, detalle, accion }: { titulo?: string; detalle?: string; accion?: ReactNode }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col items-center gap-2 px-4 py-12 text-center" role="status">
      <MdInbox className="text-4xl text-borde" aria-hidden />
      <p className="font-semibold text-vino">{titulo ?? t('estado.vacioTitulo')}</p>
      <p className="max-w-sm text-sm text-suave">{detalle ?? t('estado.vacioDetalle')}</p>
      {accion}
    </div>
  )
}

export function ErrorState({ titulo, detalle, onReintentar }: { titulo?: string; detalle?: string; onReintentar?: () => void }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col items-center gap-2 px-4 py-12 text-center" role="alert">
      <MdErrorOutline className="text-4xl text-estado-baja" aria-hidden />
      <p className="font-semibold text-vino">{titulo ?? t('estado.errorTitulo')}</p>
      <p className="max-w-sm text-sm text-suave">{detalle ?? t('estado.errorDetalle')}</p>
      {onReintentar && (
        <Button variante="secondary" icono={<MdRefresh />} onClick={onReintentar}>
          {t('acciones.reintentar')}
        </Button>
      )}
    </div>
  )
}

/** Barra de avance (préstamos, trabajos). */
export function Progreso({ valor, etiqueta }: { valor: number; etiqueta?: string }) {
  const v = Math.max(0, Math.min(1, valor))
  return (
    <div role="progressbar" aria-valuenow={Math.round(v * 100)} aria-valuemin={0} aria-valuemax={100} aria-label={etiqueta}>
      <div className="h-2.5 w-full overflow-hidden rounded-full bg-tinte">
        <div className="h-full rounded-full bg-primary transition-all" style={{ width: `${v * 100}%` }} />
      </div>
    </div>
  )
}
