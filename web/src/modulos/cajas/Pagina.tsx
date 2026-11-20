import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { MdAdd, MdApartment, MdEdit } from 'react-icons/md'
import { z } from 'zod'
import { useCaja } from '../../auth/CajaActiva'
import { useSesion } from '../../auth/Sesion'
import { api } from '../../lib/api'
import { codigosPorCampo, erroresPorCampo, textoCodigo, textoError } from '../../lib/errores'
import { dinero, entero_ } from '../../lib/formato'
import type { Caja, Empresa } from '../../lib/tipos'
import { Button, Card, EmptyState, ErrorState, PageHeader, Skeleton, StatusPill } from '../../ui/base'
import { Input, Select, Toggle } from '../../ui/form'
import { Modal } from '../../ui/Modal'
import { useToast } from '../../ui/Toast'

const esquemaNueva = z
  .object({
    modo: z.enum(['existente', 'nueva']),
    empresaId: z.string().optional(),
    empresaNueva: z.string().max(150, 'LONGITUD_MAXIMA').optional(),
    nombre: z.string().min(1, 'REQUERIDO').max(150, 'LONGITUD_MAXIMA'),
    prefijo: z.string().regex(/^[A-Z]{2,5}$/, 'PREFIJO_INVALIDO'),
    tasa: z.coerce.number('MONTO_INVALIDO').min(1, 'TASA_FUERA_DE_RANGO').max(2, 'TASA_FUERA_DE_RANGO'),
    frecuencia: z.enum(['QUINCENAL', 'SEMANAL']),
    anio: z.coerce.number('ANIO_INVALIDO').int('ANIO_INVALIDO').min(2000, 'ANIO_INVALIDO').max(2100, 'ANIO_INVALIDO'),
  })
  .refine((d) => (d.modo === 'existente' ? !!d.empresaId : !!d.empresaNueva?.trim()), { path: ['empresaNueva'], message: 'EMPRESA_REQUERIDA' })
type DatosNueva = z.infer<typeof esquemaNueva>

