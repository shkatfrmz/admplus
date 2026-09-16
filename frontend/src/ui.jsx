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
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(options.pageSize || 50)
  const [data, setData] = useState({ items: [], total: 0, page: 1, pageSize: options.pageSize || 50 })
  const [err, setErr] = useState('')
  const [loading, setLoading] = useState(true)
  const loaderRef = useRef(loader)
  loaderRef.current = loader
  const mounted = useRef(true)
  const stateRef = useRef({})
  stateRef.current = { q, type, page, pageSize }

  useEffect(() => () => { mounted.current = false }, [])

  const reload = useCallback(async (overrides = {}) => {
    const s = stateRef.current
    const nextQ = overrides.q !== undefined ? overrides.q : s.q
    const nextType = overrides.type !== undefined ? overrides.type : s.type
    const nextPage = overrides.page !== undefined ? overrides.page : s.page
    const nextSize = overrides.pageSize !== undefined ? overrides.pageSize : s.pageSize
    setLoading(true)
    setErr('')
    try {
      const params = new URLSearchParams()
      if (nextQ) params.set('q', nextQ)
      if (nextType) params.set('type', nextType)
      params.set('page', String(Math.max(1, nextPage)))
      params.set('pageSize', String(nextSize))
      const result = await loaderRef.current(`?${params}`)
      if (mounted.current) {
        setData(result)
        if (result.page && result.page !== nextPage) setPage(result.page)
      }
    } catch (e) {
      if (mounted.current) setErr(e.message)
    } finally {
      if (mounted.current) setLoading(false)
    }
  }, [])

  function changeQ(value) { setQ(value); setPage(1) }
  function changeType(value) { setType(value); setPage(1) }
  function changePage(value) { setPage(Math.max(1, value)); return reload({ page: value }) }
  function changePageSize(value) { setPageSize(value); setPage(1); return reload({ page: 1, pageSize: value }) }

  useEffect(() => { reload() }, [type, ...deps])

  const first = useRef(true)
  const debounce = options.debounce ?? 300
  useEffect(() => {
    if (first.current) { first.current = false; return }
    const t = setTimeout(() => reload({ q, page: 1 }), debounce)
    return () => clearTimeout(t)
  }, [q, debounce])

  return {
    q, setQ: changeQ, type, setType: changeType,
    page, pageSize, setPage: changePage, setPageSize: changePageSize,
    total: data.total || 0, pageCount: Math.max(1, Math.ceil((data.total || 0) / pageSize)),
    data, err, loading, reload
  }
}

export function Pager({ list, sizes = [50, 100, 200, 500, 1000] }) {
  const { page, pageCount, total, pageSize } = list
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, total)
  return (
    <div className="pager">
      <span className="muted">Showing {from}-{to} of {total}</span>
      <div className="pager-controls">
        <label className="muted">Rows
          <select value={pageSize} onChange={(e) => list.setPageSize(Number(e.target.value))}>
            {sizes.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </label>
        <button className="btn" type="button" disabled={page <= 1 || list.loading} onClick={() => list.setPage(1)}>First</button>
        <button className="btn" type="button" disabled={page <= 1 || list.loading} onClick={() => list.setPage(page - 1)}>Prev</button>
        <span className="muted">Page {page} / {pageCount}</span>
        <button className="btn" type="button" disabled={page >= pageCount || list.loading} onClick={() => list.setPage(page + 1)}>Next</button>
        <button className="btn" type="button" disabled={page >= pageCount || list.loading} onClick={() => list.setPage(pageCount)}>Last</button>
      </div>
    </div>
  )
}

export function Field({ label, children, full }) {
  return <label className={full ? 'full' : ''}>{label}{children}</label>
}

export function fmt(ts) {
  if (!ts) return '—'
  try { return new Date(ts).toLocaleString() } catch { return ts }
}
