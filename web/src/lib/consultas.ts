import { useQuery } from '@tanstack/react-query'
import { api } from './api'
import type { Periodo } from './tipos'

export const usePeriodos = (cajaId: number | undefined, anio?: number) =>
  useQuery({
    queryKey: ['periodos', cajaId, anio ?? 'todos'],
    queryFn: () => api.get<Periodo[]>(`/cajas/${cajaId}/periodos`, { anio }),
    enabled: cajaId !== undefined,
    staleTime: 15_000,
  })

/** Anios en los que una caja tiene periodos, del más reciente al más antiguo. */
export const usePeriodosCaja = (cajaId: number | undefined) =>
  useQuery({
    queryKey: ['periodos', cajaId, 'todos'],
    queryFn: () => api.get<Periodo[]>(`/cajas/${cajaId}/periodos`),
    enabled: cajaId !== undefined,
    staleTime: 15_000,
  })

export const referenciaPeriodo = (p: Periodo, frecuencia: 'QUINCENAL' | 'SEMANAL' = 'QUINCENAL') =>
  `${frecuencia === 'QUINCENAL' ? 'Q' : 'S'}${p.numero}-${p.anio}`
