// Formato de importes, fechas y porcentajes. Los importes son siempre MXN con punto decimal y coma de miles (igual en
// español e inglés); las fechas y los nombres de mes siguen el idioma de la interfaz.

import i18n from '../i18n'

const numero = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const entero = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 })

export const dinero = (v: number | null | undefined, moneda = false): string =>
  v === null || v === undefined ? '—' : `${v < 0 ? '-' : ''}$${numero.format(Math.abs(v))}${moneda ? ' MXN' : ''}`

export const dineroCorto = (v: number): string => {
  const a = Math.abs(v)
  const s = a >= 1_000_000 ? `${(a / 1_000_000).toFixed(2)}M` : a >= 1_000 ? `${(a / 1_000).toFixed(1)}k` : a.toFixed(0)
  return `${v < 0 ? '-' : ''}$${s}`
}

export const entero_ = (v: number): string => entero.format(v)

export const decimal = (v: number): string => numero.format(v)

export const porcentaje = (v: number, decimales = 2): string => `${(v * 100).toFixed(decimales)} %`

const localeActual = (): string => (i18n.language?.startsWith('en') ? 'en-US' : 'es-MX')

/** Una fecha de calendario (AAAA-MM-DD) se muestra sin convertirla de zona horaria. */
export function fecha(valor: string | null | undefined, estilo: 'corta' | 'larga' = 'corta'): string {
  if (!valor) return '—'
  const soloFecha = valor.length === 10
  const d = new Date(soloFecha ? `${valor}T00:00:00Z` : valor)
  if (Number.isNaN(d.getTime())) return valor
  return new Intl.DateTimeFormat(localeActual(), {
    day: '2-digit',
    month: estilo === 'larga' ? 'long' : 'short',
    year: 'numeric',
    ...(soloFecha ? { timeZone: 'UTC' } : {}),
  })
    .format(d)
    .replace('.', '')
}

/** Fecha y hora en la zona de la Ciudad de México. */
export function fechaHora(valor: string | null | undefined): string {
  if (!valor) return '—'
  const d = new Date(valor.endsWith('Z') || valor.includes('+') ? valor : `${valor}Z`)
  if (Number.isNaN(d.getTime())) return valor
  return new Intl.DateTimeFormat(localeActual(), {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'America/Mexico_City',
  })
    .format(d)
    .replace('.', '')
}

export const nombreMes = (mes: number, largo = false): string =>
  new Intl.DateTimeFormat(localeActual(), { month: largo ? 'long' : 'short', timeZone: 'UTC' })
    .format(new Date(Date.UTC(2024, mes - 1, 1)))
    .replace('.', '')

export const iniciales = (nombre: string): string =>
  nombre
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase())
    .join('')

/** Hora HH:mm de un valor "HH:mm:ss". */
export const hora = (v: string): string => v.slice(0, 5)
