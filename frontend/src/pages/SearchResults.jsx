import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../api.js'
import { Badge, PageHead } from '../ui.jsx'

export default function SearchResults() {
  const [params] = useSearchParams()
  const q = params.get('q') || ''
  const [state, setState] = useState({ loading: false, users: [], computers: [], groups: [], error: '' })

  useEffect(() => {
    if (!q) { setState({ loading: false, users: [], computers: [], groups: [], error: '' }); return }
    let active = true
    setState((s) => ({ ...s, loading: true, error: '' }))
    const qs = `?q=${encodeURIComponent(q)}&pageSize=25`
    Promise.all([api.users(qs), api.computers(qs), api.groups(qs)])
      .then(([users, computers, groups]) => {
        if (!active) return
        setState({
          loading: false,
          users: users.items || [],
          computers: computers.items || [],
          groups: groups.items || [],
          error: ''
        })
      })
      .catch((e) => { if (active) setState((s) => ({ ...s, loading: false, error: e.message })) })
    return () => { active = false }
  }, [q])

  const total = state.users.length + state.computers.length + state.groups.length

  return (
    <div>
      <PageHead title={`Search results for "${q}"`} subtitle={state.loading ? 'Searching users, computers, and groups...' : `${total} match${total === 1 ? '' : 'es'} across users, computers, and groups.`} />
      {state.error ? <div className="flash err">{state.error}</div> : null}
      {!state.loading && q && total === 0 && !state.error ? <div className="empty">No matches found.</div> : null}

      <div className="card">
        <h3 className="section-title">Users <span className="muted">({state.users.length})</span></h3>
        <div className="table-wrap">
          <table>
            <thead><tr><th>Name</th><th>Sam</th><th>Type</th><th>Status</th></tr></thead>
            <tbody>
              {state.users.map((u) => (
                <tr key={u.id}>
                  <td><Link to="/users">{u.displayName}</Link></td>
                  <td>{u.samAccountName}</td>
                  <td><Badge kind="info">{u.type}</Badge></td>
                  <td>{u.enabled ? <Badge kind="ok">Enabled</Badge> : <Badge kind="warn">Disabled</Badge>}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {!state.users.length ? <div className="empty">No users</div> : null}
        </div>
      </div>

      <div className="card" style={{ marginTop: 14 }}>
        <h3 className="section-title">Computers <span className="muted">({state.computers.length})</span></h3>
        <div className="table-wrap">
          <table>
            <thead><tr><th>Name</th><th>OS</th><th>Type</th><th>Status</th></tr></thead>
            <tbody>
              {state.computers.map((c) => (
                <tr key={c.id}>
                  <td><Link to="/computers">{c.name}</Link></td>
                  <td>{c.os || '—'}</td>
                  <td><Badge kind="info">{c.type}</Badge></td>
                  <td>{c.enabled ? <Badge kind="ok">Enabled</Badge> : <Badge kind="warn">Disabled</Badge>}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {!state.computers.length ? <div className="empty">No computers</div> : null}
        </div>
      </div>

      <div className="card" style={{ marginTop: 14 }}>
        <h3 className="section-title">Groups <span className="muted">({state.groups.length})</span></h3>
        <div className="table-wrap">
          <table>
            <thead><tr><th>Name</th><th>Sam</th><th>Type</th><th>Scope</th></tr></thead>
            <tbody>
              {state.groups.map((g) => (
                <tr key={g.id}>
                  <td><Link to="/groups">{g.name}</Link></td>
                  <td>{g.samAccountName}</td>
                  <td><Badge kind="info">{g.type}</Badge></td>
                  <td>{g.scope}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {!state.groups.length ? <div className="empty">No groups</div> : null}
        </div>
      </div>
    </div>
  )
}
