import { useQuery } from '@tanstack/react-query'
import { createContext, useContext, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { api } from '../lib/api'
import type { Caja } from '../lib/tipos'
import { useSesion } from './Sesion'

interface Contexto {
  /** Cajas a las que el usuario tiene acceso, con sus datos. */
  cajas: Caja[] | undefined
  cargando: boolean
  cajaId: number | undefined
  caja: Caja | undefined
  elegir: (id: number) => void
}

const Ctx = createContext<Contexto | null>(null)
const CLAVE = 'caja-comun.caja'

/** La caja elegida en el encabezado: todas las pantallas con datos por caja la toman de aquí. */
export function CajaActivaProvider({ children }: { children: ReactNode }) {
  const { usuario } = useSesion()
  const q = useQuery({ queryKey: ['cajas'], queryFn: () => api.get<Caja[]>('/cajas'), enabled: !!usuario, staleTime: 60_000 })
  const [elegida, setElegida] = useState<number | undefined>(() => {
    try {
      const v = Number(localStorage.getItem(CLAVE))
      return Number.isFinite(v) && v > 0 ? v : undefined
    } catch {
      return undefined
    }
  })

  // Si la caja guardada ya no es accesible, se toma la primera.
  const cajaId = q.data?.some((c) => c.id === elegida) ? elegida : q.data?.[0]?.id
  useEffect(() => {
    if (cajaId !== undefined && cajaId !== elegida) setElegida(cajaId)
  }, [cajaId, elegida])

  const valor = useMemo<Contexto>(
    () => ({
      cajas: q.data,
      cargando: q.isLoading,
      cajaId,
      caja: q.data?.find((c) => c.id === cajaId),
      elegir: (id) => {
        setElegida(id)
        try {
          localStorage.setItem(CLAVE, String(id))
        } catch {
          /* sin almacenamiento */
        }
      },
    }),
    [q.data, q.isLoading, cajaId],
  )
  return <Ctx.Provider value={valor}>{children}</Ctx.Provider>
}

export function useCaja(): Contexto {
  const c = useContext(Ctx)
  if (!c) throw new Error('useCaja fuera de CajaActivaProvider')
  return c
}
