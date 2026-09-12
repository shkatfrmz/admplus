import { useEffect, useState } from 'react'

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

export function useQueryList(loader, deps = []) {
  const [q, setQ] = useState('')
  const [type, setType] = useState('')
  const [data, setData] = useState({ items: [], total: 0 })
  const [err, setErr] = useState('')
  const [loading, setLoading] = useState(true)

  async function reload() {
    setLoading(true)
    setErr('')
    try {
      const params = new URLSearchParams()
      if (q) params.set('q', q)
      if (type) params.set('type', type)
      const qs = params.toString() ? `?${params}` : ''
      setData(await loader(qs))
    } catch (e) {
      setErr(e.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { reload() }, [type, ...deps])

  return { q, setQ, type, setType, data, err, loading, reload }
}

export function Field({ label, children, full }) {
  return <label className={full ? 'full' : ''}>{label}{children}</label>
}

export function fmt(ts) {
  if (!ts) return '—'
  try { return new Date(ts).toLocaleString() } catch { return ts }
}
