import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { api, refrescar, sesion } from '../lib/api'
import type { Rol, Sesion as SesionApi, Usuario } from '../lib/tipos'

type Accion = 'leer' | 'capturar' | 'administrar'

interface Contexto {
  usuario: Usuario | null
  /** Verdadero mientras se intenta recuperar la sesión con la cookie de refresco. */
  iniciando: boolean
  iniciar: (correo: string, password: string) => Promise<void>
  cerrar: () => Promise<void>
  puede: (accion: Accion) => boolean
}

const Ctx = createContext<Contexto | null>(null)

/** La misma matriz que el servidor (Domain.Permisos): Consulta lee; Capturista lee y captura; Administrador todo. */
export function permitido(rol: Rol | undefined, accion: Accion): boolean {
  if (!rol) return false
  if (accion === 'leer') return true
  if (accion === 'capturar') return rol !== 'CONSULTA'
  return rol === 'ADMINISTRADOR'
}

export function SesionProvider({ children }: { children: ReactNode }) {
  const cache = useQueryClient()
  const [usuario, setUsuario] = useState<Usuario | null>(null)
  const [iniciando, setIniciando] = useState(true)

  const limpiar = useCallback(() => {
    sesion.fijarToken(null)
    setUsuario(null)
    cache.clear()
  }, [cache])

  useEffect(() => {
    sesion.alVencer(limpiar)
    let activo = true
    ;(async () => {
      if (await refrescar()) {
        try {
          const u = await api.get<Usuario>('/auth/me')
          if (activo) setUsuario(u)
        } catch {
          /* sin sesión */
        }
      }
      if (activo) setIniciando(false)
    })()
    return () => {
      activo = false
      sesion.alVencer(null)
    }
  }, [limpiar])

  const iniciar = useCallback(async (correo: string, password: string) => {
    const s = await api.post<SesionApi>('/auth/login', { correo, password })
    sesion.fijarToken(s.accessToken)
    setUsuario(s.usuario)
  }, [])

  const cerrar = useCallback(async () => {
    try {
      await api.post('/auth/logout')
    } catch {
      /* se cierra igual del lado del cliente */
    }
    limpiar()
  }, [limpiar])

  const valor = useMemo<Contexto>(
    () => ({ usuario, iniciando, iniciar, cerrar, puede: (a) => permitido(usuario?.rol, a) }),
    [usuario, iniciando, iniciar, cerrar],
  )
  return <Ctx.Provider value={valor}>{children}</Ctx.Provider>
}

export function useSesion(): Contexto {
  const c = useContext(Ctx)
  if (!c) throw new Error('useSesion fuera de SesionProvider')
  return c
}
