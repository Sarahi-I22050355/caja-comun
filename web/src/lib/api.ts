// Cliente de la API. El token de acceso vive solo en memoria; el de refresco, en una cookie HttpOnly que maneja el
// navegador. Ante un 401 se intenta un refresco (una sola vez por petición) y, si falla, se avisa que la sesión venció.

const BASE = '/api/v1'

export class ApiError extends Error {
  status: number
  code: string
  errors?: Record<string, string[]>
  constructor(status: number, code: string, message: string, errors?: Record<string, string[]>) {
    super(message)
    this.status = status
    this.code = code
    this.errors = errors
  }
}

let token: string | null = null
let alVencer: (() => void) | null = null
let refrescando: Promise<boolean> | null = null

export const sesion = {
  fijarToken(t: string | null) {
    token = t
  },
  alVencer(f: (() => void) | null) {
    alVencer = f
  },
}

async function aError(res: Response): Promise<ApiError> {
  let cuerpo: { code?: string; detail?: string; errors?: Record<string, string[]> } = {}
  try {
    cuerpo = await res.json()
  } catch {
    /* sin cuerpo JSON */
  }
  return new ApiError(res.status, cuerpo.code ?? `HTTP_${res.status}`, cuerpo.detail ?? res.statusText, cuerpo.errors)
}

/** Intenta obtener un token nuevo con la cookie de refresco. Peticiones simultáneas comparten el mismo intento. */
export function refrescar(): Promise<boolean> {
  refrescando ??= (async () => {
    try {
      const res = await fetch(`${BASE}/auth/refresh`, { method: 'POST', credentials: 'same-origin' })
      if (!res.ok) return false
      const datos = (await res.json()) as { accessToken: string }
      token = datos.accessToken
      return true
    } catch {
      return false
    } finally {
      setTimeout(() => (refrescando = null), 0)
    }
  })()
  return refrescando
}

async function pedir(path: string, init: RequestInit = {}, reintentar = true): Promise<Response> {
  const headers = new Headers(init.headers)
  if (token) headers.set('Authorization', `Bearer ${token}`)
  const res = await fetch(BASE + path, { ...init, headers, credentials: 'same-origin' })
  if (res.status === 401 && reintentar && !path.startsWith('/auth/')) {
    if (await refrescar()) return pedir(path, init, false)
    alVencer?.()
  }
  if (!res.ok) throw await aError(res)
  return res
}

type Params = Record<string, string | number | boolean | null | undefined>

export function conParams(path: string, params?: Params): string {
  if (!params) return path
  const q = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') q.set(k, String(v))
  const s = q.toString()
  return s ? `${path}?${s}` : path
}

const json = (cuerpo: unknown): RequestInit => ({
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(cuerpo),
})

async function leer<T>(res: Response): Promise<T> {
  if (res.status === 204) return undefined as T
  const texto = await res.text()
  return (texto ? JSON.parse(texto) : undefined) as T
}

export interface Archivo {
  blob: Blob
  nombre: string
  cabeceras: Headers
}

function nombreDe(res: Response): string {
  const d = res.headers.get('Content-Disposition') ?? ''
  const m = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(d)
  return m ? decodeURIComponent(m[1]) : 'archivo'
}

export const api = {
  get: async <T>(path: string, params?: Params): Promise<T> => leer<T>(await pedir(conParams(path, params))),
  post: async <T>(path: string, cuerpo?: unknown, params?: Params): Promise<T> =>
    leer<T>(await pedir(conParams(path, params), { method: 'POST', ...(cuerpo === undefined ? {} : json(cuerpo)) })),
  put: async <T>(path: string, cuerpo: unknown): Promise<T> => leer<T>(await pedir(path, { method: 'PUT', ...json(cuerpo) })),
  delete: async (path: string): Promise<void> => {
    await pedir(path, { method: 'DELETE' })
  },
  subir: async <T>(path: string, campos: Record<string, string>, archivo: File): Promise<T> => {
    const f = new FormData()
    for (const [k, v] of Object.entries(campos)) f.append(k, v)
    f.append('archivo', archivo)
    return leer<T>(await pedir(path, { method: 'POST', body: f }))
  },
  /** Descarga un archivo (GET o POST) y devuelve el blob con su nombre y las cabeceras X-*. */
  archivo: async (path: string, params?: Params, metodo: 'GET' | 'POST' = 'GET'): Promise<Archivo> => {
    const res = await pedir(conParams(path, params), { method: metodo })
    return { blob: await res.blob(), nombre: nombreDe(res), cabeceras: res.headers }
  },
}

/** Guarda un archivo descargado con el navegador. */
export function guardar(a: Archivo) {
  const url = URL.createObjectURL(a.blob)
  const enlace = document.createElement('a')
  enlace.href = url
  enlace.download = a.nombre
  document.body.appendChild(enlace)
  enlace.click()
  enlace.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}
