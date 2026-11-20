import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdAdd, MdEdit, MdKey } from 'react-icons/md'
import { useCaja } from '../../auth/CajaActiva'
import { useSesion } from '../../auth/Sesion'
import { api } from '../../lib/api'
import { codigosPorCampo, textoCodigo, textoError } from '../../lib/errores'
import { fechaHora, iniciales } from '../../lib/formato'
import type { Rol, UsuarioAdmin } from '../../lib/tipos'
import { Button, Card, ErrorState, PageHeader, StatusPill } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'
import { Input, Select, Toggle } from '../../ui/form'
import { Modal } from '../../ui/Modal'
import { useToast } from '../../ui/Toast'

const ROLES: Rol[] = ['ADMINISTRADOR', 'CAPTURISTA', 'CONSULTA']

function ModalUsuario({ usuario, onCerrar }: { usuario: UsuarioAdmin | null; onCerrar: () => void }) {
  const { t } = useTranslation('usuarios')
  const { t: tc } = useTranslation()
  const { cajas } = useCaja()
  const cache = useQueryClient()
  const toast = useToast()
  const { usuario: yo } = useSesion()
  const [nombre, setNombre] = useState(usuario?.nombre ?? '')
  const [correo, setCorreo] = useState(usuario?.correo ?? '')
  const [password, setPassword] = useState('')
  const [rol, setRol] = useState<Rol>(usuario?.rol ?? 'CAPTURISTA')
  const [todas, setTodas] = useState(usuario?.accesoTodasCajas ?? false)
  const [activo, setActivo] = useState(usuario?.activo ?? true)
  const [ids, setIds] = useState<number[]>(usuario?.cajas.map((c) => c.id) ?? [])
  const [errores, setErrores] = useState<Record<string, string>>({})
  const esYo = usuario?.id === yo?.id
  const msg = (c?: string) => (c ? textoCodigo(tc, c) : undefined)

  const guardar = useMutation({
    mutationFn: () => usuario
      ? api.put<UsuarioAdmin>(`/usuarios/${usuario.id}`, { nombre: nombre.trim(), rol, activo, accesoTodasCajas: rol === 'ADMINISTRADOR' || todas, cajaIds: ids })
      : api.post<UsuarioAdmin>('/usuarios', { nombre: nombre.trim(), correo: correo.trim(), password, rol, accesoTodasCajas: rol === 'ADMINISTRADOR' || todas, cajaIds: ids }),
    onSuccess: () => { void cache.invalidateQueries({ queryKey: ['usuarios'] }); toast.ok(usuario ? t('actualizado') : t('creado')); onCerrar() },
    onError: (e) => { setErrores(codigosPorCampo(e)); toast.error(textoError(tc, e)) },
  })
  const alternar = (id: number) => setIds((l) => (l.includes(id) ? l.filter((x) => x !== id) : [...l, id]))

  return (
    <Modal abierto onCerrar={onCerrar} titulo={usuario ? t('editar') : t('crear')} ancho="md"
      pie={<><Button variante="secondary" onClick={onCerrar}>{tc('acciones.cancelar')}</Button><Button cargando={guardar.isPending} onClick={() => guardar.mutate()}>{tc('acciones.guardar')}</Button></>}>
      <div className="grid gap-4">
        <Input etiqueta={t('campos.nombre')} value={nombre} onChange={(e) => setNombre(e.target.value)} error={msg(errores.nombre)} />
        {!usuario && <Input etiqueta={t('campos.correo')} type="email" value={correo} onChange={(e) => setCorreo(e.target.value)} error={msg(errores.correo)} />}
        {!usuario && <Input etiqueta={t('campos.password')} ayuda={t('campos.passwordAyuda')} type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} error={msg(errores.password)} />}
        <Select etiqueta={t('campos.rol')} disabled={esYo} value={rol} onChange={(e) => setRol(e.target.value as Rol)} opciones={ROLES.map((r) => ({ valor: r, texto: `${tc(`roles.${r}`)} — ${t(`roles.${r}`)}` }))} error={msg(errores.rol)} />
        {rol !== 'ADMINISTRADOR' && (
          <>
            <label className="flex items-center gap-3 text-sm font-medium text-vino"><Toggle activo={todas} onCambio={setTodas} etiqueta={t('campos.todasCajas')} />{t('campos.todasCajas')}</label>
            {!todas && (
              <fieldset className="flex flex-col gap-2">
                <legend className="mb-1 text-xs font-semibold uppercase tracking-wide text-suave">{t('campos.cajas')}</legend>
                {cajas?.map((c) => (
                  <label key={c.id} className="flex items-center gap-2 rounded-control border border-borde px-3 py-2 text-sm text-vino">
                    <input type="checkbox" checked={ids.includes(c.id)} onChange={() => alternar(c.id)} className="h-4 w-4 accent-primary" />
                    {c.nombre}<span className="text-xs text-suave">· {c.empresa}</span>
                  </label>
                ))}
                {msg(errores.cajaIds) && <p role="alert" className="text-xs font-medium text-estado-baja">{msg(errores.cajaIds)}</p>}
              </fieldset>
            )}
          </>
        )}
        {usuario && <label className="flex items-center gap-3 text-sm font-medium text-vino"><Toggle activo={activo} deshabilitado={esYo} onCambio={setActivo} etiqueta={t('campos.activo')} />{t('campos.activo')}</label>}
      </div>
    </Modal>
  )
}

