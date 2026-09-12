import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api.js'
import { Badge, Field, Flash, Modal, PageHead, fmt, useQueryList } from '../ui.jsx'

const USER_TYPES = ['user', 'service', 'guest', 'inetOrgPerson', 'contact', 'managedService']

const empty = {
  samAccountName: '',
  displayName: '',
  givenName: '',
  surname: '',
  email: '',
  type: 'user',
  department: '',
  title: '',
  office: '',
  phone: '',
  ou: 'CN=Users,DC=contoso,DC=local',
  enabled: true,
  passwordNeverExpires: false,
  mustChangePassword: true
}

export default function Users() {
  const [params] = useSearchParams()
  const list = useQueryList(api.users)
  const [modal, setModal] = useState(null)
  const [form, setForm] = useState(empty)
  const [groups, setGroups] = useState([])
  const [flash, setFlash] = useState('')
  const [detail, setDetail] = useState(null)

  useEffect(() => {
    api.groups('?pageSize=200').then((r) => setGroups(r.items)).catch(() => {})
  }, [])

  useEffect(() => {
    const q = params.get('q')
    if (q) {
      list.setQ(q)
      setTimeout(list.reload, 0)
    }
  }, [params])

  function set(k, v) { setForm((f) => ({ ...f, [k]: v })) }

  async function save(e) {
    e.preventDefault()
    try {
      if (form.id) await api.updateUser(form.id, form)
      else await api.createUser(form)
      setModal(null)
      setFlash('User saved')
      list.reload()
    } catch (err) {
      setFlash(err.message)
    }
  }

  async function act(fn, id) {
    try {
      await fn(id)
      setFlash('Updated')
      list.reload()
      if (detail?.id === id) setDetail(await api.user(id))
    } catch (err) { setFlash(err.message) }
  }

  return (
    <div>
      <PageHead title="Users" subtitle="Create and manage user, service, guest, inetOrgPerson, and contact accounts.">
        <input className="search" placeholder="Search users" value={list.q} onChange={(e) => list.setQ(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && list.reload()} />
        <select value={list.type} onChange={(e) => { list.setType(e.target.value); setTimeout(list.reload, 0) }}>
          <option value="">All types</option>
          {USER_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
        </select>
        <button className="btn" onClick={list.reload}>Refresh</button>
        <button className="btn primary" onClick={() => { setForm(empty); setModal('create') }}>New user</button>
      </PageHead>
      <Flash msg={flash} kind={flash && flash !== 'User saved' && flash !== 'Updated' ? 'err' : 'ok'} />

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>sAMAccountName</th>
              <th>Type</th>
              <th>Department</th>
              <th>Status</th>
              <th>UPN</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {list.data.items.map((u) => (
              <tr key={u.id} className="row-link" onClick={() => api.user(u.id).then(setDetail)}>
                <td>{u.displayName}</td>
                <td>{u.samAccountName}</td>
                <td><Badge kind="info">{u.type}</Badge></td>
                <td>{u.department || '—'}</td>
                <td>
                  {u.enabled ? <Badge kind="ok">enabled</Badge> : <Badge kind="danger">disabled</Badge>}
                  {u.locked ? <Badge kind="warn">locked</Badge> : null}
                </td>
                <td>{u.userPrincipalName}</td>
                <td>
                  <button className="btn" onClick={(e) => { e.stopPropagation(); setForm(u); setModal('edit') }}>Edit</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {!list.data.items.length && <div className="empty">No users match the current filter.</div>}
      </div>
      <p className="muted">{list.data.total} accounts</p>

      {detail && (
        <Modal title={detail.displayName} onClose={() => setDetail(null)}>
          <dl className="kv">
            <dt>sAMAccountName</dt><dd>{detail.samAccountName}</dd>
            <dt>UPN</dt><dd>{detail.userPrincipalName}</dd>
            <dt>Type</dt><dd>{detail.type}</dd>
            <dt>OU</dt><dd>{detail.ou}</dd>
            <dt>Email</dt><dd>{detail.email || '—'}</dd>
            <dt>Title</dt><dd>{detail.title || '—'}</dd>
            <dt>Office / phone</dt><dd>{detail.office || '—'} / {detail.phone || '—'}</dd>
            <dt>Created</dt><dd>{fmt(detail.created)}</dd>
            <dt>Last logon</dt><dd>{fmt(detail.lastLogon)}</dd>
            <dt>Groups</dt>
            <dd className="pill-row">{(detail.groupDetails || []).map((g) => <Badge key={g.id}>{g.name}</Badge>)}</dd>
          </dl>
          <div className="actions" style={{ marginTop: 14 }}>
            {detail.enabled
              ? <button className="btn" onClick={() => act(api.disableUser, detail.id)}>Disable</button>
              : <button className="btn" onClick={() => act(api.enableUser, detail.id)}>Enable</button>}
            <button className="btn" onClick={() => act(api.unlockUser, detail.id)}>Unlock</button>
            <button className="btn" onClick={() => act(api.resetPassword, detail.id)}>Reset password</button>
            <button className="btn danger" onClick={() => { if (confirm('Delete this user?')) act(api.deleteUser, detail.id).then(() => setDetail(null)) }}>Delete</button>
          </div>
        </Modal>
      )}

      {modal && (
        <Modal title={form.id ? 'Edit user' : 'Create user'} onClose={() => setModal(null)}>
          <form onSubmit={save} className="form-grid">
            <Field label="sAMAccountName"><input required disabled={Boolean(form.id)} value={form.samAccountName} onChange={(e) => set('samAccountName', e.target.value)} /></Field>
            <Field label="Display name"><input required value={form.displayName} onChange={(e) => set('displayName', e.target.value)} /></Field>
            <Field label="Given name"><input value={form.givenName || ''} onChange={(e) => set('givenName', e.target.value)} /></Field>
            <Field label="Surname"><input value={form.surname || ''} onChange={(e) => set('surname', e.target.value)} /></Field>
            <Field label="Email"><input value={form.email || ''} onChange={(e) => set('email', e.target.value)} /></Field>
            <Field label="Type">
              <select value={form.type} onChange={(e) => set('type', e.target.value)}>
                {USER_TYPES.map((t) => <option key={t}>{t}</option>)}
              </select>
            </Field>
            <Field label="Department"><input value={form.department || ''} onChange={(e) => set('department', e.target.value)} /></Field>
            <Field label="Title"><input value={form.title || ''} onChange={(e) => set('title', e.target.value)} /></Field>
            <Field label="Office"><input value={form.office || ''} onChange={(e) => set('office', e.target.value)} /></Field>
            <Field label="Phone"><input value={form.phone || ''} onChange={(e) => set('phone', e.target.value)} /></Field>
            <Field label="OU" full><input value={form.ou || ''} onChange={(e) => set('ou', e.target.value)} /></Field>
            <Field label="Groups" full>
              <select multiple value={form.groups || []} onChange={(e) => set('groups', Array.from(e.target.selectedOptions).map((o) => o.value))} style={{ minHeight: 90 }}>
                {groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
              </select>
            </Field>
            <label className="check"><input type="checkbox" checked={form.enabled !== false} onChange={(e) => set('enabled', e.target.checked)} /> Enabled</label>
            <label className="check"><input type="checkbox" checked={Boolean(form.passwordNeverExpires)} onChange={(e) => set('passwordNeverExpires', e.target.checked)} /> Password never expires</label>
            <div className="full actions">
              <button className="btn primary" type="submit">Save</button>
              <button className="btn" type="button" onClick={() => setModal(null)}>Cancel</button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}
