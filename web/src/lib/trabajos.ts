import { useQuery } from '@tanstack/react-query'
import { api } from './api'
import type { Trabajo } from './tipos'

/** Consulta un trabajo en segundo plano cada segundo hasta que termina (éxito o error). */
export function useTrabajo(id: string | null) {
  return useQuery({
    queryKey: ['trabajo', id],
    queryFn: () => api.get<Trabajo>(`/trabajos/${id}`),
    enabled: !!id,
    refetchInterval: (q) => (q.state.data && (q.state.data.estado === 'EXITO' || q.state.data.estado === 'ERROR') ? false : 800),
    staleTime: 0,
    retry: false,
  })
}
