import { useState } from 'react'
import { api } from '../api.js'
import { Badge, Field, Flash, Modal, PageHead, useQueryList } from '../ui.jsx'

const empty = { name: '', path: '', server: 'FS01', description: '', hidden: false, permissionsText: 'Domain Users:read' }

export default function Shares() {
  const list = useQueryList(api.shares)
  const [modal, setModal] = useState(null)
  const [form, setForm] = useState(empty)
  const [flash, setFlash] = useState('')
  function set(k, v) { setForm((f) => ({ ...f, [k]: v })) }

  function parsePerms(text) {
    return String(text || '').split('\n').map((l) => l.trim()).filter(Boolean).map((l) => {
      const [principal, access] = l.split(':').map((s) => s.trim())
      return { principal, access: access || 'read' }
    })
  }

  async function save(e) {
    e.preventDefault()
    try {
      const body = { ...form, permissions: parsePerms(form.permissionsText) }
      if (form.id) await api.updateShare(form.id, body)
      else await api.createShare(body)
      setModal(null)
      setFlash('Share saved')
      list.reload()
    } catch (err) { setFlash(err.message) }
  }

  function openEdit(s) {
    setForm({
      ...s,
      permissionsText: (s.permissions || []).map((p) => `${p.principal}:${p.access}`).join('\n')
    })
    setModal(true)
  }

  return (
    <div>
      <PageHead title="Share folders" subtitle="SMB share inventory, hidden shares, and access control lists.">
        <input className="search" placeholder="Search shares" value={list.q} onChange={(e) => list.setQ(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && list.reload()} />
        <button className="btn" onClick={list.reload}>Refresh</button>
        <button className="btn primary" onClick={() => { setForm(empty); setModal(true) }}>New share</button>
      </PageHead>
      <Flash msg={flash} />
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Path</th>
              <th>Server</th>
              <th>Hidden</th>
              <th>Permissions</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {list.data.items.map((s) => (
              <tr key={s.id}>
                <td>{s.name}</td>
                <td>{s.path}</td>
                <td>{s.server}</td>
                <td>{s.hidden ? <Badge kind="warn">hidden</Badge> : <Badge kind="ok">visible</Badge>}</td>
                <td>{(s.permissions || []).map((p) => `${p.principal}:${p.access}`).join(', ')}</td>
                <td className="actions">
                  <button className="btn" onClick={() => openEdit(s)}>Edit</button>
                  <button className="btn danger" onClick={() => { if (confirm('Delete share?')) api.deleteShare(s.id).then(list.reload) }}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {modal && (
        <Modal title={form.id ? 'Edit share' : 'Create share'} onClose={() => setModal(null)}>
          <form onSubmit={save} className="form-grid">
            <Field label="Name"><input required value={form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Server"><input value={form.server || ''} onChange={(e) => set('server', e.target.value)} /></Field>
            <Field label="Path" full><input required value={form.path} onChange={(e) => set('path', e.target.value)} /></Field>
            <Field label="Description" full><textarea value={form.description || ''} onChange={(e) => set('description', e.target.value)} /></Field>
            <Field label="Permissions (principal:access per line)" full>
              <textarea value={form.permissionsText || ''} onChange={(e) => set('permissionsText', e.target.value)} />
            </Field>
            <label className="check"><input type="checkbox" checked={Boolean(form.hidden)} onChange={(e) => set('hidden', e.target.checked)} /> Hidden share</label>
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