function ModalNueva({ abierto, onCerrar }: { abierto: boolean; onCerrar: () => void }) {
  const { t } = useTranslation('cajas')
  const { t: tc } = useTranslation()
  const cache = useQueryClient()
  const toast = useToast()
  const empresas = useQuery({ queryKey: ['empresas'], queryFn: () => api.get<Empresa[]>('/empresas'), enabled: abierto })
  const { register, handleSubmit, watch, setError, reset, formState } = useForm<DatosNueva>({
    resolver: zodResolver(esquemaNueva) as never,
    defaultValues: { modo: 'existente', frecuencia: 'QUINCENAL', anio: new Date().getFullYear(), tasa: 1.5 },
  })
  const modo = watch('modo')
  const err = formState.errors
  const msg = (e?: { message?: string }) => (e?.message ? textoCodigo(tc, e.message) : undefined)

  const crear = useMutation({
    mutationFn: (d: DatosNueva) =>
      api.post<Caja>('/cajas', {
        empresaId: d.modo === 'existente' ? Number(d.empresaId) : null,
        empresaNueva: d.modo === 'nueva' ? d.empresaNueva?.trim() : null,
        nombre: d.nombre.trim(),
        prefijo: d.prefijo,
        tasaInteresMensual: d.tasa,
        frecuenciaNomina: d.frecuencia,
        anioCiclo: d.anio,
      }),
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: ['cajas'] })
      void cache.invalidateQueries({ queryKey: ['empresas'] })
      toast.ok(t('creada'))
      reset()
      onCerrar()
    },
    onError: (e) => {
      for (const [campo, codigo] of Object.entries(codigosPorCampo(e, { tasaInteresMensual: 'tasa', frecuenciaNomina: 'frecuencia', anioCiclo: 'anio' })))
        setError(campo as keyof DatosNueva, { message: codigo })
      toast.error(textoError(tc, e))
    },
  })

  return (
    <Modal
      abierto={abierto}
      onCerrar={onCerrar}
      titulo={t('nueva')}
      descripcion={t('nota')}
      pie={
        <>
          <Button variante="secondary" onClick={onCerrar}>{tc('acciones.cancelar')}</Button>
          <Button cargando={crear.isPending} onClick={handleSubmit((d) => crear.mutate(d))}>{tc('acciones.crear')}</Button>
        </>
      }
    >
      <form className="grid gap-4 sm:grid-cols-2" onSubmit={(e) => e.preventDefault()} noValidate>
        <div className="sm:col-span-2">
          <Select etiqueta={t('campos.empresa')} {...register('modo')} opciones={[{ valor: 'existente', texto: t('campos.empresaExistente') }, { valor: 'nueva', texto: t('campos.empresaNueva') }]} />
        </div>
        {modo === 'existente' ? (
          <div className="sm:col-span-2">
            <Select etiqueta={t('campos.empresaExistente')} {...register('empresaId')} error={msg(err.empresaNueva)}>
              <option value="" />
              {empresas.data?.map((e) => <option key={e.id} value={e.id}>{e.nombre}</option>)}
            </Select>
          </div>
        ) : (
          <div className="sm:col-span-2"><Input etiqueta={t('campos.nombreEmpresa')} {...register('empresaNueva')} error={msg(err.empresaNueva)} /></div>
        )}
        <Input etiqueta={t('campos.nombre')} {...register('nombre')} error={msg(err.nombre)} />
        <Input etiqueta={t('campos.prefijo')} ayuda={t('campos.prefijoAyuda')} maxLength={5} {...register('prefijo', { setValueAs: (v: string) => v.toUpperCase() })} error={msg(err.prefijo)} />
        <Input etiqueta={t('campos.tasa')} ayuda={t('campos.tasaAyuda')} type="number" step="0.01" inputMode="decimal" {...register('tasa')} error={msg(err.tasa)} />
        <Select etiqueta={t('campos.frecuencia')} {...register('frecuencia')} opciones={[{ valor: 'QUINCENAL', texto: tc('enums.frecuenciaNomina.QUINCENAL') }, { valor: 'SEMANAL', texto: tc('enums.frecuenciaNomina.SEMANAL') }]} />
        <Input etiqueta={t('campos.anio')} type="number" {...register('anio')} error={msg(err.anio)} />
      </form>
    </Modal>
  )
}

function ModalEditar({ caja, onCerrar }: { caja: Caja | null; onCerrar: () => void }) {
  const { t } = useTranslation('cajas')
  const { t: tc } = useTranslation()
  const cache = useQueryClient()
  const toast = useToast()
  const [nombre, setNombre] = useState(caja?.nombre ?? '')
  const [tasa, setTasa] = useState(String(caja?.tasaInteresMensual ?? 1.5))
  const [activa, setActiva] = useState(caja?.activa ?? true)
  const [errores, setErrores] = useState<Record<string, string>>({})

  const guardar = useMutation({
    mutationFn: () => api.put<Caja>(`/cajas/${caja!.id}`, { nombre: nombre.trim(), tasaInteresMensual: Number(tasa), activa }),
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: ['cajas'] })
      toast.ok(t('actualizada'))
      onCerrar()
    },
    onError: (e) => { setErrores(erroresPorCampo(tc, e)); toast.error(textoError(tc, e)) },
  })

  return (
    <Modal
      abierto={!!caja}
      onCerrar={onCerrar}
      titulo={t('editar')}
      pie={<><Button variante="secondary" onClick={onCerrar}>{tc('acciones.cancelar')}</Button><Button cargando={guardar.isPending} onClick={() => guardar.mutate()}>{tc('acciones.guardar')}</Button></>}
    >
      <div className="grid gap-4">
        <Input etiqueta={t('campos.nombre')} value={nombre} onChange={(e) => setNombre(e.target.value)} error={errores.nombre} />
        <Input etiqueta={t('campos.tasa')} ayuda={t('campos.tasaAyuda')} type="number" step="0.01" value={tasa} onChange={(e) => setTasa(e.target.value)} error={errores.tasaInteresMensual} />
        <div className="flex items-center gap-3"><Toggle activo={activa} onCambio={setActiva} etiqueta={t('campos.activa')} /><span className="text-sm font-medium text-vino">{t('campos.activa')}</span></div>
      </div>
    </Modal>
  )
}

