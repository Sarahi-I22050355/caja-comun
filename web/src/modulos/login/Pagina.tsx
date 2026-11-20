import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { MdSavings, MdVisibility, MdVisibilityOff } from 'react-icons/md'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { useSesion } from '../../auth/Sesion'
import { LangSwitch } from '../../layout/Shell'
import { textoCodigo, textoError } from '../../lib/errores'
import { Button } from '../../ui/base'
import { Input } from '../../ui/form'

// Los mensajes del esquema son códigos de error: se traducen al mostrarlos.
const esquema = z.object({
  correo: z.string().min(1, 'REQUERIDO').email('CORREO_INVALIDO'),
  password: z.string().min(1, 'REQUERIDO'),
})
type Datos = z.infer<typeof esquema>

export default function Pagina() {
  const { t } = useTranslation('login')
  const { t: tc } = useTranslation()
  const { usuario, iniciando, iniciar } = useSesion()
  const navegar = useNavigate()
  const ubicacion = useLocation()
  const [error, setError] = useState<string | null>(null)
  const [ver, setVer] = useState(false)
  const { register, handleSubmit, formState } = useForm<Datos>({ resolver: zodResolver(esquema) })

  if (!iniciando && usuario) return <Navigate to={(ubicacion.state as { desde?: string } | null)?.desde ?? '/'} replace />

  const enviar = handleSubmit(async (d) => {
    setError(null)
    try {
      await iniciar(d.correo.trim(), d.password)
      navegar((ubicacion.state as { desde?: string } | null)?.desde ?? '/', { replace: true })
    } catch (e) {
      setError(textoError(tc, e))
    }
  })

  const err = formState.errors
  return (
    <div className="grid min-h-screen lg:grid-cols-2">
      <section className="relative hidden flex-col justify-between bg-gradient-to-br from-vino to-primary p-12 text-white lg:flex">
        <div className="flex items-center gap-3">
          <span className="flex h-12 w-12 items-center justify-center rounded-control bg-white text-3xl text-primary-texto"><MdSavings aria-hidden /></span>
          <span className="text-2xl font-extrabold">{tc('app.nombre')}</span>
        </div>
        <div className="max-w-md">
          <h2 className="text-4xl font-extrabold leading-tight">{t('lema')}</h2>
          <p className="mt-4 text-lg text-white/85">{t('lema2')}</p>
        </div>
        <p className="text-sm text-white/70">{tc('app.subtitulo')}</p>
      </section>

      <main className="flex flex-col px-6 py-8 sm:px-12">
        <div className="flex justify-end"><LangSwitch /></div>
        <div className="mx-auto flex w-full max-w-md flex-1 flex-col justify-center">
          <div className="mb-6 flex items-center gap-3 lg:hidden">
            <span className="flex h-11 w-11 items-center justify-center rounded-control bg-primary-solido text-2xl text-white"><MdSavings aria-hidden /></span>
            <span className="text-xl font-extrabold text-vino">{tc('app.nombre')}</span>
          </div>
          <h1 className="text-3xl font-extrabold text-vino">{t('titulo')}</h1>
          <p className="mt-2 text-sm text-suave">{t('descripcion')}</p>

          <form onSubmit={enviar} noValidate className="mt-8 flex flex-col gap-5">
            <Input
              etiqueta={t('correo')}
              type="email"
              autoComplete="username"
              error={err.correo?.message ? textoCodigo(tc, err.correo.message) : undefined}
              {...register('correo')}
            />
            <div className="relative">
              <Input
                etiqueta={t('password')}
                type={ver ? 'text' : 'password'}
                autoComplete="current-password"
                error={err.password?.message ? textoCodigo(tc, err.password.message) : undefined}
                {...register('password')}
              />
              <button
                type="button"
                onClick={() => setVer((v) => !v)}
                aria-label={ver ? t('ocultar') : t('mostrar')}
                className="absolute right-3 top-[34px] text-xl text-suave hover:text-vino focus-visible:outline-none"
              >
                {ver ? <MdVisibilityOff aria-hidden /> : <MdVisibility aria-hidden />}
              </button>
            </div>
            {error && (
              <p role="alert" className="rounded-control bg-estado-baja-bg px-3 py-2 text-sm font-medium text-estado-baja">
                {error}
              </p>
            )}
            <Button type="submit" cargando={formState.isSubmitting} className="w-full">
              {t('entrar')}
            </Button>
          </form>
        </div>
      </main>
    </div>
  )
}
