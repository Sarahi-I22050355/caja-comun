import { describe, expect, it } from 'vitest'
import i18n from '../i18n'
import { dinero, dineroCorto, fecha, iniciales, porcentaje } from './formato'

describe('formato', () => {
  it('muestra el dinero con separador de miles y dos decimales', () => {
    expect(dinero(1234.5)).toBe('$1,234.50')
    expect(dinero(1234.5, true)).toBe('$1,234.50 MXN')
    expect(dinero(-10)).toBe('-$10.00')
    expect(dinero(null)).toBe('—')
  })

  it('abrevia importes grandes para las gráficas', () => {
    expect(dineroCorto(950)).toBe('$950')
    expect(dineroCorto(12_500)).toBe('$12.5k')
    expect(dineroCorto(2_340_000)).toBe('$2.34M')
  })

  it('formatea porcentajes sobre fracciones', () => {
    expect(porcentaje(0.015)).toBe('1.50 %')
  })

  it('no recorre una fecha de calendario por la zona horaria', () => {
    expect(fecha('2026-01-01')).toContain('01')
    expect(fecha('2026-01-01')).toContain('2026')
  })

  it('el idioma cambia el nombre del mes pero no el importe', async () => {
    expect(fecha('2026-03-15', 'larga').toLowerCase()).toContain('marzo')
    await i18n.changeLanguage('en')
    expect(fecha('2026-03-15', 'larga')).toContain('March')
    expect(dinero(1234.5)).toBe('$1,234.50')
  })

  it('saca las iniciales', () => {
    expect(iniciales('Ana López Ruiz')).toBe('AL')
  })
})