function ModalPassword({ usuario, onCerrar }: { usuario: UsuarioAdmin; onCerrar: () => void }) {
  const { t } = useTranslation('usuarios')
  const { t: tc } = useTranslation()
  const toast = useToast()
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | undefined>()
  const guardar = useMutation({
    mutationFn: () => api.post(`/usuarios/${usuario.id}/password`, { password }),
    onSuccess: () => { toast.ok(t('restablecida')); onCerrar() },
    onError: (e) => { setError(textoCodigo(tc, codigosPorCampo(e).password ?? 'VALIDACION')); toast.error(textoError(tc, e)) },
  })
  return (
    <Modal abierto onCerrar={onCerrar} titulo={t('restablecer')} descripcion={t('restablecerDetalle', { nombre: usuario.nombre })} ancho="sm"
      pie={<><Button variante="secondary" onClick={onCerrar}>{tc('acciones.cancelar')}</Button><Button cargando={guardar.isPending} onClick={() => guardar.mutate()}>{tc('acciones.guardar')}</Button></>}>
      <Input etiqueta={t('campos.passwordNueva')} ayuda={t('campos.passwordAyuda')} type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} error={error} />
    </Modal>
  )
}

export default function Pagina() {
  const { t } = useTranslation('usuarios')
  const { t: tc } = useTranslation()
  const { puede } = useSesion()
  const admin = puede('administrar')
  const q = useQuery({ queryKey: ['usuarios'], queryFn: () => api.get<UsuarioAdmin[]>('/usuarios'), enabled: admin })
  const [edicion, setEdicion] = useState<UsuarioAdmin | 'nuevo' | null>(null)
  const [reset, setReset] = useState<UsuarioAdmin | null>(null)

  const columnas = useMemo<Columnas<UsuarioAdmin>>(() => [
    {
      header: t('columnas.nombre'), accessorKey: 'nombre',
      cell: ({ row: { original: u } }) => (
        <div className="flex items-center gap-3">
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-tinte text-xs font-bold text-primary-texto">{iniciales(u.nombre)}</span>
          <span className="font-semibold text-vino">{u.nombre}</span>
        </div>
      ),
    },
    { header: t('columnas.correo'), accessorKey: 'correo' },
    { header: t('columnas.rol'), accessorKey: 'rol', cell: ({ row }) => <StatusPill tono="info">{tc(`roles.${row.original.rol}`)}</StatusPill> },
    { header: t('columnas.cajas'), id: 'cajas', cell: ({ row }) => (row.original.accesoTodasCajas ? t('todasCajas') : row.original.cajas.map((c) => c.nombre).join(', ') || '—') },
    { header: t('columnas.estado'), accessorKey: 'activo', cell: ({ row }) => <StatusPill tono={row.original.activo ? 'ok' : 'pausado'}>{row.original.activo ? t('activo') : t('inactivo')}</StatusPill> },
    { header: t('columnas.acceso'), accessorKey: 'ultimoAcceso', cell: ({ row }) => (row.original.ultimoAcceso ? fechaHora(row.original.ultimoAcceso) : t('sinAcceso')) },
    {
      header: t('columnas.acciones'), id: 'acciones', meta: { derecha: true },
      cell: ({ row }) => (
        <div className="flex justify-end gap-1">
          <button type="button" aria-label={tc('acciones.editar')} onClick={() => setEdicion(row.original)} className="rounded-full p-1.5 text-lg text-primary-texto hover:bg-tinte"><MdEdit aria-hidden /></button>
          <button type="button" aria-label={t('restablecer')} onClick={() => setReset(row.original)} className="rounded-full p-1.5 text-lg text-primary-texto hover:bg-tinte"><MdKey aria-hidden /></button>
        </div>
      ),
    },
  ], [t, tc])

  if (!admin) return <Card><ErrorState titulo={tc('estado.sinAccesoTitulo')} detalle={t('sinPermiso')} /></Card>

  return (
    <>
      <PageHeader titulo={t('titulo')} descripcion={t('descripcion')} acciones={<Button icono={<MdAdd />} onClick={() => setEdicion('nuevo')}>{t('nuevo')}</Button>} />
      <Card className="!p-0">
        <DataTable etiqueta={t('titulo')} columnas={columnas} datos={q.data} cargando={q.isLoading} error={q.isError ? q.error : undefined} onReintentar={() => void q.refetch()} />
      </Card>
      {edicion && <ModalUsuario key={edicion === 'nuevo' ? 'nuevo' : edicion.id} usuario={edicion === 'nuevo' ? null : edicion} onCerrar={() => setEdicion(null)} />}
      {reset && <ModalPassword usuario={reset} onCerrar={() => setReset(null)} />}
    </>
  )
}
