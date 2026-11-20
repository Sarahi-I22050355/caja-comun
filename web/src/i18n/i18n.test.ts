import { describe, expect, it } from 'vitest'
import i18n from '.'

interface Arbol { [k: string]: string | Arbol }

function claves(o: Arbol, prefijo = ''): string[] {
  return Object.entries(o).flatMap(([k, v]) => (typeof v === 'string' ? [`${prefijo}${k}`] : claves(v, `${prefijo}${k}.`)))
}

const ns = ['comun', 'login', 'inicio', 'cajas', 'ahorradores', 'sincronizacion', 'nomina', 'voluntario', 'prestamos', 'cierre', 'reportes', 'tareas', 'usuarios', 'consultas']

describe('traducciones', () => {
  it.each(ns)('el módulo %s tiene las mismas claves en español e inglés', (modulo) => {
    const es = i18n.getResourceBundle('es', modulo) as Arbol
    const en = i18n.getResourceBundle('en', modulo) as Arbol
    expect(claves(en).sort()).toEqual(claves(es).sort())
  })

  it.each(ns)('el módulo %s no deja textos vacíos', (modulo) => {
    for (const l of ['es', 'en']) {
      const b = i18n.getResourceBundle(l, modulo) as Arbol
      for (const c of claves(b)) expect(i18n.t(c, { lng: l, ns: modulo }), `${l}:${modulo}.${c}`).not.toBe('')
    }
  })
})
