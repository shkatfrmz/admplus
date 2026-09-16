import { useCallback, useEffect, useRef, useState } from 'react'

export function PageHead({ title, subtitle, children }) {
  return (
    <div className="page-head">
      <div>
        <h2>{title}</h2>
        {subtitle ? <p>{subtitle}</p> : null}
      </div>
      <div className="toolbar">{children}</div>
    </div>
  )
}

export function Badge({ kind, children }) {
  return <span className={`badge ${kind || ''}`}>{children}</span>
}

export function Flash({ msg, kind }) {
  if (!msg) return null
  return <div className={`flash ${kind || 'ok'}`}>{msg}</div>
}

export function Modal({ title, onClose, children }) {
  return (
    <div className="modal-back" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h3>{title}</h3>
          <button className="btn ghost" onClick={onClose}>Close</button>
        </div>
        {children}
      </div>
    </div>
  )
}

export function useQueryList(loader, deps = [], options = {}) {
  const [q, setQ] = useState(options.initialQ || '')
  const [type, setType] = useState('')
  const [data, setData] = useState({ items: [], total: 0 })
  const [err, setErr] = useState('')
  const [loading, setLoading] = useState(true)
  const loaderRef = useRef(loader)
  loaderRef.current = loader
  const mounted = useRef(true)

  useEffect(() => () => { mounted.current = false }, [])

  const reload = useCallback(async (overrides = {}) => {
    setLoading(true)
    setErr('')
    try {
      const nextQ = overrides.q !== undefined ? overrides.q : q
      const nextType = overrides.type !== undefined ? overrides.type : type
      const params = new URLSearchParams()
      if (nextQ) params.set('q', nextQ)
      if (nextType) params.set('type', nextType)
      if (overrides.page) params.set('page', String(overrides.page))
      const qs = params.toString() ? `?${params}` : ''
      const result = await loaderRef.current(qs)
      if (mounted.current) setData(result)
    } catch (e) {
      if (mounted.current) setErr(e.message)
    } finally {
      if (mounted.current) setLoading(false)
    }
  }, [q, type])

  useEffect(() => { reload() }, [type, ...deps])

  const first = useRef(true)
  const debounce = options.debounce ?? 250
  useEffect(() => {
    if (first.current) { first.current = false; return }
    if (!q) { reload({ q: '' }); return }
    const t = setTimeout(() => reload({ q }), debounce)
    return () => clearTimeout(t)
  }, [q, debounce])

  return { q, setQ, type, setType, data, err, loading, reload }
}

export function Field({ label, children, full }) {
  return <label className={full ? 'full' : ''}>{label}{children}</label>
}

export function fmt(ts) {
  if (!ts) return '—'
  try { return new Date(ts).toLocaleString() } catch { return ts }
}
