import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, Flash, PageHead, fmt } from '../ui.jsx'

export default function StaleCleanup() {
  const [days, setDays] = useState(90)
  const [data, setData] = useState(null)
  const [bin, setBin] = useState([])
  const [selU, setSelU] = useState([])
  const [selC, setSelC] = useState([])
  const [flash, setFlash] = useState('')

  async function load() {
    const [s, r] = await Promise.all([api.stale(days), api.recycleBin()])
    setData(s)
    setBin(r.items || [])
  }
  useEffect(() => { load().catch((e) => setFlash(e.message)) }, [])

  function toggle(list, setList, id) {
    setList(list.includes(id) ? list.filter((x) => x !== id) : [...list, id])
  }

  async function clean(type, ids) {
    await api.staleCleanup({ objectType: type, ids })
    setFlash(`Moved ${ids.length} ${type}(s) to recycle bin`)
    setSelU([]); setSelC([])
    load()
  }

  return (
    <div>
      <PageHead title="Stale objects & recycle bin" subtitle="Find accounts and computers with no logon in N days, tombstone them, then restore from AD Recycle Bin.">
        <input className="search" style={{ minWidth: 80 }} type="number" value={days} onChange={(e) => setDays(Number(e.target.value))} />
        <button className="btn" onClick={load}>Scan</button>
      </PageHead>
      <Flash msg={flash} />
      {data && (
        <div className="two-col">
          <div className="card">
            <div className="card-head">
              <h3 className="section-title" style={{ margin: 0 }}>Stale users ({data.userCount})</h3>
              <button className="btn danger" disabled={!selU.length} onClick={() => clean('user', selU)}>Move selected</button>
            </div>
            <div className="table-wrap">
              <table>
                <tbody>
                  {data.users.map((u) => (
                    <tr key={u.id}>
                      <td><input type="checkbox" checked={selU.includes(u.id)} onChange={() => toggle(selU, setSelU, u.id)} /></td>
                      <td>{u.displayName}</td>
                      <td className="muted">{fmt(u.lastLogon)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
          <div className="card">
            <div className="card-head">
              <h3 className="section-title" style={{ margin: 0 }}>Stale computers ({data.computerCount})</h3>
              <button className="btn danger" disabled={!selC.length} onClick={() => clean('computer', selC)}>Move selected</button>
            </div>
            <div className="table-wrap">
              <table>
                <tbody>
                  {data.computers.map((c) => (
                    <tr key={c.id}>
                      <td><input type="checkbox" checked={selC.includes(c.id)} onChange={() => toggle(selC, setSelC, c.id)} /></td>
                      <td>{c.name}</td>
                      <td className="muted">{fmt(c.lastLogon)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}
      <div className="card" style={{ marginTop: 14 }}>
        <h3 className="section-title">Recycle bin</h3>
        <div className="table-wrap">
          <table>
            <thead><tr><th>Name</th><th>Type</th><th>Deleted</th><th></th></tr></thead>
            <tbody>
              {bin.map((b) => (
                <tr key={b.id}>
                  <td>{b.name}</td>
                  <td><Badge>{b.objectType}</Badge></td>
                  <td>{fmt(b.deletedAt)}</td>
                  <td><button className="btn primary" onClick={() => api.restoreRecycle(b.id).then(() => { setFlash('Restored'); load() })}>Restore</button></td>
                </tr>
              ))}
            </tbody>
          </table>
          {!bin.length && <div className="empty">Recycle bin is empty</div>}
        </div>
      </div>
    </div>
  )
}
