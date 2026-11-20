import { forwardRef, useId, useRef, useState } from 'react'
import type { InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react'
import { useTranslation } from 'react-i18next'
import { MdCloudUpload, MdSearch } from 'react-icons/md'
import { cn } from './base'

const CONTROL =
  'w-full rounded-control border bg-white px-3 py-2.5 text-sm text-vino placeholder:text-suave/60 focus:border-primary focus:outline-none focus:shadow-foco disabled:bg-lienzo disabled:text-suave'

export function Field({ etiqueta, error, ayuda, id, children }: { etiqueta: string; error?: string; ayuda?: string; id: string; children: ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <label htmlFor={id} className="text-xs font-semibold uppercase tracking-wide text-suave">
        {etiqueta}
      </label>
      {children}
      {ayuda && !error && <p className="text-xs text-suave">{ayuda}</p>}
      {error && (
        <p id={`${id}-error`} role="alert" className="text-xs font-medium text-estado-baja">
          {error}
        </p>
      )}
    </div>
  )
}

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  etiqueta: string
  error?: string
  ayuda?: string
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input({ etiqueta, error, ayuda, className, id, ...r }, ref) {
  const auto = useId()
  const ident = id ?? auto
  return (
    <Field etiqueta={etiqueta} error={error} ayuda={ayuda} id={ident}>
      <input
        ref={ref}
        id={ident}
        aria-invalid={!!error}
        aria-describedby={error ? `${ident}-error` : undefined}
        className={cn(CONTROL, error ? 'border-estado-baja' : 'border-borde', className)}
        {...r}
      />
    </Field>
  )
})

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  etiqueta?: string
  error?: string
  opciones?: { valor: string | number; texto: string }[]
  /** Sin etiqueta visible: se usa como nombre accesible. */
  nombre?: string
}

export const Select = forwardRef<HTMLSelectElement, SelectProps>(function Select({ etiqueta, error, opciones, nombre, className, id, children, ...r }, ref) {
  const auto = useId()
  const ident = id ?? auto
  const cuerpo = (
    <select
      ref={ref}
      id={ident}
      aria-label={etiqueta ? undefined : nombre}
      aria-invalid={!!error}
      className={cn(CONTROL, 'pr-8', error ? 'border-estado-baja' : 'border-borde', className)}
      {...r}
    >
      {opciones?.map((o) => (
        <option key={o.valor} value={o.valor}>
          {o.texto}
        </option>
      ))}
      {children}
    </select>
  )
  return etiqueta ? (
    <Field etiqueta={etiqueta} error={error} id={ident}>
      {cuerpo}
    </Field>
  ) : (
    cuerpo
  )
})

export function Toggle({ activo, onCambio, etiqueta, deshabilitado }: { activo: boolean; onCambio: (v: boolean) => void; etiqueta: string; deshabilitado?: boolean }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={activo}
      aria-label={etiqueta}
      disabled={deshabilitado}
      onClick={() => onCambio(!activo)}
      className={cn(
        'relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition focus-visible:outline-none focus-visible:shadow-foco disabled:opacity-50',
        activo ? 'bg-primary' : 'bg-borde',
      )}
    >
      <span className={cn('inline-block h-5 w-5 rounded-full bg-white shadow transition', activo ? 'translate-x-5' : 'translate-x-0.5')} />
    </button>
  )
}

export function SearchInput({ valor, onCambio, placeholder }: { valor: string; onCambio: (v: string) => void; placeholder: string }) {
  return (
    <div className="relative min-w-[220px] flex-1">
      <MdSearch aria-hidden className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-xl text-suave" />
      <input
        type="search"
        value={valor}
        onChange={(e) => onCambio(e.target.value)}
        placeholder={placeholder}
        aria-label={placeholder}
        className={cn(CONTROL, 'border-borde pl-10')}
      />
    </div>
  )
}

/** Zona para soltar o elegir un archivo. La validación de tamaño y extensión es de aviso: la definitiva es del servidor. */
export function FileDropzone({ extensiones, maxBytes, onArchivo, deshabilitado, texto, ayuda }: {
  extensiones: string[]
  maxBytes: number
  onArchivo: (f: File) => void
  deshabilitado?: boolean
  texto: string
  ayuda: string
}) {
  const { t } = useTranslation('sincronizacion')
  const entrada = useRef<HTMLInputElement>(null)
  const [arrastrando, setArrastrando] = useState(false)
  const [aviso, setAviso] = useState<string | null>(null)

  const tomar = (f: File | undefined) => {
    if (!f) return
    const ext = f.name.slice(f.name.lastIndexOf('.')).toLowerCase()
    if (!extensiones.includes(ext)) return setAviso(t('dropzone.extension', { extensiones: extensiones.join(', ') }))
    if (f.size > maxBytes) return setAviso(t('dropzone.tamano', { mb: maxBytes / 1024 / 1024 }))
    setAviso(null)
    onArchivo(f)
  }

  return (
    <div>
      <button
        type="button"
        disabled={deshabilitado}
        onClick={() => entrada.current?.click()}
        onDragOver={(e) => { e.preventDefault(); setArrastrando(true) }}
        onDragLeave={() => setArrastrando(false)}
        onDrop={(e) => { e.preventDefault(); setArrastrando(false); tomar(e.dataTransfer.files[0]) }}
        className={cn(
          'flex w-full flex-col items-center gap-2 rounded-tarjeta border-2 border-dashed px-6 py-10 text-center transition focus-visible:outline-none focus-visible:shadow-foco disabled:opacity-50',
          arrastrando ? 'border-primary bg-tinte' : 'border-borde bg-lienzo hover:bg-tinte',
        )}
      >
        <MdCloudUpload aria-hidden className="text-4xl text-primary-texto" />
        <span className="font-semibold text-vino">{texto}</span>
        <span className="text-xs text-suave">{ayuda}</span>
      </button>
      <input ref={entrada} type="file" hidden accept={extensiones.join(',')} onChange={(e) => { tomar(e.target.files?.[0]); e.target.value = '' }} />
      {aviso && <p role="alert" className="mt-2 text-sm font-medium text-estado-baja">{aviso}</p>}
    </div>
  )
}
