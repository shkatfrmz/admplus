import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, Field, Flash, PageHead, fmt } from '../ui.jsx'

const emptyDc = { host: '', port: 389, useSsl: false, bindDn: '', password: '', baseDn: '', domain: '' }
const emptyAz = { tenantId: '', clientId: '', clientSecret: '' }

export default function Settings() {
  const [dc, setDc] = useState(emptyDc)
  const [az, setAz] = useState(emptyAz)
  const [flash, setFlash] = useState('')
  const [kind, setKind] = useState('ok')
  const [busy, setBusy] = useState('')

  useEffect(() => {
    api.settings().then((s) => {
      setDc({ ...emptyDc, ...s.domainController })
      setAz({ ...emptyAz, ...s.azureAd })
    }).catch((e) => { setFlash(e.message); setKind('err') })
  }, [])

  function msg(text, k = 'ok') { setFlash(text); setKind(k) }

  async function saveDc(e) {
    e.preventDefault()
    try {
      const saved = await api.saveDc(dc)
      setDc((d) => ({ ...d, ...saved }))
      msg('Domain controller settings saved')
    } catch (err) { msg(err.message, 'err') }
  }

  async function saveAz(e) {
    e.preventDefault()
    try {
      const saved = await api.saveAzure(az)
      setAz((a) => ({ ...a, ...saved }))
      msg('Azure AD settings saved')
    } catch (err) { msg(err.message, 'err') }
  }

  async function testDc() {
    setBusy('dc')
    try {
      const r = await api.testDc()
      setDc((d) => ({ ...d, connected: true, lastTest: r.lastTest, lastError: null }))
      msg(r.message)
    } catch (err) {
      setDc((d) => ({ ...d, connected: false, lastError: err.message }))
      msg(err.message, 'err')
    } finally { setBusy('') }
  }

  async function testAz() {
    setBusy('az')
    try {
      const r = await api.testAzure()
      setAz((a) => ({ ...a, connected: true, lastTest: r.lastTest, lastError: null }))
      msg(r.message)
    } catch (err) {
      setAz((a) => ({ ...a, connected: false, lastError: err.message }))
      msg(err.message, 'err')
    } finally { setBusy('') }
  }

  async function sync() {
    setBusy('sync')
    try {
      const r = await api.sync()
      msg(`Synced ${r.count} directory users (${r.created} created, ${r.updated} updated)`)
    } catch (err) { msg(err.message, 'err') }
    finally { setBusy('') }
  }

  return (
    <div>
      <PageHead title="Settings" subtitle="Connect via System.DirectoryServices.Protocols (LDAP/LDAPS) and Microsoft.Identity.Client (Azure AD / Entra ID)." />
      <Flash msg={flash} kind={kind} />

      <div className="two-col">
        <form className="card" onSubmit={saveDc}>
          <h3 className="section-title">Domain controller</h3>
          <p>
            {dc.connected ? <Badge kind="ok">Connected</Badge> : <Badge kind="warn">Disconnected</Badge>}
            <span className="muted"> Last test {fmt(dc.lastTest)}</span>
          </p>
          {dc.lastError ? <p className="flash err">{dc.lastError}</p> : null}
          <div className="form-grid">
            <Field label="Host / FQDN"><input value={dc.host} onChange={(e) => setDc({ ...dc, host: e.target.value })} placeholder="dc01.contoso.local" /></Field>
            <Field label="Port"><input type="number" value={dc.port} onChange={(e) => setDc({ ...dc, port: Number(e.target.value) })} /></Field>
            <Field label="Domain"><input value={dc.domain} onChange={(e) => setDc({ ...dc, domain: e.target.value })} placeholder="contoso.local" /></Field>
            <Field label="Base DN"><input value={dc.baseDn} onChange={(e) => setDc({ ...dc, baseDn: e.target.value })} placeholder="DC=contoso,DC=local" /></Field>
            <Field label="Bind DN" full><input value={dc.bindDn} onChange={(e) => setDc({ ...dc, bindDn: e.target.value })} placeholder="CN=Administrator,CN=Users,DC=contoso,DC=local" /></Field>
            <Field label="Password" full><input type="password" value={dc.password} onChange={(e) => setDc({ ...dc, password: e.target.value })} placeholder="Bind password" /></Field>
            <label className="check"><input type="checkbox" checked={Boolean(dc.useSsl)} onChange={(e) => setDc({ ...dc, useSsl: e.target.checked, port: e.target.checked ? 636 : 389 })} /> Use LDAPS</label>
          </div>
          <div className="actions" style={{ marginTop: 14 }}>
            <button className="btn primary" type="submit">Save</button>
            <button className="btn" type="button" disabled={busy === 'dc'} onClick={testDc}>{busy === 'dc' ? 'Testing...' : 'Test connection'}</button>
            <button className="btn" type="button" disabled={busy === 'sync'} onClick={sync}>{busy === 'sync' ? 'Syncing...' : 'Sync users from DC'}</button>
          </div>
        </form>

        <form className="card" onSubmit={saveAz}>
          <h3 className="section-title">Azure AD / Entra ID</h3>
          <p>
            {az.connected ? <Badge kind="ok">Connected</Badge> : <Badge kind="warn">Disconnected</Badge>}
            <span className="muted"> Last test {fmt(az.lastTest)}</span>
          </p>
          {az.lastError ? <p className="flash err">{az.lastError}</p> : null}
          <div className="form-grid">
            <Field label="Tenant ID" full><input value={az.tenantId} onChange={(e) => setAz({ ...az, tenantId: e.target.value })} placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" /></Field>
            <Field label="Application (client) ID" full><input value={az.clientId} onChange={(e) => setAz({ ...az, clientId: e.target.value })} /></Field>
            <Field label="Client secret" full><input type="password" value={az.clientSecret} onChange={(e) => setAz({ ...az, clientSecret: e.target.value })} /></Field>
          </div>
          <p className="muted">Uses client credentials against Microsoft Graph. Grant Directory.Read.All (or higher) on the app registration.</p>
          <div className="actions" style={{ marginTop: 14 }}>
            <button className="btn primary" type="submit">Save</button>
            <button className="btn" type="button" disabled={busy === 'az'} onClick={testAz}>{busy === 'az' ? 'Testing...' : 'Test connection'}</button>
          </div>
        </form>
      </div>
    </div>
  )
}
