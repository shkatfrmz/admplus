import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, Field, Flash, PageHead, fmt } from '../ui.jsx'

const emptyDc = { host: '', port: 389, useSsl: false, bindDn: '', password: '', baseDn: '', domain: '', searchPageSize: 1000 }
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

  function normalizeDc(next = dc) {
    const payload = { ...next, host: (next.host || '').trim(), domain: (next.domain || '').trim(), baseDn: (next.baseDn || '').trim(), bindDn: (next.bindDn || '').trim() }
    if (!payload.host && payload.domain) payload.host = payload.domain
    if (!payload.baseDn && payload.domain)
      payload.baseDn = payload.domain.split('.').filter(Boolean).map((p) => `DC=${p}`).join(',')
    if (!payload.domain && payload.baseDn)
      payload.domain = payload.baseDn.split(',').filter((p) => /^DC=/i.test(p.trim())).map((p) => p.trim().slice(3)).join('.')
    return payload
  }

  async function persistDc(next = dc) {
    const payload = normalizeDc(next)
    if (!payload.password || payload.password === '********')
      delete payload.password
    const saved = await api.saveDc(payload)
    const merged = { ...payload, ...saved }
    if (next.password && next.password !== '********')
      merged.password = next.password
    else if (merged.password === '********')
      merged.password = ''
    setDc(merged)
    return merged
  }

  async function saveDc(e) {
    e.preventDefault()
    try {
      await persistDc()
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
      const current = await persistDc()
      const r = await api.testDc(current)
      setDc((d) => ({
        ...d,
        connected: true,
        lastTest: r.lastTest,
        lastError: null,
        host: r.host || d.host,
        bindDn: r.bindDn || d.bindDn,
        baseDn: r.baseDn || d.baseDn,
        domain: r.domain || d.domain
      }))
      msg(r.message)
    } catch (err) {
      const extra = err.data || {}
      setDc((d) => ({
        ...d,
        connected: false,
        lastError: err.message,
        host: extra.host || d.host,
        bindDn: extra.bindDn || d.bindDn,
        baseDn: extra.baseDn || d.baseDn,
        domain: extra.domain || d.domain
      }))
      msg(err.message, 'err')
    } finally { setBusy('') }
  }

  async function testAz() {
    setBusy('az')
    try {
      const saved = await api.saveAzure(az)
      setAz((a) => ({ ...a, ...saved }))
      const r = await api.testAzure(az)
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
      const x = r.result || {}
      msg(`Synced users ${x.users?.total ?? r.count ?? 0} (${x.users?.created ?? r.created ?? 0} new/${x.users?.updated ?? r.updated ?? 0} upd), computers ${x.computers?.total ?? 0}, groups ${x.groups?.total ?? 0}, OUs ${x.ous?.total ?? 0}, GPOs ${x.gpos?.total ?? 0}`)
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
            <Field label="Host / FQDN"><input value={dc.host} onChange={(e) => setDc({ ...dc, host: e.target.value })} placeholder="dc01.labnet.local or labnet.local" /></Field>
            <Field label="Port"><input type="number" value={dc.port} onChange={(e) => setDc({ ...dc, port: Number(e.target.value) })} /></Field>
            <Field label="Domain"><input value={dc.domain} onChange={(e) => setDc({ ...dc, domain: e.target.value })} placeholder="labnet.local" /></Field>
            <Field label="Base DN"><input value={dc.baseDn} onChange={(e) => setDc({ ...dc, baseDn: e.target.value })} placeholder="DC=labnet,DC=local" /></Field>
            <Field label="Bind DN / UPN" full><input value={dc.bindDn} onChange={(e) => setDc({ ...dc, bindDn: e.target.value })} placeholder="CN=Administrator,CN=Users,DC=labnet,DC=local or Administrator@labnet.local" /></Field>
            <Field label="Page size"><input type="number" min="100" max="5000" step="100" value={dc.searchPageSize ?? 1000} onChange={(e) => setDc({ ...dc, searchPageSize: Number(e.target.value) })} /></Field>
            <Field label="Password" full><input type="password" value={dc.password === '********' ? '' : (dc.password || '')} onChange={(e) => setDc({ ...dc, password: e.target.value })} placeholder={dc.password === '********' ? 'Saved — type again to change' : 'Bind password'} autoComplete="off" /></Field>
            <label className="check"><input type="checkbox" checked={Boolean(dc.useSsl)} onChange={(e) => setDc({ ...dc, useSsl: e.target.checked, port: e.target.checked ? 636 : 389 })} /> Use LDAPS</label>
          </div>
          <p className="muted">Host can be a DC FQDN or the DNS domain. Bind DN can be a distinguished name, UPN, or SAM. Test saves the form first. Sync pages through the whole domain, so large directories (tens of thousands of objects) are fetched completely. Page size defaults to 1000; lower it if the DC rejects large pages. Failures are written to Logs.</p>
          <div className="actions" style={{ marginTop: 14 }}>
            <button className="btn primary" type="submit">Save</button>
            <button className="btn" type="button" disabled={busy === 'dc'} onClick={testDc}>{busy === 'dc' ? 'Testing...' : 'Test connection'}</button>
            <button className="btn" type="button" disabled={busy === 'sync'} onClick={sync}>{busy === 'sync' ? 'Syncing...' : 'Sync directory from DC'}</button>
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
