import { useEffect, useState } from 'react'

type Estado = 'cargando' | 'ok' | 'error'

export default function App() {
  const [estado, setEstado] = useState<Estado>('cargando')

  useEffect(() => {
    fetch('/api/health/ready')
      .then((r) => setEstado(r.ok ? 'ok' : 'error'))
      .catch(() => setEstado('error'))
  }, [])

  return (
    <main className="mx-auto flex min-h-screen max-w-contenido flex-col items-center justify-center gap-3 px-4">
      <h1 className="text-4xl font-extrabold text-vino">Caja Común</h1>
      <p>Sistema de Administración de Cajas de Ahorro</p>
      <p className="text-sm" role="status">
        API: {estado === 'cargando' ? 'verificando…' : estado === 'ok' ? 'disponible' : 'sin respuesta'}
      </p>
    </main>
  )
}
