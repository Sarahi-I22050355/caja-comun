import type { Config } from 'tailwindcss'

export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        // DEFAULT es el rosa de la marca (rellenos, bordes, íconos). solido y texto son versiones un poco más oscuras para que el
        // texto blanco sobre rosa y el texto rosa sobre blanco lleguen a contraste AA (4.5:1).
        primary: { DEFAULT: '#E91E63', hover: '#D81B60', pressed: '#C2185B', solido: '#D81B60', texto: '#C2185B' },
        vino: '#880E4F',
        lienzo: '#FFF8FA',
        tinte: '#FCE4EC',
        borde: '#F8BBD0',
        suave: '#5B3F43',
        estado: {
          ok: '#2E7D32',
          'ok-bg': '#E8F5E9',
          pendiente: '#ED6C02',
          'pendiente-bg': '#FFF3E0',
          baja: '#D32F2F',
          'baja-bg': '#FFEBEE',
          pausado: '#757575',
          'pausado-bg': '#F5F5F5',
        },
        serie: ['#E91E63', '#C2185B', '#880E4F', '#F48FB1', '#E0E0E0'],
      },
      fontFamily: {
        sans: ['"Plus Jakarta Sans"', 'system-ui', 'sans-serif'],
      },
      borderRadius: { control: '0.75rem', tarjeta: '1rem', modal: '1.25rem' },
      boxShadow: {
        n1: '0 2px 8px -2px rgba(233, 30, 99, 0.08)',
        n2: '0 8px 24px -4px rgba(233, 30, 99, 0.12)',
        n3: '0 16px 40px -8px rgba(136, 14, 79, 0.16)',
        foco: '0 0 0 3px rgba(233, 30, 99, 0.25)',
      },
      maxWidth: { contenido: '1600px' },
    },
  },
  plugins: [],
} satisfies Config
