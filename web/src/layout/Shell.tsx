import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import {
  MdAdminPanelSettings, MdApartment, MdBarChart, MdCalendarToday, MdEventAvailable, MdGroups, MdHome, MdHub, MdLock, MdLogout, MdMenu,
  MdReceiptLong, MdRequestQuote, MdSavings, MdSchedule, MdSyncAlt, MdChevronLeft, MdChevronRight,
} from 'react-icons/md'
import type { IconType } from 'react-icons'
import { useCaja } from '../auth/CajaActiva'
import { useSesion } from '../auth/Sesion'
import { api } from '../lib/api'
import { cambiarIdioma } from '../i18n'
import type { Idioma } from '../i18n'
import { iniciales } from '../lib/formato'
import { useMedia } from '../lib/hooks'
import type { Dashboard } from '../lib/tipos'
import { cn } from '../ui/base'
import { Select } from '../ui/form'

interface Item {
  clave: string
  ruta: string
  icono: IconType
  soloAdmin?: boolean
}

// Los 12 elementos llevan siempre su ícono (react-icons/md, importados uno por uno para que haya tree-shaking).
const ITEMS: Item[] = [
  { clave: 'inicio', ruta: '/', icono: MdHome },
  { clave: 'ahorradores', ruta: '/ahorradores', icono: MdGroups },
  { clave: 'sincronizacion', ruta: '/sincronizacion', icono: MdSyncAlt },
  { clave: 'nomina', ruta: '/nomina', icono: MdReceiptLong },
  { clave: 'voluntario', ruta: '/ahorro-voluntario', icono: MdSavings },
  { clave: 'prestamos', ruta: '/prestamos', icono: MdRequestQuote },
  { clave: 'cierre', ruta: '/cierre-ciclo', icono: MdEventAvailable },
  { clave: 'reportes', ruta: '/reportes', icono: MdBarChart },
  { clave: 'tareas', ruta: '/tareas', icono: MdSchedule },
  { clave: 'consultas', ruta: '/consultas', icono: MdHub },
  { clave: 'usuarios', ruta: '/usuarios', icono: MdAdminPanelSettings, soloAdmin: true },
  { clave: 'cajas', ruta: '/cajas', icono: MdApartment },
]

export function LangSwitch() {
  const { t, i18n } = useTranslation()
  return (
    <div role="group" aria-label={t('header.idioma')} className="flex rounded-full border border-borde bg-tinte p-0.5 text-xs font-bold">
      {(['es', 'en'] as Idioma[]).map((l) => (
        <button
          key={l}
          type="button"
          aria-pressed={i18n.language === l}
          onClick={() => void cambiarIdioma(l)}
          className={cn('rounded-full px-3 py-1 transition focus-visible:outline-none focus-visible:shadow-foco', i18n.language === l ? 'bg-white text-vino shadow-n1' : 'text-suave hover:text-vino')}
        >
          {l.toUpperCase()}
        </button>
      ))}
    </div>
  )
}

export function CajaSwitcher() {
  const { t } = useTranslation()
  const { cajas, cajaId, elegir } = useCaja()
  if (!cajas?.length) return <span className="text-sm text-suave">{t('header.sinCajas')}</span>
  return (
    <Select
      nombre={t('header.caja')}
      value={cajaId ?? ''}
      onChange={(e) => elegir(Number(e.target.value))}
      opciones={cajas.map((c) => ({ valor: c.id, texto: c.nombre }))}
      className="!w-auto max-w-[150px] !rounded-full !py-2 font-semibold sm:max-w-[240px]"
    />
  )
}

function Marca({ compacto }: { compacto?: boolean }) {
  const { t } = useTranslation()
  return (
    <div className="flex items-center gap-3">
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-control border border-borde bg-white text-2xl text-primary-texto shadow-n1">
        <MdSavings aria-hidden />
      </span>
      {!compacto && (
        <div className="min-w-0 leading-tight">
          <p className="text-lg font-extrabold text-vino">{t('app.nombre')}</p>
          <p className="truncate text-[11px] font-semibold text-suave">{t('app.subtitulo')}</p>
        </div>
      )}
    </div>
  )
}

