import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, PageHead, fmt } from '../ui.jsx'

export default function Logs() {
  const [items, setItems] = useState([])
  const [file, setFile] = useState('')
  const [q, setQ] = useState('')
  const [level, setLevel] = useState('all')
  const [open, setOpen] = useState(null)
  const [err, setErr] = useState('')

  async function load() {
    try {
      const params = new URLSearchParams()
      if (q) params.set('q', q)
      if (level && level !== 'all') params.set('level', level)
      params.set('take', '300')
      const r = await api.logs(`?${params}`)
      setItems(r.items || [])
      setFile(r.file || '')
      setErr('')
    } catch (e) { setErr(e.message) }
  }

  useEffect(() => { load().catch(() => {}) }, [])

  function kind(levelName) {
    if (levelName === 'error') return 'danger'
    if (levelName === 'warn') return 'warn'
    if (levelName === 'debug') return 'info'
    return 'ok'
  }

  return (
    <div>
      <PageHead title="Logs" subtitle="In-depth HTTP, LDAP, and Azure traces. Secrets are redacted. Use this instead of guessing from chat.">
        <input className="search" placeholder="Search logs" value={q} onChange={(e) => setQ(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && load()} />
        <select value={level} onChange={(e) => setLevel(e.target.value)}>
          <option value="all">all levels</option>
          <option value="error">error</option>
          <option value="warn">warn</option>
          <option value="info">info</option>
          <option value="debug">debug</option>
        </select>
        <button className="btn" onClick={load}>Refresh</button>
      </PageHead>
      {err ? <p className="flash err">{err}</p> : null}
      <p className="muted">{file ? `File ${file}` : 'In-memory ring buffer'} — {items.length} entries</p>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Time</th>
              <th>Level</th>
              <th>Source</th>
              <th>Message</th>
            </tr>
          </thead>
          <tbody>
            {items.map((e) => (
              <tr key={e.id} className="row-link" onClick={() => setOpen(open === e.id ? null : e.id)}>
                <td>{fmt(e.time)}</td>
                <td><Badge kind={kind(e.level)}>{e.level}</Badge></td>
                <td>{e.source}</td>
                <td>
                  {e.message}
                  {open === e.id && (e.detail || e.exception) ? (
                    <pre className="password-box" style={{ marginTop: 8, whiteSpace: 'pre-wrap' }}>{[e.detail, e.exception].filter(Boolean).join('\n')}</pre>
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
