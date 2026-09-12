import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, Field, Flash, Modal, PageHead, fmt, useQueryList } from '../ui.jsx'

export default function Laps() {
  const list = useQueryList(api.laps)
  const [computers, setComputers] = useState([])
  const [computerId, setComputerId] = useState('')
  const [flash, setFlash] = useState('')
  const [secret, setSecret] = useState(null)

  useEffect(() => {
    api.computers('?pageSize=200').then((r) => setComputers(r.items)).catch(() => {})
  }, [])

  async function enroll() {
    try {
      await api.createLaps({ computerId })
      setFlash('LAPS enrolled')
      list.reload()
    } catch (e) { setFlash(e.message) }
  }

  async function reveal(id) {
    try { setSecret(await api.lapsOne(id)) } catch (e) { setFlash(e.message) }
  }

  async function rotate(id) {
    try {
      const item = await api.rotateLaps(id)
      setSecret(item)
      setFlash('Password rotated')
      list.reload()
    } catch (e) { setFlash(e.message) }
  }

  return (
    <div>
      <PageHead title="LAPS passwords" subtitle="Local Administrator Password Solution — retrieve, rotate, and enroll computers.">
        <input className="search" placeholder="Search" value={list.q} onChange={(e) => list.setQ(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && list.reload()} />
        <button className="btn" onClick={list.reload}>Refresh</button>
      </PageHead>
      <Flash msg={flash} kind={flash && !/enrolled|rotated/.test(flash) ? 'err' : 'ok'} />

      <div className="card" style={{ marginBottom: 14 }}>
        <h3 className="section-title">Enroll computer</h3>
        <div className="toolbar">
          <select value={computerId} onChange={(e) => setComputerId(e.target.value)}>
            <option value="">Select computer</option>
            {computers.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
          <button className="btn primary" disabled={!computerId} onClick={enroll}>Enable LAPS</button>
        </div>
      </div>

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Computer</th>
              <th>Account</th>
              <th>Expires</th>
              <th>Days left</th>
              <th>Last rotated</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {list.data.items.map((l) => (
              <tr key={l.id}>
                <td>{l.computerName}</td>
                <td>{l.account}</td>
                <td>{fmt(l.expiration)}</td>
                <td>{l.daysRemaining < 7 ? <Badge kind="danger">{l.daysRemaining}</Badge> : <Badge kind="ok">{l.daysRemaining}</Badge>}</td>
                <td>{fmt(l.lastRotated)}</td>
                <td className="actions">
                  <button className="btn" onClick={() => reveal(l.id)}>Show password</button>
                  <button className="btn" onClick={() => rotate(l.id)}>Rotate</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {secret && (
        <Modal title={`LAPS — ${secret.computerName}`} onClose={() => setSecret(null)}>
          <Field label="Local admin account"><input readOnly value={secret.account} /></Field>
          <p className="muted">Password retrieval is audited.</p>
          <div className="password-box">{secret.password}</div>
          <p className="muted">Expires {fmt(secret.expiration)}</p>
          <div className="actions" style={{ marginTop: 12 }}>
            <button className="btn" onClick={() => navigator.clipboard.writeText(secret.password)}>Copy</button>
            <button className="btn primary" onClick={() => rotate(secret.id)}>Rotate now</button>
          </div>
        </Modal>
      )}
    </div>
  )
}