export default function Pagina() {
  const { t } = useTranslation('cajas')
  const { t: tc } = useTranslation()
  const { puede } = useSesion()
  const { cajas, cargando } = useCaja()
  const q = useQuery({ queryKey: ['cajas'], queryFn: () => api.get<Caja[]>('/cajas') })
  const [nueva, setNueva] = useState(false)
  const [editando, setEditando] = useState<Caja | null>(null)
  const lista = cajas ?? q.data

  return (
    <>
      <PageHeader
        titulo={t('titulo')}
        descripcion={t('descripcion')}
        acciones={puede('administrar') && <Button icono={<MdAdd />} onClick={() => setNueva(true)}>{t('nueva')}</Button>}
      />
      {q.isError && !lista ? (
        <Card><ErrorState detalle={textoError(tc, q.error)} onReintentar={() => void q.refetch()} /></Card>
      ) : cargando && !lista ? (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-52" />)}</div>
      ) : lista && lista.length === 0 ? (
        <Card><EmptyState /></Card>
      ) : (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {lista?.map((c) => (
            <Card key={c.id} className="flex flex-col gap-4">
              <div className="flex items-start justify-between gap-3">
                <div className="flex min-w-0 items-center gap-3">
                  <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-control bg-tinte text-2xl text-primary-texto"><MdApartment aria-hidden /></span>
                  <div className="min-w-0">
                    <h2 className="truncate text-lg font-bold text-vino">{c.nombre}</h2>
                    <p className="truncate text-xs text-suave">{c.empresa}</p>
                  </div>
                </div>
                {!c.activa && <StatusPill tono="pausado">{t('tarjeta.inactiva')}</StatusPill>}
              </div>
              <dl className="grid grid-cols-2 gap-3 text-sm">
                <div><dt className="text-xs text-suave">{t('tarjeta.ahorradores')}</dt><dd className="text-xl font-extrabold text-vino">{entero_(c.ahorradoresActivos)}</dd></div>
                <div><dt className="text-xs text-suave">{t('tarjeta.ahorro')}</dt><dd className="text-xl font-extrabold text-vino">{dinero(c.ahorroTotal)}</dd></div>
                <div><dt className="text-xs text-suave">{t('tarjeta.tasa')}</dt><dd className="font-semibold text-vino">{c.tasaInteresMensual.toFixed(2)} %</dd></div>
                <div><dt className="text-xs text-suave">{t('tarjeta.frecuencia')}</dt><dd className="font-semibold text-vino">{tc(`enums.frecuenciaNomina.${c.frecuenciaNomina}`)}</dd></div>
              </dl>
              <div className="flex items-center justify-between gap-2 border-t border-borde pt-3">
                <span className="rounded-full bg-tinte px-2.5 py-0.5 text-xs font-bold text-vino">{c.prefijo}-00000</span>
                {puede('administrar') && <Button variante="ghost" pequeno icono={<MdEdit />} onClick={() => setEditando(c)}>{tc('acciones.editar')}</Button>}
              </div>
            </Card>
          ))}
        </div>
      )}
      <ModalNueva abierto={nueva} onCerrar={() => setNueva(false)} />
      {editando && <ModalEditar key={editando.id} caja={editando} onCerrar={() => setEditando(null)} />}
    </>
  )
}
