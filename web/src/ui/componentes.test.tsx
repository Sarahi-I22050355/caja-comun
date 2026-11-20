import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import type { ReactNode } from 'react'
import { I18nextProvider, useTranslation } from 'react-i18next'
import { describe, expect, it, vi } from 'vitest'
import i18n, { cambiarIdioma } from '../i18n'
import { EmptyState, ErrorState, StatusPill } from './base'
import { DataTable } from './DataTable'
import type { Columnas } from './DataTable'
import { Input } from './form'
import { Modal } from './Modal'

const con = (n: ReactNode) => <I18nextProvider i18n={i18n}>{n}</I18nextProvider>

interface Fila { id: number; nombre: string }
const columnas: Columnas<Fila> = [{ header: 'Nombre', accessorKey: 'nombre' }]

describe('DataTable', () => {
  it('muestra esqueletos mientras carga', () => {
    render(con(<DataTable etiqueta="t" columnas={columnas} datos={undefined} cargando />))
    expect(document.querySelectorAll('.animate-pulse').length).toBeGreaterThan(0)
  })

  it('muestra el estado vacío sin filas', () => {
    render(con(<DataTable etiqueta="t" columnas={columnas} datos={[]} />))
    expect(screen.queryByText('Ana')).toBeNull()
    expect(screen.getByText(i18n.t('estado.vacioTitulo'))).toBeInTheDocument()
  })

  it('muestra el error con reintento', async () => {
    const reintentar = vi.fn()
    render(con(<DataTable etiqueta="t" columnas={columnas} datos={undefined} error={new Error('x')} onReintentar={reintentar} />))
    await userEvent.click(screen.getByRole('button', { name: i18n.t('acciones.reintentar') }))
    expect(reintentar).toHaveBeenCalled()
  })

  it('pinta las filas', () => {
    render(con(<DataTable etiqueta="t" columnas={columnas} datos={[{ id: 1, nombre: 'Ana' }, { id: 2, nombre: 'Luis' }]} />))
    expect(screen.getByText('Ana')).toBeInTheDocument()
    expect(screen.getByText('Luis')).toBeInTheDocument()
  })
})

describe('Modal', () => {
  it('no renderiza nada cerrado y se cierra con Escape', async () => {
    const cerrar = vi.fn()
    const { rerender } = render(con(<Modal abierto={false} onCerrar={cerrar} titulo="Hola">contenido</Modal>))
    expect(screen.queryByRole('dialog')).toBeNull()
    rerender(con(<Modal abierto onCerrar={cerrar} titulo="Hola">contenido</Modal>))
    expect(screen.getByRole('dialog', { name: 'Hola' })).toBeInTheDocument()
    await userEvent.keyboard('{Escape}')
    expect(cerrar).toHaveBeenCalled()
  })
})

describe('estados', () => {
  it('el chip de estado muestra su texto', () => {
    render(con(<StatusPill tono="ok">Activo</StatusPill>))
    expect(screen.getByText('Activo')).toBeInTheDocument()
  })

  it('vacío y error anuncian su mensaje', () => {
    render(con(<><EmptyState titulo="Nada aquí" /><ErrorState detalle="Falló" /></>))
    expect(screen.getByText('Nada aquí')).toBeInTheDocument()
    expect(screen.getByText('Falló')).toBeInTheDocument()
  })
})

describe('idioma', () => {
  function Demo() {
    const { t } = useTranslation()
    const [valor, setValor] = useState('')
    return (
      <>
        <Input etiqueta={t('acciones.guardar')} value={valor} onChange={(e) => setValor(e.target.value)} />
        <button type="button" onClick={() => void cambiarIdioma('en')}>cambiar</button>
      </>
    )
  }

  it('cambiar de idioma traduce la interfaz sin perder lo escrito', async () => {
    render(con(<Demo />))
    await userEvent.type(screen.getByLabelText(i18n.t('acciones.guardar', { lng: 'es' })), 'Ana López')
    await act(async () => { await userEvent.click(screen.getByRole('button', { name: 'cambiar' })) })
    expect(screen.getByLabelText(i18n.t('acciones.guardar', { lng: 'en' }))).toHaveValue('Ana López')
    expect(document.documentElement.lang).toBe('en')
  })
})