function Navegacion({ compacto, alNavegar }: { compacto: boolean; alNavegar?: () => void }) {
  const { t } = useTranslation()
  const { usuario } = useSesion()
  return (
    <nav aria-label={t('nav.principal')} className="flex flex-col gap-1 px-3">
      {ITEMS.filter((i) => !i.soloAdmin || usuario?.rol === 'ADMINISTRADOR').map((i) => {
        const Icono = i.icono
        return (
          <NavLink
            key={i.clave}
            to={i.ruta}
            end={i.ruta === '/'}
            onClick={alNavegar}
            title={compacto ? t(`nav.${i.clave}`) : undefined}
            className={({ isActive }) =>
              cn(
                'flex items-center gap-3 rounded-control border-l-4 px-3 py-2.5 text-sm font-medium transition focus-visible:outline-none focus-visible:shadow-foco',
                compacto && 'justify-center px-0',
                isActive ? 'border-primary bg-tinte font-semibold text-primary-texto' : 'border-transparent text-suave hover:bg-white/70 hover:text-vino',
              )
            }
          >
            <Icono aria-hidden className="shrink-0 text-xl" />
            <span className={compacto ? 'sr-only' : 'truncate'}>{t(`nav.${i.clave}`)}</span>
          </NavLink>
        )
      })}
    </nav>
  )
}

