import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { api } from '../../lib/api'
import { codigosPorCampo, textoCodigo, textoError } from '../../lib/errores'
import type { AhorradorDetalle } from '../../lib/tipos'
import { Button } from '../../ui/base'
import { Input, Toggle } from '../../ui/form'
import { Modal } from '../../ui/Modal'
import { useToast } from '../../ui/Toast'

const dinero2 = (c: string) => z.coerce.number(c).min(0, 'MONTO_NEGATIVO')

const esquema = z.object({
  numEmpleado: z.string().regex(/^([A-Z]{2,5}-[0-9]{5})?$/, 'NUM_EMPLEADO_INVALIDO'),
  nombre: z.string().trim().min(1, 'REQUERIDO').max(100, 'LONGITUD_MAXIMA'),
  apellidos: z.string().trim().min(1, 'REQUERIDO').max(100, 'LONGITUD_MAXIMA'),
  rfc: z.string().regex(/^[A-ZÑ&]{4}[0-9]{6}[A-Z0-9]{3}$/, 'RFC_INVALIDO'),
  fechaNacimiento: z.string().min(1, 'REQUERIDO'),
  fechaIngreso: z.string().min(1, 'REQUERIDO'),
  sueldoQuincenal: dinero2('MONTO_INVALIDO'),
  clabe: z.string().regex(/^[0-9]{18}$/, 'CLABE_INVALIDA'),
  departamento: z.string().trim().min(1, 'REQUERIDO').max(100, 'LONGITUD_MAXIMA'),
  cuotaOrdinaria: dinero2('MONTO_INVALIDO'),
  cuotaVoluntaria: dinero2('MONTO_INVALIDO'),
  voluntarioActivo: z.boolean(),
})
type Datos = z.infer<typeof esquema>

