import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, Field, Flash, Modal, PageHead, fmt } from '../ui.jsx'

export default function Operators() {
  const [ops, setOps] = useState([])
  const [current, setCurrent] = useState('')
  const [grants, setGrants] = useState([])
  const [computers, setComputers] = useState([])
  const [flash, setFlash] = useState('')
  const [modal, setModal] = useState(false)
  const [form, setForm] = useState({ name: '', upn: '', role: 'helpdesk' })
  const [jit, setJit] = useState({ operatorId: '', computerId: '', reason: '' })

  async function load() {
    const [o, g, c] = await Promise.all([api.operators(), api.lapsGrants(), api.computers('?pageSize=200')])
    setOps(o.items || [])
    setCurrent(o.currentOperatorId)
    setGrants(g.items || [])
    setComputers(c.items || [])
  }
  useEffect(() => { load().catch((e) => setFlash(e.message)) }, [])

  async function assume(id) {
    await api.assumeOperator(id)
    setFlash('Role assumed')
    load()
  }

  async function save(e) {
    e.preventDefault()
    await api.createOperator({ ...form, enabled: true })
    setModal(false)
    load()
  }

  async function grant(e) {
    e.preventDefault()
    await api.createLapsGrant({ ...jit, expiresAt: new Date(Date.now() + 2 * 3600000).toISOString() })
    setFlash('LAPS JIT grant issued (2h)')
    load()
  }

  return (
    <div>
      <PageHead title="Operators & LAPS JIT" subtitle="Helpdesk vs domain admin roles. LAPS passwords require a time-bound grant unless you are a domain admin.">
        <button className="btn primary" onClick={() => setModal(true)}>New operator</button>
      </PageHead>
      <Flash msg={flash} />
      <div className="two-col">
        <div className="card">
          <h3 className="section-title">Operators</h3>
          {ops.map((o) => (
            <div className="person-row" key={o.id}>
              <div className="avatar" style={{ width: 34, height: 34 }}>{o.name.slice(0, 2).toUpperCase()}</div>
              <div className="meta">
                <strong>{o.name} {current === o.id && <Badge kind="ok">active</Badge>}</strong>
                <span>{o.role} · {o.upn}</span>
              </div>
              <button className="btn" onClick={() => assume(o.id)}>Assume</button>
            </div>
          ))}
        </div>
        <div className="card">
          <h3 className="section-title">JIT LAPS access</h3>
          <form className="form-grid" onSubmit={grant}>
            <Field label="Operator">
              <select required value={jit.operatorId} onChange={(e) => setJit({ ...jit, operatorId: e.target.value })}>
                <option value="">Select</option>
                {ops.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
              </select>
            </Field>
            <Field label="Computer">
              <select required value={jit.computerId} onChange={(e) => setJit({ ...jit, computerId: e.target.value })}>
                <option value="">Select</option>
                {computers.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            </Field>
            <Field label="Reason" full><input value={jit.reason} onChange={(e) => setJit({ ...jit, reason: e.target.value })} /></Field>
            <div className="full"><button className="btn primary">Grant 2 hours</button></div>
          </form>
          <div className="table-wrap" style={{ marginTop: 12 }}>
            <table>
              <thead><tr><th>Operator</th><th>Computer</th><th>Expires</th><th></th></tr></thead>
              <tbody>
                {grants.map((g) => (
                  <tr key={g.id}>
                    <td>{g.operatorName}</td>
                    <td>{g.computerName}</td>
                    <td>{fmt(g.expiresAt)}</td>
                    <td><Badge kind={g.status === 'active' ? 'ok' : 'warn'}>{g.status}</Badge></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
      {modal && (
        <Modal title="New operator" onClose={() => setModal(false)}>
          <form onSubmit={save} className="form-grid">
            <Field label="Name"><input required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></Field>
            <Field label="UPN"><input value={form.upn} onChange={(e) => setForm({ ...form, upn: e.target.value })} /></Field>
            <Field label="Role" full>
              <select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })}>
                <option value="helpdesk">helpdesk</option>
                <option value="domainAdmin">domainAdmin</option>
                <option value="auditor">auditor</option>
              </select>
            </Field>
            <div className="full"><button className="btn primary">Save</button></div>
          </form>
        </Modal>
      )}
    </div>
  )
}
