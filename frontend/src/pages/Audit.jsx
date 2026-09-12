import { api } from '../api.js'
import { Badge, PageHead, fmt, useQueryList } from '../ui.jsx'

export default function Audit() {
  const list = useQueryList(api.audit)
  return (
    <div>
      <PageHead title="Auditing" subtitle="Directory change log for create, update, delete, password, and connection events.">
        <input className="search" placeholder="Search audit log" value={list.q} onChange={(e) => list.setQ(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && list.reload()} />
        <button className="btn" onClick={list.reload}>Refresh</button>
      </PageHead>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Time</th>
              <th>Actor</th>
              <th>Action</th>
              <th>Target</th>
              <th>Detail</th>
              <th>Result</th>
            </tr>
          </thead>
          <tbody>
            {list.data.items.map((a) => (
              <tr key={a.id}>
                <td>{fmt(a.time)}</td>
                <td>{a.actor}</td>
                <td>{a.action}</td>
                <td>{a.targetType} / {a.target}</td>
                <td>{a.detail}</td>
                <td>{a.result === 'success' ? <Badge kind="ok">success</Badge> : <Badge kind="danger">{a.result}</Badge>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="muted">{list.data.total} events</p>
    </div>
  )
}
