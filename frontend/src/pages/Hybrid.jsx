import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, Flash, PageHead, fmt } from '../ui.jsx'

export default function Hybrid() {
  const [d, setD] = useState(null)
  const [flash, setFlash] = useState('')

  async function load() { setD(await api.hybrid()) }
  useEffect(() => { load().catch((e) => setFlash(e.message)) }, [])

  async function sync(id) {
    try { await api.hybridSync(id); setFlash('Sync stamped'); load() } catch (e) { setFlash(e.message) }
  }

  if (!d) return <p className="muted">Loading hybrid identity…</p>
  return (
    <div>
      <PageHead title="Hybrid identity" subtitle="On-prem vs Entra ID sync state, ImmutableId / sourceAnchor, and password hash sync." />
      <Flash msg={flash} kind={flash && flash !== 'Sync stamped' ? 'err' : 'ok'} />
      <div className="grid-cards">
        {Object.entries(d.summary).map(([k, v]) => (
          <div key={k} className="card stat"><div className="label">{k}</div><div className="value">{v}</div></div>
        ))}
      </div>
      <div className="card">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>User</th><th>UPN</th><th>ImmutableId</th><th>Cloud UPN</th><th>PHS</th><th>Status</th><th>Last sync</th><th></th>
              </tr>
            </thead>
            <tbody>
              {d.items.map((i) => (
                <tr key={i.userId}>
                  <td>{i.displayName}</td>
                  <td>{i.upn}</td>
                  <td className="muted">{i.immutableId || '—'}</td>
                  <td>{i.cloudUpn || '—'}</td>
                  <td>{i.passwordHashSync ? <Badge kind="ok">PHS</Badge> : <Badge>off</Badge>}</td>
                  <td><Badge kind={i.syncStatus === 'synced' ? 'ok' : i.syncStatus === 'pending' ? 'warn' : 'info'}>{i.syncStatus}</Badge></td>
                  <td>{fmt(i.lastSync)}</td>
                  <td><button className="btn" onClick={() => sync(i.userId)}>Force sync</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
