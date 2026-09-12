import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, Field, Flash, Modal, PageHead } from '../ui.jsx'

function Tree({ nodes, parent, selected, onSelect, depth = 0 }) {
  const kids = nodes.filter((n) => n.parentId === parent)
  return kids.map((n) => (
    <div key={n.id}>
      <button className={`nav-link${selected === n.id ? ' active' : ''}`} style={{ width: '100%', border: 0, marginBottom: 4, paddingLeft: 12 + depth * 14 }} onClick={() => onSelect(n)}>
        {n.name}
        <span className="muted" style={{ marginLeft: 'auto', fontSize: 11 }}>{n.users + n.computers}</span>
      </button>
      <Tree nodes={nodes} parent={n.id} selected={selected} onSelect={onSelect} depth={depth + 1} />
    </div>
  ))
}

export default function OuBrowser() {
  const [ous, setOus] = useState([])
  const [sel, setSel] = useState(null)
  const [detail, setDetail] = useState(null)
  const [flash, setFlash] = useState('')
  const [modal, setModal] = useState(false)
  const [form, setForm] = useState({ name: '', parentId: '', description: '' })
  const [move, setMove] = useState({ objectType: 'user', objectId: '', ouId: '' })

  async function load() {
    const r = await api.ous()
    setOus(r.items || [])
  }

  useEffect(() => { load().catch((e) => setFlash(e.message)) }, [])

  async function open(n) {
    setSel(n.id)
    setDetail(await api.ou(n.id))
    setMove((m) => ({ ...m, ouId: n.id }))
  }

  async function create(e) {
    e.preventDefault()
    await api.createOu({ ...form, parentId: form.parentId || sel })
    setModal(false)
    setFlash('OU created')
    load()
  }

  async function doMove(e) {
    e.preventDefault()
    try {
      await api.moveObject(move)
      setFlash('Object moved')
      if (sel) setDetail(await api.ou(sel))
      load()
    } catch (err) { setFlash(err.message) }
  }

  const objects = [
    ...(detail?.users || []).map((u) => ({ id: u.id, name: u.displayName, type: 'user' })),
    ...(detail?.computers || []).map((c) => ({ id: c.id, name: c.name, type: 'computer' })),
    ...(detail?.groups || []).map((g) => ({ id: g.id, name: g.name, type: 'group' }))
  ]

  return (
    <div>
      <PageHead title="OU browser" subtitle="ADUC-style organizational unit tree. Select an OU, then move users, computers, or groups into it.">
        <button className="btn primary" onClick={() => { setForm({ name: '', parentId: sel || '', description: '' }); setModal(true) }}>New OU</button>
      </PageHead>
      <Flash msg={flash} kind={flash && !/created|moved/.test(flash) ? 'err' : 'ok'} />
      <div className="two-col">
        <div className="card">
          <h3 className="section-title">Directory tree</h3>
          <Tree nodes={ous} parent="" selected={sel} onSelect={open} />
        </div>
        <div className="card">
          <h3 className="section-title">{detail ? detail.name : 'Select an OU'}</h3>
          {detail && (
            <>
              <p className="muted">{detail.dn}</p>
              <form onSubmit={doMove} className="form-grid" style={{ marginTop: 12 }}>
                <Field label="Move object">
                  <select value={move.objectId} onChange={(e) => {
                    const o = objects.find((x) => x.id === e.target.value)
                    setMove((m) => ({ ...m, objectId: e.target.value, objectType: o?.type || m.objectType }))
                  }}>
                    <option value="">Choose object in this OU or another…</option>
                    {objects.map((o) => <option key={o.id} value={o.id}>{o.type}: {o.name}</option>)}
                  </select>
                </Field>
                <Field label="Target OU">
                  <select value={move.ouId} onChange={(e) => setMove((m) => ({ ...m, ouId: e.target.value }))}>
                    {ous.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
                  </select>
                </Field>
                <div className="full"><button className="btn primary" disabled={!move.objectId}>Move into OU</button></div>
              </form>
              <div className="table-wrap" style={{ marginTop: 14 }}>
                <table>
                  <thead><tr><th>Type</th><th>Name</th></tr></thead>
                  <tbody>
                    {objects.map((o) => <tr key={o.id}><td><Badge kind="info">{o.type}</Badge></td><td>{o.name}</td></tr>)}
                  </tbody>
                </table>
                {!objects.length && <div className="empty">Empty OU</div>}
              </div>
            </>
          )}
        </div>
      </div>
      {modal && (
        <Modal title="Create OU" onClose={() => setModal(false)}>
          <form onSubmit={create} className="form-grid">
            <Field label="Name"><input required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></Field>
            <Field label="Parent">
              <select value={form.parentId} onChange={(e) => setForm({ ...form, parentId: e.target.value })}>
                {ous.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
              </select>
            </Field>
            <Field label="Description" full><textarea value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></Field>
            <div className="full"><button className="btn primary">Create</button></div>
          </form>
        </Modal>
      )}
    </div>
  )
}