export default function Shell() {
  const { t } = useTranslation()
  const { usuario, cerrar } = useSesion()
  const { cajaId } = useCaja()
  const navegar = useNavigate()
  const ubicacion = useLocation()
  const pantallaChica = useMedia('(max-width: 1023px)')
  const intermedia = useMedia('(min-width: 1024px) and (max-width: 1279px)')
  const [movil, setMovil] = useState(false)
  const [manual, setManual] = useState<boolean | null>(null)
  const [menuPerfil, setMenuPerfil] = useState(false)

  // Entre 1024 y 1279 px el menú se contrae solo; en pantallas grandes el usuario decide.
  const compacto = !pantallaChica && (manual ?? intermedia)
  useEffect(() => setMovil(false), [ubicacion.pathname])

  const ciclo = useQuery({
    queryKey: ['dashboard', cajaId],
    queryFn: () => api.get<Dashboard>('/dashboard', { cajaId }),
    enabled: cajaId !== undefined,
    staleTime: 60_000,
  })

  const barra = (
    <div className="flex h-full flex-col bg-gradient-to-b from-lienzo to-tinte">
      <div className={cn('flex h-16 items-center border-b border-borde/70', compacto ? 'justify-center px-2' : 'px-5')}>
        <Marca compacto={compacto} />
      </div>
      <div className="flex-1 overflow-y-auto py-4">
        <Navegacion compacto={compacto} alNavegar={() => setMovil(false)} />
      </div>
      <div className={cn('flex items-center gap-2 border-t border-borde/70 px-4 py-3 text-[11px] font-semibold text-suave', compacto && 'justify-center px-0')}>
        <MdLock aria-hidden className="shrink-0 text-base" />
        <span className={compacto ? 'sr-only' : ''}>{t('header.conexionSegura')}</span>
      </div>
    </div>
  )

  return (
    <div className="min-h-screen">
      <a href="#contenido" className="sr-only focus:not-sr-only focus:fixed focus:left-2 focus:top-2 focus:z-[70] focus:rounded-control focus:bg-white focus:px-3 focus:py-2 focus:text-vino">
        {t('nav.inicio')}
      </a>

      <aside className={cn('fixed inset-y-0 left-0 z-30 hidden border-r border-borde transition-[width] lg:block', compacto ? 'w-20' : 'w-[280px]')}>
        {barra}
        {!intermedia && (
          <button
            type="button"
            onClick={() => setManual(!compacto)}
            aria-label={compacto ? t('nav.expandir') : t('nav.colapsar')}
            className="absolute -right-3 top-20 flex h-6 w-6 items-center justify-center rounded-full border border-borde bg-white text-vino shadow-n1 focus-visible:outline-none focus-visible:shadow-foco"
          >
            {compacto ? <MdChevronRight aria-hidden /> : <MdChevronLeft aria-hidden />}
          </button>
        )}
      </aside>

      {movil && (
        <div className="fixed inset-0 z-40 lg:hidden" role="dialog" aria-modal="true" aria-label={t('nav.principal')}>
          <div className="absolute inset-0 bg-vino/40" onClick={() => setMovil(false)} />
          <div className="absolute inset-y-0 left-0 w-[280px] max-w-[85vw] shadow-n3">{barra}</div>
        </div>
      )}

      <div className={cn('transition-[padding]', compacto ? 'lg:pl-20' : 'lg:pl-[280px]')}>
        <header className="sticky top-0 z-20 flex h-16 items-center gap-2 border-b sm:gap-3 border-borde bg-white/90 px-4 backdrop-blur lg:px-8">
          <button type="button" onClick={() => setMovil(true)} aria-label={t('nav.abrir')} className="rounded-control p-2 text-2xl text-vino hover:bg-tinte focus-visible:outline-none focus-visible:shadow-foco lg:hidden">
            <MdMenu aria-hidden />
          </button>
          <div className="min-w-0 shrink"><CajaSwitcher /></div>
          {ciclo.data && (
            <span className="hidden items-center gap-1.5 rounded-full bg-tinte px-3 py-1 text-xs font-semibold text-primary-texto sm:inline-flex">
              <MdCalendarToday aria-hidden /> {t('header.ciclo', { anio: ciclo.data.anioCiclo })}
            </span>
          )}
          <div className="ml-auto flex shrink-0 items-center gap-2 sm:gap-3">
            <LangSwitch />
            <div className="relative">
              <button
                type="button"
                onClick={() => setMenuPerfil((v) => !v)}
                aria-haspopup="menu"
                aria-expanded={menuPerfil}
                aria-label={t('header.perfil')}
                className="flex items-center gap-3 rounded-full border-l border-borde py-1 pl-3 focus-visible:outline-none focus-visible:shadow-foco"
              >
                <span className="hidden text-right leading-tight md:block">
                  <span className="block text-sm font-semibold text-vino">{usuario?.nombre}</span>
                  <span className="block text-[11px] font-medium text-suave">{usuario ? t(`roles.${usuario.rol}`) : ''}</span>
                </span>
                <span className="flex h-10 w-10 items-center justify-center rounded-full bg-primary-solido text-sm font-bold text-white">{iniciales(usuario?.nombre ?? '')}</span>
              </button>
              {menuPerfil && (
                <div role="menu" className="absolute right-0 top-12 z-30 w-56 rounded-tarjeta border border-borde bg-white p-2 shadow-n2">
                  <p className="truncate px-3 py-2 text-xs text-suave">{usuario?.correo}</p>
                  <button
                    role="menuitem"
                    type="button"
                    onClick={async () => { setMenuPerfil(false); await cerrar(); navegar('/login') }}
                    className="flex w-full items-center gap-2 rounded-control px-3 py-2 text-sm font-semibold text-vino hover:bg-tinte"
                  >
                    <MdLogout aria-hidden /> {t('header.cerrarSesion')}
                  </button>
                </div>
              )}
            </div>
          </div>
        </header>
        <main id="contenido" className="mx-auto max-w-contenido px-4 py-6 lg:px-8" tabIndex={-1}>
          <Outlet />
        </main>
      </div>
    </div>
  )
}
