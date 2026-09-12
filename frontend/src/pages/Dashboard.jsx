import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api.js'
import { Badge, fmt } from '../ui.jsx'

function Spark({ color = '#f2b93b' }) {
  return (
    <svg className="stat-spark" viewBox="0 0 220 54" preserveAspectRatio="none">
      <path d="M0 40 C 20 38, 40 42, 60 28 S 100 12, 140 18 S 180 34, 220 8" fill="none" stroke={color} strokeWidth="2.4" />
      <path d="M0 54 C 20 38, 40 42, 60 28 S 100 12, 140 18 S 180 34, 220 8 L 220 54 Z" fill={color} opacity="0.08" />
    </svg>
  )
}

function AreaChart() {
  return (
    <svg className="area-chart" viewBox="0 0 640 180" preserveAspectRatio="none">
      <defs>
        <linearGradient id="areaFill" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#12a394" stopOpacity="0.28" />
          <stop offset="100%" stopColor="#12a394" stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d="M0 130 C 50 120, 90 128, 130 100 S 210 40, 260 55 S 340 110, 390 70 S 500 30, 560 48 L 640 80 L 640 180 L 0 180 Z" fill="url(#areaFill)" />
      <path d="M0 130 C 50 120, 90 128, 130 100 S 210 40, 260 55 S 340 110, 390 70 S 500 30, 560 48 L 640 80" fill="none" stroke="#12a394" strokeWidth="2.6" />
      <circle cx="260" cy="55" r="6" fill="#12a394" />
      <rect x="222" y="18" width="78" height="24" rx="12" fill="#1c2b28" />
      <text x="261" y="34" textAnchor="middle" fill="#fff" fontSize="11" fontFamily="Plus Jakarta Sans">peak</text>
    </svg>
  )
}

