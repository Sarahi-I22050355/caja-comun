import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Navigate, RouterProvider, createBrowserRouter, useLocation } from 'react-router-dom'
import { CajaActivaProvider } from './auth/CajaActiva'
import { SesionProvider, useSesion } from './auth/Sesion'
import Shell from './layout/Shell'
import { ApiError } from './lib/api'
import { Skeleton } from './ui/base'
import { ToastProvider } from './ui/Toast'

const cliente = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      // Un 4xx no se arregla reintentando; los fallos de red o 5xx se reintentan una vez.
      retry: (n, e) => !(e instanceof ApiError && e.status < 500) && n < 1,
    },
  },
})

function Protegida() {
  const { usuario, iniciando } = useSesion()
  const ubicacion = useLocation()
  if (iniciando) {
    return (
      <div className="flex min-h-screen items-center justify-center p-8" role="status" aria-busy>
        <Skeleton className="h-10 w-64" />
      </div>
    )
  }
  if (!usuario) return <Navigate to="/login" replace state={{ desde: ubicacion.pathname }} />
  return (
    <CajaActivaProvider>
      <Shell />
    </CajaActivaProvider>
  )
}

const pagina = (cargar: () => Promise<{ default: React.ComponentType }>) => async () => ({ Component: (await cargar()).default })

// Cada pantalla se carga cuando se visita (code splitting por ruta).
// Mientras llega el código de la primera pantalla (rutas lazy) se muestra este marcador, no una página en blanco.
const espera = (
  <div className="flex min-h-screen items-center justify-center p-8" role="status" aria-busy>
    <Skeleton className="h-10 w-64" />
  </div>
)

const router = createBrowserRouter([
  { path: '/login', hydrateFallbackElement: espera, lazy: pagina(() => import('./modulos/login/Pagina')) },
  {
    element: <Protegida />,
    hydrateFallbackElement: espera,
    children: [
      { index: true, lazy: pagina(() => import('./modulos/inicio/Pagina')) },
      { path: 'cajas', lazy: pagina(() => import('./modulos/cajas/Pagina')) },
      { path: 'ahorradores', lazy: pagina(() => import('./modulos/ahorradores/Lista')) },
      { path: 'ahorradores/:id', lazy: pagina(() => import('./modulos/ahorradores/Perfil')) },
      { path: 'sincronizacion', lazy: pagina(() => import('./modulos/sincronizacion/Pagina')) },
      { path: 'nomina', lazy: pagina(() => import('./modulos/nomina/Pagina')) },
      { path: 'ahorro-voluntario', lazy: pagina(() => import('./modulos/voluntario/Pagina')) },
      { path: 'prestamos', lazy: pagina(() => import('./modulos/prestamos/Lista')) },
      { path: 'prestamos/nuevo', lazy: pagina(() => import('./modulos/prestamos/Nuevo')) },
      { path: 'prestamos/:id', lazy: pagina(() => import('./modulos/prestamos/Detalle')) },
      { path: 'cierre-ciclo', lazy: pagina(() => import('./modulos/cierre/Pagina')) },
      { path: 'reportes', lazy: pagina(() => import('./modulos/reportes/Pagina')) },
      { path: 'tareas', lazy: pagina(() => import('./modulos/tareas/Pagina')) },
      { path: 'consultas', lazy: pagina(() => import('./modulos/consultas/Pagina')) },
      { path: 'usuarios', lazy: pagina(() => import('./modulos/usuarios/Pagina')) },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
])

export default function App() {
  return (
    <QueryClientProvider client={cliente}>
      <ToastProvider>
        <SesionProvider>
          <RouterProvider router={router} />
        </SesionProvider>
      </ToastProvider>
    </QueryClientProvider>
  )
}
