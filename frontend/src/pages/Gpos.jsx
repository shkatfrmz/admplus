import { useState } from 'react'
import { api } from '../api.js'
import { Badge, Field, Flash, Modal, PageHead, Pager, fmt, useQueryList } from '../ui.jsx'

const empty = { name: '', status: 'enabled', linkedOus: '', enforced: false, description: '', settings: '{}' }

export default function Gpos() {
  const list = useQueryList(api.gpos)
  const [modal, setModal] = useState(null)
  const [form, setForm] = useState(empty)
  const [flash, setFlash] = useState('')
  const [detail, setDetail] = useState(null)
  const [rsop, setRsop] = useState(null)
  const [backups, setBackups] = useState([])
  function set(k, v) { setForm((f) => ({ ...f, [k]: v })) }

  async function openDetail(g) {
    const [full, bak] = await Promise.all([api.gpo(g.id), api.gpoBackups(g.id)])
    setDetail(full)
    setBackups(bak.items || [])
    setRsop(null)
  }

  async function runRsop() {
    try {
      setRsop(await api.gpoRsop(detail.id))
      setFlash('RSOP calculated')
    } catch (err) { setFlash(err.message) }
  }

  async function runBackup() {
    try {
      await api.backupGpo(detail.id)
      setBackups((await api.gpoBackups(detail.id)).items || [])
      setFlash('GPO backup created')
    } catch (err) { setFlash(err.message) }
  }

  async function runRestore(backupId) {
    try {
      const restored = await api.restoreGpo(detail.id, backupId)
      setDetail(restored)
      list.reload()
      setFlash('GPO restored from backup')
    } catch (err) { setFlash(err.message) }
  }

  function toForm(g) {
    return {
      ...g,
      linkedOus: (g.linkedOus || []).join('\n'),
      settings: JSON.stringify(g.settings || {}, null, 2)
    }
  }

  async function save(e) {
    e.preventDefault()
    try {
      let settings = {}
      try { settings = JSON.parse(form.settings || '{}') } catch { throw new Error('Settings must be valid JSON') }
      const body = {
        ...form,
        linkedOus: String(form.linkedOus || '').split('\n').map((s) => s.trim()).filter(Boolean),
        settings
      }
      if (form.id) await api.updateGpo(form.id, body)
      else await api.createGpo(body)
      setModal(null)
      setFlash('GPO saved')
      list.reload()
    } catch (err) { setFlash(err.message) }
  }

  return (
    <div>
      <PageHead title="Group Policy" subtitle="Create, link, and manage GPOs across organizational units.">
        <input className="search" placeholder="Search GPOs" value={list.q} onChange={(e) => list.setQ(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && list.reload()} />
        <button className="btn" onClick={list.reload}>Refresh</button>
        <button className="btn primary" onClick={() => { setForm(empty); setModal(true) }}>New GPO</button>
      </PageHead>
      <Flash msg={flash} kind={flash && !/saved|RSOP|backup|restored/.test(flash) ? 'err' : 'ok'} />
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Status</th>
              <th>Enforced</th>
              <th>Linked OUs</th>
              <th>Modified</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {list.data.items.map((g) => (
              <tr key={g.id} className="row-link" onClick={() => openDetail(g)}>
                <td>{g.name}</td>
                <td>{g.status === 'enabled' ? <Badge kind="ok">enabled</Badge> : <Badge kind="warn">{g.status}</Badge>}</td>
                <td>{g.enforced ? <Badge kind="info">enforced</Badge> : '—'}</td>
                <td>{(g.linkedOus || []).length}</td>
                <td>{fmt(g.modified)}</td>
                <td><button className="btn" onClick={(e) => { e.stopPropagation(); setForm(toForm(g)); setModal(true) }}>Edit</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Pager list={list} />

      {detail && (
        <Modal title={detail.name} onClose={() => setDetail(null)}>
          <p>{detail.description}</p>
          <dl className="kv">
            <dt>Status</dt><dd>{detail.status}</dd>
            <dt>Enforced</dt><dd>{detail.enforced ? 'yes' : 'no'}</dd>
            <dt>Links</dt><dd>{(detail.linkedOus || []).join(' | ') || 'None'}</dd>
            <dt>Created</dt><dd>{fmt(detail.created)}</dd>
          </dl>
          <h4>Settings</h4>
          <pre className="password-box">{JSON.stringify(detail.settings, null, 2)}</pre>
          <div className="actions" style={{ marginTop: 12 }}>
            <button className="btn" onClick={runRsop}>Run RSOP</button>
            <button className="btn" onClick={runBackup}>Backup</button>
            <button className="btn danger" onClick={() => { if (confirm('Delete GPO?')) api.deleteGpo(detail.id).then(() => { setDetail(null); list.reload() }) }}>Delete</button>
          </div>
          {rsop && (
            <div style={{ marginTop: 16 }}>
              <h4>Resulting set of policy</h4>
              <pre className="password-box">{JSON.stringify(rsop.resultingSet, null, 2)}</pre>
              <div className="table-wrap" style={{ marginTop: 8 }}>
                <table>
                  <thead><tr><th>GPO</th><th>Setting</th><th>Value</th><th>Enforced</th><th>Overwritten</th></tr></thead>
                  <tbody>
                    {(rsop.trace || []).map((t, i) => (
                      <tr key={i}>
                        <td>{t.gpo}</td>
                        <td>{t.setting}</td>
                        <td>{typeof t.value === 'object' ? JSON.stringify(t.value) : String(t.value)}</td>
                        <td>{t.enforced ? 'yes' : 'no'}</td>
                        <td>{t.overwritten ? 'yes' : 'no'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
          <div style={{ marginTop: 16 }}>
            <h4>Backups</h4>
            {backups.length === 0 ? <p className="muted">No backups yet.</p> : (
              <div className="table-wrap">
                <table>
                  <thead><tr><th>Created</th><th>Id</th><th></th></tr></thead>
                  <tbody>
                    {backups.map((b) => (
                      <tr key={b.id}>
                        <td>{fmt(b.created)}</td>
                        <td className="muted">{b.id}</td>
                        <td><button className="btn" onClick={() => runRestore(b.id)}>Restore</button></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </Modal>
      )}

      {modal && (
        <Modal title={form.id ? 'Edit GPO' : 'Create GPO'} onClose={() => setModal(null)}>
          <form onSubmit={save} className="form-grid">
            <Field label="Name"><input required value={form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Status">
              <select value={form.status} onChange={(e) => set('status', e.target.value)}>
                <option>enabled</option>
                <option>disabled</option>
                <option value="userDisabled">userDisabled</option>
                <option value="computerDisabled">computerDisabled</option>
              </select>
            </Field>
            <Field label="Linked OUs (one per line)" full><textarea value={form.linkedOus} onChange={(e) => set('linkedOus', e.target.value)} /></Field>
            <Field label="Description" full><textarea value={form.description || ''} onChange={(e) => set('description', e.target.value)} /></Field>
            <Field label="Settings JSON" full><textarea value={form.settings} onChange={(e) => set('settings', e.target.value)} /></Field>
            <label className="check"><input type="checkbox" checked={Boolean(form.enforced)} onChange={(e) => set('enforced', e.target.checked)} /> Enforced</label>
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
