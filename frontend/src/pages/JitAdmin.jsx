import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, Field, Flash, PageHead, fmt } from '../ui.jsx'

export default function JitAdmin() {
  const [items, setItems] = useState([])
  const [users, setUsers] = useState([])
  const [groups, setGroups] = useState([])
  const [form, setForm] = useState({ userId: '', groupId: '', reason: '', ticket: '' })
  const [flash, setFlash] = useState('')

  async function load() {
    const [j, u, g] = await Promise.all([api.jit(), api.users('?pageSize=200'), api.groups('?pageSize=200')])
    setItems(j.items || [])
    setUsers(u.items || [])
    setGroups(g.items || [])
  }
  useEffect(() => { load().catch((e) => setFlash(e.message)) }, [])

  async function grant(e) {
    e.preventDefault()
    try {
      await api.createJit({ ...form, expiresAt: new Date(Date.now() + 4 * 3600000).toISOString() })
      setFlash('JIT grant active for 4 hours')
      load()
    } catch (err) { setFlash(err.message) }
  }

  return (
    <div>
      <PageHead title="JIT privileged access" subtitle="Time-bound Domain Admin / privileged group membership. Grants expire automatically." />
      <Flash msg={flash} kind={flash && !flash.includes('JIT') ? 'err' : 'ok'} />
      <div className="card" style={{ marginBottom: 14 }}>
        <h3 className="section-title">New grant</h3>
        <form className="form-grid" onSubmit={grant}>
          <Field label="User">
            <select required value={form.userId} onChange={(e) => setForm({ ...form, userId: e.target.value })}>
              <option value="">Select user</option>
              {users.map((u) => <option key={u.id} value={u.id}>{u.displayName}</option>)}
            </select>
          </Field>
          <Field label="Privileged group">
            <select required value={form.groupId} onChange={(e) => setForm({ ...form, groupId: e.target.value })}>
              <option value="">Select group</option>
              {groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
            </select>
          </Field>
          <Field label="Reason"><input value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} /></Field>
          <Field label="Ticket"><input value={form.ticket} onChange={(e) => setForm({ ...form, ticket: e.target.value })} /></Field>
          <div className="full"><button className="btn primary">Grant 4 hours</button></div>
        </form>
      </div>
      <div className="card">
        <div className="table-wrap">
          <table>
            <thead><tr><th>User</th><th>Group</th><th>Reason</th><th>Expires</th><th>Status</th><th></th></tr></thead>
            <tbody>
              {items.map((j) => (
                <tr key={j.id}>
                  <td>{j.userName}</td>
                  <td>{j.groupName}</td>
                  <td>{j.reason} {j.ticket && <Badge>{j.ticket}</Badge>}</td>
                  <td>{fmt(j.expiresAt)}</td>
                  <td><Badge kind={j.status === 'active' ? 'ok' : 'warn'}>{j.status}</Badge></td>
                  <td>{j.status === 'active' && <button className="btn danger" onClick={() => api.revokeJit(j.id).then(load)}>Revoke</button>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
