import type { TFunction } from 'i18next'
import { ApiError } from './api'

export function codigoDe(e: unknown): string {
  if (e instanceof ApiError) return e.code
  if (e instanceof TypeError) return 'red'
  return 'generico'
}

/** Traduce un código de la API. Si no hay traducción se muestra el mensaje genérico, nunca el texto del servidor. */
export function textoCodigo(t: TFunction, codigo: string): string {
  const base = codigo.split(':')[0]
  return t(`comun:errores.${base}`, { defaultValue: t('comun:errores.generico') })
}

export function textoError(t: TFunction, e: unknown): string {
  return textoCodigo(t, codigoDe(e))
}

/** Un mensaje por campo con el primer código que devolvió la validación (los campos llegan en camelCase). */
export function erroresPorCampo(t: TFunction, e: unknown): Record<string, string> {
  const salida: Record<string, string> = {}
  if (e instanceof ApiError && e.errors) {
    for (const [campo, codigos] of Object.entries(e.errors)) if (codigos[0]) salida[campo] = textoCodigo(t, codigos[0])
  }
  return salida
}

/** El primer código de validación de cada campo (camelCase). Con `alias` se renombran los campos del servidor a los del formulario. */
export function codigosPorCampo(e: unknown, alias: Record<string, string> = {}): Record<string, string> {
  const salida: Record<string, string> = {}
  if (e instanceof ApiError && e.errors) {
    for (const [campo, codigos] of Object.entries(e.errors)) if (codigos[0]) salida[alias[campo] ?? campo] = codigos[0]
  }
  return salida
}
