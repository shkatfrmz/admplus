import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, Field, Flash, Modal, PageHead } from '../ui.jsx'

const empty = { name: '', precedence: 100, minLength: 14, historyCount: 24, maxAgeDays: 60, minAgeDays: 1, complexity: true, lockoutThreshold: 5, lockoutMinutes: 30 }

export default function PasswordPolicy() {
  const [items, setItems] = useState([])
  const [users, setUsers] = useState([])
  const [sim, setSim] = useState({ userId: '', password: '' })
  const [result, setResult] = useState(null)
  const [flash, setFlash] = useState('')
  const [modal, setModal] = useState(false)
  const [form, setForm] = useState(empty)

  async function load() {
    const [p, u] = await Promise.all([api.passwordPolicies(), api.users('?pageSize=200')])
    setItems(p.items || [])
    setUsers(u.items || [])
    if (!sim.userId && u.items?.[0]) setSim((s) => ({ ...s, userId: u.items[0].id }))
  }
  useEffect(() => { load().catch((e) => setFlash(e.message)) }, [])

  async function save(e) {
    e.preventDefault()
    await api.createPso(form)
    setModal(false)
    setFlash('PSO saved')
    load()
  }

  async function run(e) {
    e.preventDefault()
    setResult(await api.simulatePassword(sim))
  }

  return (
    <div>
      <PageHead title="Password policy" subtitle="Fine-grained PSOs plus a live complexity simulator against the effective policy for a user.">
        <button className="btn primary" onClick={() => { setForm(empty); setModal(true) }}>New PSO</button>
      </PageHead>
      <Flash msg={flash} />
      <div className="two-col">
        <div className="card">
          <h3 className="section-title">Policies</h3>
          <div className="table-wrap">
            <table>
              <thead><tr><th>Name</th><th>Prec.</th><th>Length</th><th>Age</th><th>Lockout</th></tr></thead>
              <tbody>
                {items.map((p) => (
                  <tr key={p.id}>
                    <td>{p.name} {p.isDomainDefault && <Badge kind="info">domain</Badge>}</td>
                    <td>{p.precedence}</td>
                    <td>{p.minLength}{p.complexity ? ' + complex' : ''}</td>
                    <td>{p.maxAgeDays}d</td>
                    <td>{p.lockoutThreshold}/{p.lockoutMinutes}m</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
        <div className="card">
          <h3 className="section-title">Simulator</h3>
          <form onSubmit={run} className="form-grid">
            <Field label="User" full>
              <select value={sim.userId} onChange={(e) => setSim({ ...sim, userId: e.target.value })}>
                {users.map((u) => <option key={u.id} value={u.id}>{u.displayName}</option>)}
              </select>
            </Field>
            <Field label="Candidate password" full>
              <input type="password" value={sim.password} onChange={(e) => setSim({ ...sim, password: e.target.value })} />
            </Field>
            <div className="full"><button className="btn primary">Test password</button></div>
          </form>
          {result && (
            <div style={{ marginTop: 12 }}>
              <p>Effective policy: <strong>{result.policy.name}</strong> (min {result.policy.minLength})</p>
              {result.passed ? <Badge kind="ok">Passes</Badge> : <Badge kind="danger">Fails</Badge>}
              <ul>{(result.issues || []).map((i) => <li key={i}>{i}</li>)}</ul>
            </div>
          )}
        </div>
      </div>
      {modal && (
        <Modal title="Create PSO" onClose={() => setModal(false)}>
          <form onSubmit={save} className="form-grid">
            <Field label="Name"><input required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></Field>
            <Field label="Precedence"><input type="number" value={form.precedence} onChange={(e) => setForm({ ...form, precedence: Number(e.target.value) })} /></Field>
            <Field label="Min length"><input type="number" value={form.minLength} onChange={(e) => setForm({ ...form, minLength: Number(e.target.value) })} /></Field>
            <Field label="Max age (days)"><input type="number" value={form.maxAgeDays} onChange={(e) => setForm({ ...form, maxAgeDays: Number(e.target.value) })} /></Field>
            <label className="check"><input type="checkbox" checked={form.complexity} onChange={(e) => setForm({ ...form, complexity: e.target.checked })} /> Complexity</label>
            <div className="full"><button className="btn primary">Save</button></div>
          </form>
        </Modal>
      )}
    </div>
  )
}