export default function Dashboard() {
  const [d, setD] = useState(null)
  const [err, setErr] = useState('')

  useEffect(() => {
    api.dashboard().then(setD).catch((e) => setErr(e.message))
  }, [])

  if (err) return <p className="flash err">{err}</p>
  if (!d) return <p className="muted">Loading directory snapshot...</p>

  const enabled = d.counts.enabledUsers || 0
  const total = d.counts.users || 1
  const disabled = d.counts.disabledUsers || 0
  const locked = d.counts.lockedUsers || 0
  const donut = `conic-gradient(#12a394 0 ${Math.round((enabled / total) * 360)}deg, #f07a62 0 ${Math.round(((enabled + disabled) / total) * 360)}deg, #d9e4e0 0)`

  const groupRows = Object.entries(d.groupTypes || {}).map(([k, n]) => ({
    label: k, pct: Math.round((n / Math.max(1, d.counts.groups)) * 100)
  }))

  return (
    <div>
      <div className="page-head">
        <div>
          <h2>Welcome back, Admin</h2>
          <p>Here is your current Active Directory overview.</p>
        </div>
        <div className="toolbar">
          <Link className="btn" to="/settings">Connections</Link>
          <Link className="btn primary" to="/reports">Open reports</Link>
        </div>
      </div>

      <div className="dash-grid">
        <Link to="/users" className="card stat" style={{ color: 'inherit' }}>
          <div className="label">Enabled users</div>
          <div className="value">{d.counts.enabledUsers}</div>
          <div className="hint">{d.counts.users} total accounts</div>
          <Spark color="#f2b93b" />
        </Link>
        <Link to="/computers" className="card stat" style={{ color: 'inherit' }}>
          <div className="label">Computers</div>
          <div className="value">{d.counts.computers}</div>
          <div className="hint">{d.counts.computersOnline} enabled in directory</div>
          <Spark color="#f07a62" />
        </Link>
        <div className="card">
          <div className="card-head">
            <h3 className="section-title" style={{ margin: 0 }}>Accounts</h3>
            <span className="chip">Directory</span>
          </div>
          <div className="donut-wrap">
            <div className="donut" style={{ background: donut }}>
              <div className="donut-inner">
                <strong>{d.counts.users}</strong>
                <span className="muted" style={{ fontSize: 11 }}>Total</span>
              </div>
            </div>
            <div className="legend">
              <div><i style={{ background: '#12a394' }} /> Enabled {enabled}</div>
              <div><i style={{ background: '#f07a62' }} /> Disabled {disabled}</div>
              <div><i style={{ background: '#d9e4e0' }} /> Locked {locked}</div>
            </div>
          </div>
        </div>
      </div>

      <div className="dash-mid">
        <div className="card">
          <div className="card-head">
            <h3 className="section-title" style={{ margin: 0 }}>Directory activity</h3>
            <span className="chip">Last year</span>
          </div>
          <AreaChart />
          <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>Audit volume across create, update, and policy changes.</div>
        </div>
        <div className="card">
          <h3 className="section-title">Connections</h3>
          <div className="bars-h">
            <div className="row"><span>Domain DC</span><div className="track"><span style={{ width: d.connections.domainController.connected ? '92%' : '18%', background: '#12a394' }} /></div><b>{d.connections.domainController.connected ? '92' : '18'}</b></div>
            <div className="row"><span>Azure AD</span><div className="track"><span style={{ width: d.connections.azureAd.connected ? '88%' : '22%' }} /></div><b>{d.connections.azureAd.connected ? '88' : '22'}</b></div>
            <div className="row"><span>GPOs</span><div className="track"><span style={{ width: `${Math.min(100, d.counts.gpoEnabled * 22)}%` }} /></div><b>{d.counts.gpoEnabled}</b></div>
            <div className="row"><span>Shares</span><div className="track"><span style={{ width: `${Math.min(100, d.counts.shares * 28)}%` }} /></div><b>{d.counts.shares}</b></div>
          </div>
          <p className="muted" style={{ marginTop: 12, fontSize: 12 }}>{d.connections.domainController.host || 'No DC host configured'}</p>
        </div>
        <div className="card">
          <h3 className="section-title">Group mix</h3>
          <div className="bars-h">
            {(groupRows.length ? groupRows : [{ label: 'security', pct: 80 }, { label: 'distribution', pct: 20 }]).map((r) => (
              <div className="row" key={r.label}><span>{r.label}</span><div className="track"><span style={{ width: `${r.pct}%` }} /></div><b>{r.pct}</b></div>
            ))}
          </div>
          <p className="muted" style={{ marginTop: 12, fontSize: 12 }}>{d.counts.groups} groups · {d.counts.lapsExpiring} LAPS expiring</p>
        </div>
      </div>

      <div className="dash-bottom">
        <div className="card">
          <div className="card-head">
            <h3 className="section-title" style={{ margin: 0 }}>Recent audit activity</h3>
            <Link className="btn" to="/audit">View all</Link>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Time</th>
                  <th>Action</th>
                  <th>Target</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {d.recentAudit.map((a) => (
                  <tr key={a.id}>
                    <td>{fmt(a.time)}</td>
                    <td>{a.action}</td>
                    <td>{a.targetType} / {a.target}</td>
                    <td>{a.result === 'success' ? <Badge kind="ok">In sync</Badge> : <Badge kind="danger">{a.result}</Badge>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
        <div className="card">
          <h3 className="section-title">Health signals</h3>
          {[
            { name: 'LAPS coverage', extra: `${d.counts.laps} enrolled`, amt: d.counts.lapsExpiring ? `-${d.counts.lapsExpiring}` : '+ok' },
            { name: 'Group Policy', extra: `${d.counts.gpoEnabled} enabled`, amt: `+${d.counts.gpos}` },
            { name: 'Share folders', extra: 'SMB inventory', amt: `+${d.counts.shares}` },
            { name: 'Locked users', extra: 'Account lockout', amt: locked ? `-${locked}` : '+0' }
          ].map((p) => (
            <div className="person-row" key={p.name}>
              <div className="avatar" style={{ width: 34, height: 34, fontSize: 11 }}>{p.name.slice(0, 2).toUpperCase()}</div>
              <div className="meta"><strong>{p.name}</strong><span>{p.extra}</span></div>
              <div className="amt">{p.amt}</div>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
