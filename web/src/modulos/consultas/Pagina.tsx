import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { MdCheckCircle, MdContentCopy, MdDownload, MdPlayArrow, MdRule, MdStorage } from 'react-icons/md'
import { useCaja } from '../../auth/CajaActiva'
import { api, guardar } from '../../lib/api'
import { textoError } from '../../lib/errores'
import { dinero, fecha } from '../../lib/formato'
import { usePeriodosCaja } from '../../lib/consultas'
import type { CeldaConsulta, EquivalenciaConsulta, EstadoFederacion, ResultadoConsulta } from '../../lib/tipos'
import { Button, Card, ErrorState, PageHeader, Skeleton, StatusPill, cn } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'
import { Select } from '../../ui/form'
import { useToast } from '../../ui/Toast'

type Fila = CeldaConsulta[]

const COMANDO = 'scripts/federacion.ps1 iniciar'

// Anchos de las columnas de texto para que nombres largos no se partan en cuatro renglones.
const ANCHOS: Record<string, string> = { caja: '210px', empresa: '210px', ahorrador: '190px', motor: '110px', num_empleado: '110px' }

function celda(valor: CeldaConsulta, tipo: string, nombre: string, tc: TFunction): string {
  if (valor === null) return '—'
  if (tipo.startsWith('decimal')) return dinero(Number(valor))
  if (tipo === 'date') return fecha(String(valor))
  if (nombre === 'estado') return tc(`enums.estadoAhorrador.${valor}`, { defaultValue: String(valor) })
  return String(valor)
}

function Motores({ estado }: { estado: EstadoFederacion }) {
  const { t } = useTranslation('consultas')
  return (
    <section aria-label={t('motores')}>
      <h2 className="mb-2 text-xs font-bold uppercase tracking-wide text-suave">{t('motores')}</h2>
      <ul className="grid gap-3 sm:grid-cols-3">
        {estado.motores.map((m) => (
          <li key={m.motor} className="rounded-tarjeta border border-borde bg-white p-4 shadow-n1">
            <div className="flex items-center gap-2 font-bold text-vino"><MdStorage className="text-primary-texto" aria-hidden />{m.motor}</div>
            <p className="mt-1 text-sm text-vino">{m.cajas || '—'}</p>
            <p className="mt-1 text-xs text-suave">{t('motorDetalle', { ahorradores: m.ahorradores, movimientos: m.movimientos })}</p>
          </li>
        ))}
      </ul>
    </section>
  )
}

function BloqueSql({ titulo, sql }: { titulo: string; sql: string }) {
  const { t } = useTranslation('consultas')
  const toast = useToast()
  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(sql)
      toast.ok(t('copiado'))
    } catch {
      /* el portapapeles puede estar bloqueado: no pasa nada */
    }
  }
  return (
    <Card className="!p-0 overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-borde bg-tinte/60 px-4 py-2">
        <h2 className="text-sm font-bold text-vino">{titulo}</h2>
        <Button variante="ghost" pequeno icono={<MdContentCopy />} onClick={() => void copiar()}>{t('copiar')}</Button>
      </div>
      <pre tabIndex={0} className="max-h-72 overflow-auto bg-white p-4 text-xs leading-relaxed text-vino"><code>{sql}</code></pre>
    </Card>
  )
}

