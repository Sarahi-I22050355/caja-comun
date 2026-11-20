// Métricas web de usuarios reales (monitoreo sencillo): LCP, CLS, INP, FCP y TTFB se mandan a la propia API, sin ningún
// servicio de terceros. No se envía nada de la persona: solo el nombre de la métrica, su valor y la ruta de la pantalla.

import type { Metric } from 'web-vitals'

const DESTINO = '/api/v1/telemetria/web-vitals'

function enviar(m: Metric) {
  const cuerpo = JSON.stringify({ nombre: m.name, valor: m.value, ruta: location.pathname, calificacion: m.rating })
  try {
    // sendBeacon sobrevive al cierre de la pestaña, que es cuando el navegador entrega CLS e INP.
    if (navigator.sendBeacon?.(DESTINO, new Blob([cuerpo], { type: 'application/json' }))) return
    void fetch(DESTINO, { method: 'POST', body: cuerpo, headers: { 'Content-Type': 'application/json' }, keepalive: true })
  } catch {
    /* la telemetría nunca debe romper la aplicación */
  }
}

/** Se carga cuando el navegador está libre, para no competir con el primer pintado. */
export function iniciarVitals() {
  const arrancar = async () => {
    const { onCLS, onFCP, onINP, onLCP, onTTFB } = await import('web-vitals')
    onLCP(enviar)
    onCLS(enviar)
    onINP(enviar)
    onFCP(enviar)
    onTTFB(enviar)
  }
  if ('requestIdleCallback' in window) window.requestIdleCallback(() => void arrancar())
  else setTimeout(() => void arrancar(), 2000)
}
