import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MdDownload, MdRestartAlt } from 'react-icons/md'
import { useCaja } from '../../auth/CajaActiva'
import { useSesion } from '../../auth/Sesion'
import { api, guardar } from '../../lib/api'
import { textoCodigo, textoError } from '../../lib/errores'
import { dinero, entero_, fechaHora } from '../../lib/formato'
import type { AplicarImportacionResultado, Importacion, ImportacionDetalle, Pagina, VistaPreviaImportacion } from '../../lib/tipos'
import { Button, Card, KpiCard, PageHeader, Spinner, StatusPill, tonoDe } from '../../ui/base'
import { DataTable } from '../../ui/DataTable'
import type { Columnas } from '../../ui/DataTable'
import { FileDropzone, Select, Toggle } from '../../ui/form'
import { Modal } from '../../ui/Modal'
import { Stepper } from '../../ui/Stepper'
import { useToast } from '../../ui/Toast'

const COLUMNAS_ARCHIVO = ['movimiento', 'num_empleado', 'nombre', 'apellidos', 'rfc', 'fecha_nacimiento', 'fecha_ingreso', 'sueldo_quincenal', 'clabe', 'departamento', 'cuota_ordinaria', 'cuota_voluntaria', 'voluntario_activo', 'fecha_baja']

function descargarPlantilla(prefijo: string) {
  const ejemplo = ['ALTA', `${prefijo}-09901`, 'Julia', 'Pérez', 'PEMJ900517AB1', '1990-05-17', '2024-10-01', '9000.00', '012180001234567899', 'Almacén', '450.00', '', '', '']
  const baja = ['BAJA', `${prefijo}-00130`, '', '', '', '', '', '', '', '', '', '', '', '2024-11-05']
  const texto = [COLUMNAS_ARCHIVO, ejemplo, baja].map((f) => f.join(',')).join('\r\n') + '\r\n'
  guardar({ blob: new Blob(['﻿' + texto], { type: 'text/csv;charset=utf-8' }), nombre: 'plantilla-altas-bajas.csv', cabeceras: new Headers() })
}

