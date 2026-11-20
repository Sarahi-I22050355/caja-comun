import { createContext, useCallback, useContext, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { MdCheckCircle, MdErrorOutline } from 'react-icons/md'
import { cn } from './base'

interface Aviso {
  id: number
  tipo: 'ok' | 'error'
  texto: string
}

interface Api {
  ok: (texto: string) => void
  error: (texto: string) => void
}

const Contexto = createContext<Api>({ ok: () => {}, error: () => {} })

export const useToast = () => useContext(Contexto)

let contador = 0

/** Avisos efímeros. Reemplazan a alert(): se anuncian a lectores de pantalla y se cierran solos. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [avisos, setAvisos] = useState<Aviso[]>([])

  const agregar = useCallback((tipo: Aviso['tipo'], texto: string) => {
    const id = ++contador
    setAvisos((a) => [...a, { id, tipo, texto }])
    setTimeout(() => setAvisos((a) => a.filter((x) => x.id !== id)), tipo === 'error' ? 7000 : 4000)
  }, [])

  const api = useMemo<Api>(() => ({ ok: (t) => agregar('ok', t), error: (t) => agregar('error', t) }), [agregar])

  return (
    <Contexto.Provider value={api}>
      {children}
      <div className="pointer-events-none fixed bottom-4 right-4 z-[60] flex w-[min(92vw,380px)] flex-col gap-2" aria-live="polite">
        {avisos.map((a) => (
          <div
            key={a.id}
            role={a.tipo === 'error' ? 'alert' : 'status'}
            className={cn(
              'pointer-events-auto flex items-start gap-2 rounded-control border bg-white px-4 py-3 text-sm shadow-n2',
              a.tipo === 'error' ? 'border-estado-baja text-estado-baja' : 'border-estado-ok text-estado-ok',
            )}
          >
            {a.tipo === 'error' ? <MdErrorOutline className="mt-0.5 shrink-0 text-lg" aria-hidden /> : <MdCheckCircle className="mt-0.5 shrink-0 text-lg" aria-hidden />}
            <span className="text-vino">{a.texto}</span>
          </div>
        ))}
      </div>
    </Contexto.Provider>
  )
}
