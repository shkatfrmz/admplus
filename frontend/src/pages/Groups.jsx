import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, Field, Flash, Modal, PageHead, useQueryList } from '../ui.jsx'

const empty = { name: '', samAccountName: '', type: 'security', scope: 'global', description: '', mail: '', ou: 'OU=Groups,DC=contoso,DC=local', members: [] }

export default function Groups() {
  const list = useQueryList(api.groups)
  const [modal, setModal] = useState(null)
  const [form, setForm] = useState(empty)
  const [flash, setFlash] = useState('')
  const [detail, setDetail] = useState(null)
  const [users, setUsers] = useState([])
  const [memberId, setMemberId] = useState('')
  function set(k, v) { setForm((f) => ({ ...f, [k]: v })) }

  useEffect(() => {
    api.users('?pageSize=200').then((r) => setUsers(r.items)).catch(() => {})
  }, [])

  async function save(e) {
    e.preventDefault()
    try {
      if (form.id) await api.updateGroup(form.id, form)
      else await api.createGroup(form)
      setModal(null)
      setFlash('Group saved')
      list.reload()
    } catch (err) { setFlash(err.message) }
  }

  async function open(id) {
    setDetail(await api.group(id))
  }

  async function addMember() {
    if (!memberId || !detail) return
    await api.addMember(detail.id, memberId)
    setDetail(await api.group(detail.id))
    list.reload()
  }

  async function removeMember(mid) {
    await api.removeMember(detail.id, mid)
    setDetail(await api.group(detail.id))
    list.reload()
  }

  return (
    <div>
      <PageHead title="Groups" subtitle="Security and distribution groups — global, universal, and domain local.">
        <input className="search" placeholder="Search groups" value={list.q} onChange={(e) => list.setQ(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && list.reload()} />
        <select value={list.type} onChange={(e) => { list.setType(e.target.value) }}>
          <option value="">All types</option>
          <option value="security">security</option>
          <option value="distribution">distribution</option>
        </select>
        <button className="btn" onClick={list.reload}>Refresh</button>
        <button className="btn primary" onClick={() => { setForm(empty); setModal(true) }}>New group</button>
      </PageHead>
      <Flash msg={flash} />
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Type</th>
              <th>Scope</th>
              <th>Members</th>
              <th>Mail</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {list.data.items.map((g) => (
              <tr key={g.id} className="row-link" onClick={() => open(g.id)}>
                <td>{g.name}</td>
                <td><Badge kind="info">{g.type}</Badge></td>
                <td>{g.scope}</td>
                <td>{g.members?.length || 0}</td>
                <td>{g.mail || '—'}</td>
                <td><button className="btn" onClick={(e) => { e.stopPropagation(); setForm(g); setModal(true) }}>Edit</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {detail && (
        <Modal title={detail.name} onClose={() => setDetail(null)}>
          <p className="muted">{detail.description}</p>
          <dl className="kv">
            <dt>sAMAccountName</dt><dd>{detail.samAccountName}</dd>
            <dt>Type / scope</dt><dd>{detail.type} / {detail.scope}</dd>
            <dt>OU</dt><dd>{detail.ou}</dd>
          </dl>
          <h4>Members</h4>
          <div className="table-wrap">
            <table>
              <thead><tr><th>Name</th><th>Kind</th><th></th></tr></thead>
              <tbody>
                {(detail.memberDetails || []).map((m) => (
                  <tr key={m.id}>
                    <td>{m.name}</td>
                    <td>{m.kind}</td>
                    <td><button className="btn" onClick={() => removeMember(m.id)}>Remove</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="toolbar" style={{ marginTop: 12 }}>
            <select value={memberId} onChange={(e) => setMemberId(e.target.value)}>
              <option value="">Add member...</option>
              {users.map((u) => <option key={u.id} value={u.id}>{u.displayName} ({u.samAccountName})</option>)}
            </select>
            <button className="btn primary" onClick={addMember}>Add</button>
            <button className="btn danger" onClick={() => { if (confirm('Delete group?')) api.deleteGroup(detail.id).then(() => { setDetail(null); list.reload() }) }}>Delete group</button>
          </div>
        </Modal>
      )}

      {modal && (
        <Modal title={form.id ? 'Edit group' : 'Create group'} onClose={() => setModal(null)}>
          <form onSubmit={save} className="form-grid">
            <Field label="Name"><input required value={form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="sAMAccountName"><input value={form.samAccountName || ''} onChange={(e) => set('samAccountName', e.target.value)} /></Field>
            <Field label="Type">
              <select value={form.type} onChange={(e) => set('type', e.target.value)}>
                <option>security</option>
                <option>distribution</option>
              </select>
            </Field>
            <Field label="Scope">
              <select value={form.scope} onChange={(e) => set('scope', e.target.value)}>
                <option value="global">global</option>
                <option value="universal">universal</option>
                <option value="domainLocal">domainLocal</option>
              </select>
            </Field>
            <Field label="Mail"><input value={form.mail || ''} onChange={(e) => set('mail', e.target.value)} /></Field>
            <Field label="OU"><input value={form.ou || ''} onChange={(e) => set('ou', e.target.value)} /></Field>
            <Field label="Description" full><textarea value={form.description || ''} onChange={(e) => set('description', e.target.value)} /></Field>
            <div className="full actions">
              <button className="btn primary">Save</button>
              <button className="btn" type="button" onClick={() => setModal(null)}>Cancel</button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}
