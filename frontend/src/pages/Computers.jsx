import { useState } from 'react'
import { api } from '../api.js'
import { Badge, Field, Flash, Modal, PageHead, fmt, useQueryList } from '../ui.jsx'

const TYPES = ['workstation', 'laptop', 'server', 'domainController', 'virtual']

const empty = {
  name: '', dnsHostName: '', os: 'Windows 11 Enterprise', osVersion: '', type: 'workstation',
  ou: 'OU=Computers,DC=contoso,DC=local', description: '', ipAddress: '', enabled: true
}

export default function Computers() {
  const list = useQueryList(api.computers)
  const [modal, setModal] = useState(null)
  const [form, setForm] = useState(empty)
  const [flash, setFlash] = useState('')
  const [detail, setDetail] = useState(null)
  function set(k, v) { setForm((f) => ({ ...f, [k]: v })) }

  async function save(e) {
    e.preventDefault()
    try {
      if (form.id) await api.updateComputer(form.id, form)
      else await api.createComputer(form)
      setModal(null)
      setFlash('Computer saved')
      list.reload()
    } catch (err) { setFlash(err.message) }
  }

  async function act(fn, id) {
    try {
      await fn(id)
      setFlash('Updated')
      list.reload()
    } catch (err) { setFlash(err.message) }
  }

  return (
    <div>
      <PageHead title="Computers" subtitle="Workstations, laptops, servers, and domain controllers.">
        <input className="search" placeholder="Search computers" value={list.q} onChange={(e) => list.setQ(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && list.reload()} />
        <select value={list.type} onChange={(e) => { list.setType(e.target.value) }}>
          <option value="">All types</option>
          {TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
        </select>
        <button className="btn" onClick={list.reload}>Refresh</button>
        <button className="btn primary" onClick={() => { setForm(empty); setModal(true) }}>New computer</button>
      </PageHead>
      <Flash msg={flash} />
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>DNS</th>
              <th>Type</th>
              <th>OS</th>
              <th>IP</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {list.data.items.map((c) => (
              <tr key={c.id} className="row-link" onClick={() => api.computer(c.id).then(setDetail)}>
                <td>{c.name}</td>
                <td>{c.dnsHostName}</td>
                <td><Badge kind="info">{c.type}</Badge></td>
                <td>{c.os}</td>
                <td>{c.ipAddress || '—'}</td>
                <td>{c.enabled ? <Badge kind="ok">enabled</Badge> : <Badge kind="danger">disabled</Badge>}</td>
                <td><button className="btn" onClick={(e) => { e.stopPropagation(); setForm(c); setModal(true) }}>Edit</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {detail && (
        <Modal title={detail.name} onClose={() => setDetail(null)}>
          <dl className="kv">
            <dt>DNS</dt><dd>{detail.dnsHostName}</dd>
            <dt>OS</dt><dd>{detail.os} {detail.osVersion}</dd>
            <dt>OU</dt><dd>{detail.ou}</dd>
            <dt>Description</dt><dd>{detail.description || '—'}</dd>
            <dt>Last logon</dt><dd>{fmt(detail.lastLogon)}</dd>
            <dt>LAPS</dt><dd>{detail.laps ? `Expires ${fmt(detail.laps.expiration)}` : 'Not enrolled'}</dd>
          </dl>
          <div className="actions" style={{ marginTop: 14 }}>
            {detail.enabled
              ? <button className="btn" onClick={() => act(api.disableComputer, detail.id)}>Disable</button>
              : <button className="btn" onClick={() => act(api.enableComputer, detail.id)}>Enable</button>}
            <button className="btn danger" onClick={() => { if (confirm('Delete this computer?')) act(api.deleteComputer, detail.id).then(() => setDetail(null)) }}>Delete</button>
          </div>
        </Modal>
      )}

      {modal && (
        <Modal title={form.id ? 'Edit computer' : 'Create computer'} onClose={() => setModal(null)}>
          <form onSubmit={save} className="form-grid">
            <Field label="Name"><input required value={form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="DNS host name"><input value={form.dnsHostName || ''} onChange={(e) => set('dnsHostName', e.target.value)} /></Field>
            <Field label="Type">
              <select value={form.type} onChange={(e) => set('type', e.target.value)}>{TYPES.map((t) => <option key={t}>{t}</option>)}</select>
            </Field>
            <Field label="OS"><input value={form.os || ''} onChange={(e) => set('os', e.target.value)} /></Field>
            <Field label="IP address"><input value={form.ipAddress || ''} onChange={(e) => set('ipAddress', e.target.value)} /></Field>
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
