import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { dinero, dineroCorto } from '../../lib/formato'

export interface PuntoSerie {
  mes: string
  ahorro: number
  prestamos: number
}

/** Barras de ahorro contra préstamos por mes. Vive aparte para que recharts (la dependencia más pesada) se cargue sin bloquear los KPIs. */
export default function Grafica({ serie, titulo, ahorro, prestamos }: { serie: PuntoSerie[]; titulo: string; ahorro: string; prestamos: string }) {
  return (
    <div role="img" aria-label={titulo} className="h-72 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={serie} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#F8BBD0" vertical={false} />
          <XAxis dataKey="mes" tickLine={false} axisLine={false} tick={{ fill: '#5B3F43', fontSize: 12 }} />
          <YAxis tickFormatter={dineroCorto} tickLine={false} axisLine={false} width={64} tick={{ fill: '#5B3F43', fontSize: 12 }} />
          <Tooltip formatter={(v) => dinero(Number(v), true)} cursor={{ fill: '#FCE4EC' }} />
          <Legend iconType="circle" />
          <Bar dataKey="ahorro" name={ahorro} fill="#E91E63" radius={[6, 6, 0, 0]} />
          <Bar dataKey="prestamos" name={prestamos} fill="#880E4F" radius={[6, 6, 0, 0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}