export default function Pagina() {
  const { t } = useTranslation('consultas')
  const { t: tc } = useTranslation()
  const toast = useToast()
  const { cajaId } = useCaja()
  const [q, setQ] = useState(1)
  const [anio, setAnio] = useState<number | null>(null)
  const [page, setPage] = useState(1)
  const [equivalencia, setEquivalencia] = useState<EquivalenciaConsulta | null>(null)

  const estado = useQuery({ queryKey: ['consultas', 'estado'], queryFn: () => api.get<EstadoFederacion>('/consultas-distribuidas'), retry: false })
  const periodos = usePeriodosCaja(cajaId)
  const usaAnio = estado.data?.consultas.find((c) => c.numero === q)?.usaAnio ?? false

  const anios = useMemo(() => {
    const s = new Set((periodos.data ?? []).map((p) => p.anio))
    if (anio !== null) s.add(anio)
    return [...s].sort((a, b) => b - a)
  }, [periodos.data, anio])

  const resultado = useQuery({
    queryKey: ['consultas', q, usaAnio ? anio : null, page],
    queryFn: () => api.get<ResultadoConsulta>(`/consultas-distribuidas/${q}`, { anio: usaAnio ? anio ?? undefined : undefined, page, pageSize: 15 }),
    enabled: estado.data?.disponible === true,
    placeholderData: keepPreviousData,
    retry: false,
  })

  // El servidor elige el año más reciente con ciclo cuando no se pide uno; se adopta para mostrarlo en el selector.
  useEffect(() => {
    if (usaAnio && anio === null && resultado.data?.anio) setAnio(resultado.data.anio)
  }, [usaAnio, anio, resultado.data?.anio])

  useEffect(() => { setEquivalencia(null) }, [q, anio])

  const comparar = useMutation({
    mutationFn: () => api.get<EquivalenciaConsulta>(`/consultas-distribuidas/${q}/equivalencia`, { anio: usaAnio ? anio ?? undefined : undefined }),
    onSuccess: setEquivalencia,
    onError: (e) => toast.error(textoError(tc, e)),
  })
  const csv = useMutation({
    mutationFn: () => api.archivo(`/consultas-distribuidas/${q}/csv`, { anio: usaAnio ? anio ?? undefined : undefined }),
    onSuccess: (a) => { guardar(a); toast.ok(t('csvListo')) },
    onError: (e) => toast.error(textoError(tc, e)),
  })

  const datos = resultado.data
  const columnas = useMemo<Columnas<Fila>>(
    () => (datos?.columnas ?? []).map((c, i) => ({
      id: c.nombre,
      header: t(`columnas.${c.nombre}`, { defaultValue: c.nombre }),
      accessorFn: (f: Fila) => f[i],
      cell: ({ row }) => celda(row.original[i], c.tipo, c.nombre, tc),
      meta: {
        derecha: c.tipo.startsWith('decimal') || c.tipo === 'bigint' || c.tipo === 'integer',
        ancho: ANCHOS[c.nombre],
      },
    })),
    [datos?.columnas, t, tc],
  )

  const cabecera = <PageHeader titulo={t('titulo')} descripcion={t('descripcion')} />

  if (estado.isLoading) return <>{cabecera}<Skeleton className="h-72 w-full" /></>
  if (estado.isError) return <>{cabecera}<Card><ErrorState detalle={textoError(tc, estado.error)} onReintentar={() => void estado.refetch()} /></Card></>
  if (!estado.data?.disponible) {
    const fallo = estado.data?.motivo === 'CONSULTA_FEDERADA_FALLO'
    return (
      <>
        {cabecera}
        <Card className="flex flex-col items-center gap-3 py-10 text-center" role="status">
          <MdStorage className="text-4xl text-borde" aria-hidden />
          <p className="font-semibold text-vino">{fallo ? t('fallaTitulo') : t('noDisponibleTitulo')}</p>
          <p className="max-w-lg text-sm text-suave">{fallo ? t('sinCarga') : t('noDisponibleDetalle')}</p>
          {!fallo && <code className="rounded-control bg-tinte px-3 py-2 text-sm text-vino">{COMANDO}</code>}
          <Button variante="secondary" onClick={() => void estado.refetch()}>{tc('acciones.reintentar')}</Button>
        </Card>
      </>
    )
  }

  return (
    <>
      {cabecera}
      <div className="grid gap-6 lg:grid-cols-[320px_minmax(0,1fr)]">
        <nav aria-label={t('catalogo')} className="flex flex-col gap-3">
          <h2 className="text-xs font-bold uppercase tracking-wide text-suave">{t('catalogo')}</h2>
          {estado.data.consultas.map((c) => {
            const activa = c.numero === q
            return (
              <button
                key={c.numero}
                type="button"
                aria-pressed={activa}
                onClick={() => { setQ(c.numero); setPage(1) }}
                className={cn(
                  'rounded-tarjeta border bg-white p-4 text-left shadow-n1 transition-colors focus-visible:outline-none focus-visible:shadow-foco',
                  activa ? 'border-primary bg-tinte/50' : 'border-borde hover:border-primary/50',
                )}
              >
                <span className="flex items-center justify-between">
                  <span className="rounded-full bg-tinte px-2 py-0.5 text-xs font-extrabold text-primary-texto">Q{c.numero}</span>
                  {activa && <MdCheckCircle className="text-primary-texto" aria-hidden />}
                </span>
                <span className="mt-2 block font-bold text-vino">{t(`consultas.${c.numero}.titulo`)}</span>
                <span className="mt-1 block text-xs text-suave">{t(`consultas.${c.numero}.detalle`)}</span>
              </button>
            )
          })}
        </nav>

        <div className="flex min-w-0 flex-col gap-6">
          <Motores estado={estado.data} />

          <Card className="flex flex-wrap items-end justify-between gap-4">
            <div>
              <p className="text-xs font-bold uppercase tracking-wide text-suave">{t('consulta', { n: q })}</p>
              <h2 className="text-lg font-bold text-vino">{t(`consultas.${q}.titulo`)}</h2>
              {q === 3 && <p className="text-xs text-suave">{t('edadAl')}</p>}
              {q === 5 && <p className="max-w-xl text-xs text-suave">{t('nota5')}</p>}
            </div>
            <div className="flex flex-wrap items-end gap-3">
              {usaAnio && (
                <Select
                  nombre={t('anio')}
                  value={anio ?? ''}
                  onChange={(e) => { setAnio(Number(e.target.value)); setPage(1) }}
                  opciones={anios.map((a) => ({ valor: a, texto: `${t('anio')} ${a}` }))}
                  className="!w-auto"
                />
              )}
              <Button variante="secondary" icono={<MdPlayArrow />} cargando={resultado.isFetching} onClick={() => void resultado.refetch()}>{t('ejecutar')}</Button>
              <Button variante="secondary" icono={<MdRule />} cargando={comparar.isPending} onClick={() => comparar.mutate()}>{t('comparar')}</Button>
              <Button icono={<MdDownload />} cargando={csv.isPending} onClick={() => csv.mutate()}>{t('csv')}</Button>
            </div>
          </Card>

          {equivalencia && (
            <Card className={cn('flex flex-col gap-3', equivalencia.coincide ? 'border-estado-ok' : 'border-estado-baja')} role="status">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 className="font-bold text-vino">{t('equivalencia.titulo')}</h2>
                <StatusPill tono={equivalencia.coincide ? 'ok' : 'baja'}>{equivalencia.coincide ? 'OK' : '≠'}</StatusPill>
              </div>
              <p className="text-sm text-vino">
                {equivalencia.coincide
                  ? t('equivalencia.ok', { filas: equivalencia.filasFederadas })
                  : t('equivalencia.fallo', { diferencias: equivalencia.diferencias, federadas: equivalencia.filasFederadas, directas: equivalencia.filasDirectas })}
              </p>
              {equivalencia.ejemplo && <p className="rounded-control bg-lienzo p-2 text-xs text-suave">{equivalencia.ejemplo}</p>}
              <p className="text-xs text-suave">{t('equivalencia.tiempos', { federada: equivalencia.duracionFederadaMs, directa: equivalencia.duracionDirectaMs })}</p>
              <details>
                <summary className="cursor-pointer text-sm font-semibold text-primary-texto">{t('equivalencia.sqlDirecto')}</summary>
                <pre tabIndex={0} className="mt-2 max-h-60 overflow-auto rounded-control bg-lienzo p-3 text-xs text-vino"><code>{equivalencia.sqlDirecto}</code></pre>
              </details>
            </Card>
          )}

          {datos ? (
            <>
              <BloqueSql titulo={t('sql')} sql={datos.sql} />
              <p className="-mt-3 text-xs text-suave">{t('sqlNota')}</p>
            </>
          ) : resultado.isLoading ? <Skeleton className="h-48 w-full" /> : null}

          <section aria-label={t('resultados')}>
            <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2">
              <h2 className="text-sm font-bold text-vino">{t('resultados')}</h2>
              {datos && (
                <p className="text-xs text-suave" aria-live="polite">
                  {t('registros', { count: datos.total })} · {t('tiempo', { ms: datos.duracionMs })}
                </p>
              )}
            </div>
            <Card className="!p-0">
              <DataTable
                etiqueta={t(`consultas.${q}.titulo`)}
                columnas={columnas}
                datos={datos?.filas}
                cargando={resultado.isLoading}
                error={resultado.isError ? resultado.error : undefined}
                onReintentar={() => void resultado.refetch()}
                pagina={datos && { page: datos.page, pageSize: datos.pageSize, total: datos.total }}
                onPagina={setPage}
              />
            </Card>
          </section>
        </div>
      </div>
    </>
  )
}
