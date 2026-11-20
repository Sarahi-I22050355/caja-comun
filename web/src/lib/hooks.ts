import { useEffect, useState } from 'react'

export function useDebounce<T>(valor: T, ms = 350): T {
  const [v, setV] = useState(valor)
  useEffect(() => {
    const id = setTimeout(() => setV(valor), ms)
    return () => clearTimeout(id)
  }, [valor, ms])
  return v
}

/** Estado guardado en localStorage (con tolerancia a que el almacenamiento no exista). */
export function useAlmacenado<T extends string>(clave: string, inicial: T): [T, (v: T) => void] {
  const [v, setV] = useState<T>(() => {
    try {
      return (localStorage.getItem(clave) as T | null) ?? inicial
    } catch {
      return inicial
    }
  })
  const fijar = (nuevo: T) => {
    setV(nuevo)
    try {
      localStorage.setItem(clave, nuevo)
    } catch {
      /* sin almacenamiento */
    }
  }
  return [v, fijar]
}

export function useMedia(consulta: string): boolean {
  const [coincide, setCoincide] = useState(() => (typeof window !== 'undefined' ? window.matchMedia(consulta).matches : false))
  useEffect(() => {
    const m = window.matchMedia(consulta)
    const f = () => setCoincide(m.matches)
    f()
    m.addEventListener('change', f)
    return () => m.removeEventListener('change', f)
  }, [consulta])
  return coincide
}