/** Alta (sin `ahorrador`) o edición de datos personales y de nómina. La cuota se cambia aparte desde el perfil. */
export function FormularioAhorrador({ abierto, onCerrar, cajaId, ahorrador }: { abierto: boolean; onCerrar: () => void; cajaId?: number; ahorrador?: AhorradorDetalle }) {
  const { t } = useTranslation('ahorradores')
  const { t: tc } = useTranslation()
  const cache = useQueryClient()
  const toast = useToast()
  const edicion = !!ahorrador
  const { register, handleSubmit, setValue, watch, setError, reset, formState } = useForm<Datos>({
    resolver: zodResolver(esquema) as never,
    defaultValues: { voluntarioActivo: false, cuotaVoluntaria: 0, cuotaOrdinaria: 0, numEmpleado: '' },
  })

  useEffect(() => {
    if (!abierto) return
    reset(
      ahorrador
        ? {
            numEmpleado: ahorrador.numEmpleado, nombre: ahorrador.nombre, apellidos: ahorrador.apellidos, rfc: ahorrador.rfc,
            fechaNacimiento: ahorrador.fechaNacimiento, fechaIngreso: ahorrador.fechaIngreso, sueldoQuincenal: ahorrador.sueldoQuincenal,
            clabe: ahorrador.clabe, departamento: ahorrador.departamento, cuotaOrdinaria: 0, cuotaVoluntaria: 0, voluntarioActivo: false,
          }
        : { voluntarioActivo: false, cuotaVoluntaria: 0, cuotaOrdinaria: 0, numEmpleado: '' },
    )
  }, [abierto, ahorrador, reset])

  const activo = watch('voluntarioActivo')
  const err = formState.errors
  const msg = (e?: { message?: string }) => (e?.message ? textoCodigo(tc, e.message) : undefined)

  const guardar = useMutation({
    mutationFn: (d: Datos) => {
      const comun = {
        nombre: d.nombre.trim(), apellidos: d.apellidos.trim(), rfc: d.rfc, fechaNacimiento: d.fechaNacimiento,
        sueldoQuincenal: d.sueldoQuincenal, clabe: d.clabe, departamento: d.departamento.trim(),
      }
      return edicion
        ? api.put<AhorradorDetalle>(`/ahorradores/${ahorrador!.id}`, comun)
        : api.post<AhorradorDetalle>('/ahorradores', {
            ...comun, cajaId, numEmpleado: d.numEmpleado || null, fechaIngreso: d.fechaIngreso, cuotaOrdinaria: d.cuotaOrdinaria,
            cuotaVoluntaria: d.voluntarioActivo ? d.cuotaVoluntaria : 0, voluntarioActivo: d.voluntarioActivo,
          })
    },
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: ['ahorradores'] })
      void cache.invalidateQueries({ queryKey: ['ahorrador'] })
      void cache.invalidateQueries({ queryKey: ['dashboard'] })
      toast.ok(edicion ? t('actualizado') : t('creado'))
      onCerrar()
    },
    onError: (e) => {
      for (const [campo, codigo] of Object.entries(codigosPorCampo(e))) setError(campo as keyof Datos, { message: codigo })
      toast.error(textoError(tc, e))
    },
  })

  return (
    <Modal
      abierto={abierto}
      onCerrar={onCerrar}
      titulo={edicion ? t('perfil.editar') : t('alta')}
      ancho="lg"
      pie={<><Button variante="secondary" onClick={onCerrar}>{tc('acciones.cancelar')}</Button><Button cargando={guardar.isPending} onClick={handleSubmit((d) => guardar.mutate(d))}>{tc('acciones.guardar')}</Button></>}
    >
      <form className="grid gap-4 sm:grid-cols-2" onSubmit={(e) => e.preventDefault()} noValidate>
        {!edicion && <Input etiqueta={t('campos.numEmpleado')} ayuda={t('campos.numEmpleadoAyuda')} placeholder="GIM-00124" {...register('numEmpleado', { setValueAs: (v: string) => v.trim().toUpperCase() })} error={msg(err.numEmpleado)} />}
        <Input etiqueta={t('campos.nombre')} {...register('nombre')} error={msg(err.nombre)} />
        <Input etiqueta={t('campos.apellidos')} {...register('apellidos')} error={msg(err.apellidos)} />
        <Input etiqueta={t('campos.rfc')} maxLength={13} {...register('rfc', { setValueAs: (v: string) => v.trim().toUpperCase() })} error={msg(err.rfc)} />
        <Input etiqueta={t('campos.fechaNacimiento')} type="date" {...register('fechaNacimiento')} error={msg(err.fechaNacimiento)} />
        {!edicion && <Input etiqueta={t('campos.fechaIngreso')} type="date" {...register('fechaIngreso')} error={msg(err.fechaIngreso)} />}
        <Input etiqueta={t('campos.sueldoQuincenal')} type="number" step="0.01" inputMode="decimal" {...register('sueldoQuincenal')} error={msg(err.sueldoQuincenal)} />
        <Input etiqueta={t('campos.clabe')} inputMode="numeric" maxLength={18} {...register('clabe')} error={msg(err.clabe)} />
        <Input etiqueta={t('campos.departamento')} {...register('departamento')} error={msg(err.departamento)} />
        {!edicion && (
          <>
            <Input etiqueta={t('campos.cuotaOrdinaria')} type="number" step="0.01" inputMode="decimal" {...register('cuotaOrdinaria')} error={msg(err.cuotaOrdinaria)} />
            <div className="flex items-center gap-3 sm:col-span-2">
              <Toggle activo={activo} onCambio={(v) => setValue('voluntarioActivo', v)} etiqueta={t('campos.voluntarioActivo')} />
              <span className="text-sm font-medium text-vino">{t('campos.voluntarioActivo')}</span>
            </div>
            {activo && <Input etiqueta={t('campos.cuotaVoluntaria')} type="number" step="0.01" inputMode="decimal" {...register('cuotaVoluntaria')} error={msg(err.cuotaVoluntaria)} />}
          </>
        )}
      </form>
    </Modal>
  )
}
