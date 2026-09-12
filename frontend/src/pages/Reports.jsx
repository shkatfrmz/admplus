import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, PageHead, fmt } from '../ui.jsx'

function Block({ title, items, render }) {
  return (
    <div className="card" style={{ marginBottom: 14 }}>
      <h3 className="section-title">{title} <Badge>{items.length}</Badge></h3>
      {!items.length ? <p className="muted">None</p> : (
        <div className="table-wrap">
          <table>
            <tbody>
              {items.slice(0, 40).map((x) => <tr key={x.id || x.name || JSON.stringify(x)}><td>{render(x)}</td></tr>)}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

export default function Reports() {
  const [r, setR] = useState(null)
  const [err, setErr] = useState('')

  useEffect(() => {
    api.reports().then(setR).catch((e) => setErr(e.message))
  }, [])

  if (err) return <p className="flash err">{err}</p>
  if (!r) return <p className="muted">Building reports...</p>

  const s = r.summaries
  const cards = [
    { label: 'Inactive users', value: s.inactiveUsers },
    { label: 'Disabled users', value: s.disabledUsers },
    { label: 'Password never expires', value: s.passwordNeverExpires },
    { label: 'Empty groups', value: s.emptyGroups },
    { label: 'Unlinked GPOs', value: s.unlinkedGpos },
    { label: 'LAPS expiring soon', value: s.lapsExpiringSoon },
    { label: 'Computers without LAPS', value: s.computersNoLaps }
  ]

  return (
    <div>
      <PageHead title="Reports" subtitle={`Generated ${fmt(r.generatedAt)}. Security and hygiene views across the directory.`} />
      <div className="grid-cards">
        {cards.map((c) => (
          <div key={c.label} className="card stat">
            <div className="label">{c.label}</div>
            <div className="value">{c.value}</div>
          </div>
        ))}
      </div>

      <div className="card" style={{ marginBottom: 14 }}>
        <h3 className="section-title">Privileged group membership</h3>
        {(r.privileged || []).map((g) => (
          <div key={g.group} style={{ marginBottom: 8 }}>
            <strong>{g.group}</strong>
            <div className="pill-row" style={{ marginTop: 6 }}>
              {g.members.map((m) => <Badge key={m.id} kind="warn">{m.name} ({m.sam})</Badge>)}
            </div>
          </div>
        ))}
      </div>

      <Block title="Inactive / never logged on users" items={r.inactiveUsers} render={(u) => `${u.displayName} — ${u.samAccountName} — last logon ${fmt(u.lastLogon)}`} />
      <Block title="Disabled users" items={r.disabledUsers} render={(u) => `${u.displayName} (${u.samAccountName})`} />
      <Block title="Password never expires" items={r.passwordNeverExpires} render={(u) => `${u.displayName} (${u.type})`} />
      <Block title="Empty groups" items={r.emptyGroups} render={(g) => `${g.name} — ${g.type}/${g.scope}`} />
      <Block title="Unlinked GPOs" items={r.unlinkedGpos} render={(g) => g.name} />
      <Block title="LAPS expiring within 14 days" items={r.lapsSoon} render={(l) => `${l.computerName} expires ${fmt(l.expiration)}`} />
      <Block title="Computers without LAPS" items={r.computersNoLaps} render={(c) => `${c.name} (${c.type})`} />
    </div>
  )
}