export default function Pagina() {
  const { t } = useTranslation('sincronizacion')
  const { t: tc } = useTranslation()
  const { puede } = useSesion()
  const { cajaId, caja } = useCaja()
  const cache = useQueryClient()
  const toast = useToast()
  const [lote, setLote] = useState<Importacion | null>(null)
  const [resultado, setResultado] = useState<AplicarImportacionResultado | null>(null)
  const [soloErrores, setSoloErrores] = useState(false)
  const [omitir, setOmitir] = useState(false)
  const [page, setPage] = useState(1)
  const [confirmar, setConfirmar] = useState(false)
  const [pageHist, setPageHist] = useState(1)

  const paso = resultado ? 2 : lote ? 1 : 0

  const subir = useMutation({
    mutationFn: (f: File) => api.subir<VistaPreviaImportacion>('/importaciones', { cajaId: String(cajaId) }, f),
    onSuccess: (v) => {
      setLote(v.importacion)
      setPage(1)
      setSoloErrores(false)
      setOmitir(false)
      void cache.invalidateQueries({ queryKey: ['importaciones'] })
    },
    onError: (e) => toast.error(textoError(tc, e)),
  })

  const detalle = useQuery({
    queryKey: ['importacion', lote?.id, soloErrores, page],
    queryFn: () => api.get<VistaPreviaImportacion>(`/importaciones/${lote!.id}`, { validacion: soloErrores ? 'ERROR' : undefined, page, pageSize: 100 }),
    enabled: !!lote,
    placeholderData: keepPreviousData,
  })
  const resumen = detalle.data?.importacion.resumen ?? lote?.resumen

  const aplicar = useMutation({
    mutationFn: () => api.post<AplicarImportacionResultado>(`/importaciones/${lote!.id}/aplicar`, undefined, { omitirErrores: omitir }),
    onSuccess: (r) => {
      setResultado(r)
      setConfirmar(false)
      toast.ok(t('aplicado'))
      for (const k of ['ahorradores', 'dashboard', 'importaciones', 'cajas']) void cache.invalidateQueries({ queryKey: [k] })
    },
    onError: (e) => { setConfirmar(false); toast.error(textoError(tc, e)) },
  })
  const cancelar = useMutation({
    mutationFn: () => api.post(`/importaciones/${lote!.id}/cancelar`),
    onSuccess: () => { toast.ok(t('cancelado')); reiniciar(); void cache.invalidateQueries({ queryKey: ['importaciones'] }) },
    onError: (e) => toast.error(textoError(tc, e)),
  })
  const reiniciar = () => { setLote(null); setResultado(null); setPage(1) }

  const historial = useQuery({
    queryKey: ['importaciones', cajaId, pageHist],
    queryFn: () => api.get<Pagina<Importacion>>('/importaciones', { cajaId, page: pageHist, pageSize: 8 }),
    enabled: cajaId !== undefined,
    placeholderData: keepPreviousData,
  })

  const columnas = useMemo<Columnas<ImportacionDetalle>>(
    () => [
      { header: t('columnas.fila'), accessorKey: 'numeroFila', meta: { ancho: '70px' } },
      { header: t('columnas.tipo'), accessorKey: 'tipo', cell: ({ row }) => <StatusPill tono={tonoDe(row.original.tipo)}>{tc(`enums.tipoCambio.${row.original.tipo}`)}</StatusPill>, meta: { ancho: '120px' } },
      { header: t('columnas.empleado'), accessorKey: 'numEmpleado' },
      { header: t('columnas.nombre'), id: 'nombre', cell: ({ row }) => [row.original.datos.nombre, row.original.datos.apellidos].filter(Boolean).join(' ') || (row.original.datos.fecha_baja ? t('baja', { fecha: row.original.datos.fecha_baja }) : '—') },
      { header: t('columnas.validacion'), accessorKey: 'validacion', cell: ({ row }) => <StatusPill tono={row.original.validacion === 'VALIDO' ? 'ok' : 'baja'}>{row.original.validacion === 'VALIDO' ? t('valido') : t('conError')}</StatusPill>, meta: { ancho: '120px' } },
      {
        header: t('columnas.errores'),
        id: 'errores',
        cell: ({ row }) => (
          <ul className="flex flex-wrap gap-1.5">
            {row.original.errores.map((e, i) => (
              <li key={i} className="rounded-full bg-estado-baja-bg px-2 py-0.5 text-xs font-medium text-estado-baja"><span className="font-mono">{e.campo}</span>: {textoCodigo(tc, e.codigo)}</li>
            ))}
          </ul>
        ),
      },
    ],
    [t, tc],
  )

  const columnasHist = useMemo<Columnas<Importacion>>(
    () => [
      { header: t('hist.archivo'), accessorKey: 'archivo', cell: ({ row }) => <span className="font-semibold text-vino">{row.original.archivo}</span> },
      { header: t('hist.fecha'), accessorKey: 'fecha', cell: ({ row }) => fechaHora(row.original.fecha) },
      { header: t('hist.usuario'), accessorKey: 'usuario' },
      { header: t('hist.estado'), accessorKey: 'estado', cell: ({ row }) => <StatusPill tono={row.original.estado === 'APLICADA' ? 'ok' : row.original.estado === 'PREVIA' ? 'pendiente' : 'pausado'}>{tc(`enums.estadoImportacion.${row.original.estado}`)}</StatusPill> },
      { header: t('hist.resumen'), id: 'resumen', cell: ({ row }) => t('histResumen', { altas: row.original.resumen.altas, bajas: row.original.resumen.bajas, errores: row.original.resumen.errores }) },
    ],
    [t, tc],
  )

  const sinErrores = (resumen?.errores ?? 0) === 0
  const puedeAplicar = puede('capturar') && !!resumen && (sinErrores || omitir) && resumen.altas + resumen.bajas > 0
  const r = resumen

  return (
    <>
      <PageHeader titulo={t('titulo')} descripcion={t('descripcion')} acciones={<Button variante="secondary" icono={<MdDownload />} onClick={() => descargarPlantilla(caja?.prefijo ?? 'GIM')}>{t('plantilla')}</Button>} />
      <Card className="mb-6">
        <Stepper pasos={[{ titulo: t('pasos.cargar'), detalle: t('pasos.cargarDetalle') }, { titulo: t('pasos.revisar'), detalle: t('pasos.revisarDetalle') }, { titulo: t('pasos.aplicar'), detalle: t('pasos.aplicarDetalle') }]} actual={paso} />
      </Card>

      {paso === 0 && (
        <Card>
          {!puede('capturar') && <p className="mb-3 text-sm font-medium text-estado-pendiente">{t('sinPermiso')}</p>}
          {subir.isPending ? (
            <div className="flex items-center justify-center gap-3 py-12 text-vino" role="status"><Spinner className="h-6 w-6" /> {t('subiendo')}</div>
          ) : (
            <FileDropzone extensiones={['.xlsx', '.xml', '.txt', '.csv']} maxBytes={2 * 1024 * 1024} onArchivo={(f) => subir.mutate(f)} deshabilitado={!puede('capturar') || cajaId === undefined} texto={t('dropzone.texto')} ayuda={t('dropzone.ayuda')} />
          )}
          <p className="mt-3 text-xs text-suave">{t('formato')}</p>
        </Card>
      )}

      {paso >= 1 && lote && r && (
        <>
          <p className="mb-3 text-sm text-suave"><span className="font-semibold text-vino">{lote.archivo}</span> · {lote.caja} · {fechaHora(lote.fecha)}</p>
          <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
            <KpiCard titulo={t('resumen.filas')} valor={entero_(r.filas)} />
            <KpiCard titulo={t('resumen.altas')} valor={entero_(r.altas)} />
            <KpiCard titulo={t('resumen.bajas')} valor={entero_(r.bajas)} />
            <KpiCard titulo={t('resumen.sinCambio')} valor={entero_(r.sinCambio)} />
            <KpiCard titulo={t('resumen.errores')} valor={<span className={r.errores > 0 ? 'text-estado-baja' : undefined}>{entero_(r.errores)}</span>} />
          </section>

          {paso === 1 && (
            <Card className="mt-6 !p-0">
              <div className="flex flex-wrap items-center justify-between gap-3 p-4">
                <Select nombre={t('filtro.todos')} value={soloErrores ? 'e' : 't'} onChange={(e) => { setSoloErrores(e.target.value === 'e'); setPage(1) }} className="!w-auto">
                  <option value="t">{t('filtro.todos')}</option>
                  <option value="e">{t('filtro.errores')}</option>
                </Select>
                {!sinErrores && (
                  <label className="flex items-center gap-3 text-sm font-medium text-vino" title={t('omitirAyuda')}>
                    <Toggle activo={omitir} onCambio={setOmitir} etiqueta={t('omitir')} />
                    {t('omitir')}
                  </label>
                )}
              </div>
              <DataTable
                etiqueta={t('titulo')}
                columnas={columnas}
                datos={detalle.data?.detalle.items}
                cargando={detalle.isLoading}
                error={detalle.isError ? detalle.error : undefined}
                onReintentar={() => void detalle.refetch()}
                pagina={detalle.data && { page: detalle.data.detalle.page, pageSize: detalle.data.detalle.pageSize, total: detalle.data.detalle.total }}
                onPagina={setPage}
                virtual
                alto={460}
                claseFila={(f) => (f.validacion === 'ERROR' ? 'bg-estado-baja-bg/40' : undefined)}
              />
              <div className="flex flex-wrap justify-end gap-2 border-t border-borde p-4">
                <Button variante="secondary" cargando={cancelar.isPending} onClick={() => cancelar.mutate()}>{t('cancelar')}</Button>
                <Button disabled={!puedeAplicar} onClick={() => setConfirmar(true)}>{t('aplicar')}</Button>
              </div>
            </Card>
          )}

          {paso === 2 && resultado && (
            <Card className="mt-6">
              <h2 className="text-xl font-bold text-estado-ok">{t('aplicado')}</h2>
              <dl className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
                {[
                  [t('resultado.altas'), entero_(resultado.altasAplicadas)], [t('resultado.bajas'), entero_(resultado.bajasAplicadas)],
                  [t('resultado.sinCambio'), entero_(resultado.sinCambio)], [t('resultado.omitidas'), entero_(resultado.omitidas)],
                  [t('resultado.liquidado'), dinero(resultado.ahorroLiquidado)],
                ].map(([k, v]) => <div key={k} className="rounded-control bg-lienzo p-3"><dt className="text-xs text-suave">{k}</dt><dd className="text-xl font-extrabold text-vino">{v}</dd></div>)}
              </dl>
              <Button className="mt-5" icono={<MdRestartAlt />} onClick={reiniciar}>{t('otro')}</Button>
            </Card>
          )}
        </>
      )}

      <Card className="mt-6 !p-0">
        <h2 className="p-5 pb-2 text-xl font-bold text-vino">{t('historial')}</h2>
        <DataTable
          etiqueta={t('historial')}
          columnas={columnasHist}
          datos={historial.data?.items}
          cargando={historial.isLoading}
          error={historial.isError ? historial.error : undefined}
          onReintentar={() => void historial.refetch()}
          pagina={historial.data && { page: historial.data.page, pageSize: historial.data.pageSize, total: historial.data.total }}
          onPagina={setPageHist}
        />
      </Card>

      <Modal abierto={confirmar} onCerrar={() => setConfirmar(false)} titulo={t('confirmarTitulo')} descripcion={t('confirmarTexto')} ancho="sm"
        pie={<><Button variante="secondary" onClick={() => setConfirmar(false)}>{tc('acciones.cancelar')}</Button><Button cargando={aplicar.isPending} onClick={() => aplicar.mutate()}>{tc('acciones.confirmar')}</Button></>}>
        {r && <p className="text-sm text-vino">{t('resumen.altas')}: <b>{r.altas}</b> · {t('resumen.bajas')}: <b>{r.bajas}</b> · {t('resumen.errores')}: <b>{r.errores}</b></p>}
      </Modal>
    </>
  )
}
