import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import comun from '../modulos/comun/i18n'
import inicio from '../modulos/inicio/i18n'
import cajas from '../modulos/cajas/i18n'
import ahorradores from '../modulos/ahorradores/i18n'
import sincronizacion from '../modulos/sincronizacion/i18n'
import nomina from '../modulos/nomina/i18n'
import voluntario from '../modulos/voluntario/i18n'
import prestamos from '../modulos/prestamos/i18n'
import cierre from '../modulos/cierre/i18n'
import reportes from '../modulos/reportes/i18n'
import tareas from '../modulos/tareas/i18n'
import usuarios from '../modulos/usuarios/i18n'
import consultas from '../modulos/consultas/i18n'
import login from '../modulos/login/i18n'

// Cada módulo trae sus textos en español e inglés. Solo se traduce la interfaz: los datos, las claves de concepto,
// los layouts y los nombres de archivo no pasan por aquí.
const modulos = { comun, login, inicio, cajas, ahorradores, sincronizacion, nomina, voluntario, prestamos, cierre, reportes, tareas, usuarios, consultas }

export const IDIOMAS = ['es', 'en'] as const
export type Idioma = (typeof IDIOMAS)[number]

const CLAVE = 'caja-comun.idioma'

export function idiomaGuardado(): Idioma {
  try {
    const v = localStorage.getItem(CLAVE)
    if (v === 'es' || v === 'en') return v
  } catch {
    /* sin almacenamiento */
  }
  return 'es'
}

export function cambiarIdioma(i: Idioma) {
  try {
    localStorage.setItem(CLAVE, i)
  } catch {
    /* sin almacenamiento */
  }
  document.documentElement.lang = i
  return i18n.changeLanguage(i)
}

const resources = Object.fromEntries(
  (['es', 'en'] as const).map((l) => [l, Object.fromEntries(Object.entries(modulos).map(([ns, t]) => [ns, t[l]]))]),
)

void i18n.use(initReactI18next).init({
  resources,
  lng: idiomaGuardado(),
  fallbackLng: 'es',
  ns: Object.keys(modulos),
  defaultNS: 'comun',
  interpolation: { escapeValue: false },
  returnNull: false,
})
document.documentElement.lang = idiomaGuardado()

export default i18n
