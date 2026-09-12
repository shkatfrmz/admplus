import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Flash, Modal, PageHead, fmt } from '../ui.jsx'

export default function BitLocker() {
  const [items, setItems] = useState([])
  const [q, setQ] = useState('')
  const [secret, setSecret] = useState(null)
  const [flash, setFlash] = useState('')

  async function load() {
    const r = await api.bitlocker(q ? `?q=${encodeURIComponent(q)}` : '')
    setItems(r.items || [])
  }
  useEffect(() => { load().catch((e) => setFlash(e.message)) }, [])

  async function reveal(id) {
    try { setSecret(await api.bitlockerOne(id)) } catch (e) { setFlash(e.message) }
  }

  return (
    <div>
      <PageHead title="BitLocker recovery" subtitle="Look up msFVE-RecoveryPassword / recovery GUID stored in Active Directory.">
        <input className="search" placeholder="Computer or GUID" value={q} onChange={(e) => setQ(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && load()} />
        <button className="btn" onClick={load}>Search</button>
      </PageHead>
      <Flash msg={flash} kind="err" />
      <div className="card">
        <div className="table-wrap">
          <table>
            <thead><tr><th>Computer</th><th>Volume</th><th>Recovery GUID</th><th>Created</th><th></th></tr></thead>
            <tbody>
              {items.map((k) => (
                <tr key={k.id}>
                  <td>{k.computerName}</td>
                  <td>{k.volume}</td>
                  <td>{k.recoveryGuid}</td>
                  <td>{fmt(k.created)}</td>
                  <td><button className="btn" onClick={() => reveal(k.id)}>Show password</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      {secret && (
        <Modal title={`BitLocker — ${secret.computerName}`} onClose={() => setSecret(null)}>
          <p className="muted">Retrieval is audited. Recovery GUID {secret.recoveryGuid}</p>
          <div className="password-box">{secret.recoveryPassword}</div>
        </Modal>
      )}
    </div>
  )
}
