import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdClose } from 'react-icons/md'
import { api } from '../lib/api'
import { useDebounce } from '../lib/hooks'
import type { AhorradorLista, EstadoAhorrador, Pagina } from '../lib/tipos'
import { Field } from './form'

export interface AhorradorElegido {
  id: number
  numEmpleado: string
  nombre: string
}

/** Busca ahorradores de una caja por nombre o número y devuelve el elegido. */
export function AhorradorPicker({ etiqueta, cajaId, valor, onElegir, estado, excluir, error, id }: {
  etiqueta: string
  cajaId: number | undefined
  valor: AhorradorElegido | null
  onElegir: (a: AhorradorElegido | null) => void
  estado?: EstadoAhorrador
  excluir?: number
  error?: string
  id: string
}) {
  const { t } = useTranslation()
  const [q, setQ] = useState('')
  const [abierto, setAbierto] = useState(false)
  const busqueda = useDebounce(q, 300)
  const r = useQuery({
    queryKey: ['picker', cajaId, busqueda, estado],
    queryFn: () => api.get<Pagina<AhorradorLista>>('/ahorradores', { cajaId, q: busqueda, estado, pageSize: 8 }),
    enabled: abierto && cajaId !== undefined && busqueda.length >= 1,
  })

  return (
    <Field etiqueta={etiqueta} error={error} id={id}>
      {valor ? (
        <div className="flex items-center justify-between gap-2 rounded-control border border-borde bg-lienzo px-3 py-2.5 text-sm">
          <span className="min-w-0 truncate"><b className="text-vino">{valor.numEmpleado}</b> · {valor.nombre}</span>
          <button type="button" onClick={() => { onElegir(null); setQ('') }} aria-label={t('acciones.limpiar')} className="rounded-full p-1 text-suave hover:bg-tinte"><MdClose aria-hidden /></button>
        </div>
      ) : (
        <div className="relative">
          <input
            id={id}
            value={q}
            autoComplete="off"
            role="combobox"
            aria-expanded={abierto}
            aria-controls={`${id}-lista`}
            onChange={(e) => { setQ(e.target.value); setAbierto(true) }}
            onFocus={() => setAbierto(true)}
            onBlur={() => setTimeout(() => setAbierto(false), 150)}
            placeholder={t('acciones.buscar')}
            className="w-full rounded-control border border-borde bg-white px-3 py-2.5 text-sm text-vino focus:border-primary focus:outline-none focus:shadow-foco"
          />
          {abierto && busqueda.length >= 1 && (
            <ul id={`${id}-lista`} role="listbox" className="absolute z-20 mt-1 max-h-60 w-full overflow-y-auto rounded-control border border-borde bg-white py-1 shadow-n2">
              {r.isLoading && <li className="px-3 py-2 text-sm text-suave">{t('estado.cargando')}</li>}
              {r.data?.items.filter((a) => a.id !== excluir).length === 0 && <li className="px-3 py-2 text-sm text-suave">{t('estado.vacioTitulo')}</li>}
              {r.data?.items.filter((a) => a.id !== excluir).map((a) => (
                <li key={a.id} role="option" aria-selected={false}>
                  <button
                    type="button"
                    onMouseDown={(e) => e.preventDefault()}
                    onClick={() => { onElegir({ id: a.id, numEmpleado: a.numEmpleado, nombre: `${a.nombre} ${a.apellidos}` }); setAbierto(false) }}
                    className="flex w-full flex-col px-3 py-2 text-left text-sm hover:bg-tinte"
                  >
                    <span className="font-semibold text-vino">{a.nombre} {a.apellidos}</span>
                    <span className="text-xs text-suave">{a.numEmpleado} · {a.departamento}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </Field>
  )
}
