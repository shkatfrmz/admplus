import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { Badge, Flash, PageHead, fmt } from '../ui.jsx'

export default function SecurityHealth() {
  const [d, setD] = useState(null)
  const [flash, setFlash] = useState('')

  async function load() { setD(await api.securityHealth()) }
  useEffect(() => { load().catch((e) => setFlash(e.message)) }, [])

  async function toggle(key) {
    const next = { ...d.health, [key]: !d.health[key] }
    await api.saveSecurityHealth(next)
    load()
  }

  if (!d) return <p className="muted">Assessing forest health…</p>
  const flags = [
    ['ldapSigningRequired', 'LDAP signing required'],
    ['ldapChannelBinding', 'LDAP channel binding'],
    ['kerberosAes', 'Kerberos AES'],
    ['ntlmV1Disabled', 'NTLMv1 disabled'],
    ['smbSigning', 'SMB signing']
  ]

  return (
    <div>
      <PageHead title="Kerberos / NTLM / LDAP health" subtitle="Signing, channel binding, encryption types, and duplicate SPN report." />
      <Flash msg={flash} kind="err" />
      <p className="muted">Last check {fmt(d.health.checkedAt)}</p>
      <div className="grid-cards">
        {flags.map(([k, label]) => (
          <button key={k} className="card stat" style={{ textAlign: 'left', cursor: 'pointer' }} onClick={() => toggle(k)}>
            <div className="label">{label}</div>
            <div className="value" style={{ fontSize: 18 }}>{d.health[k] ? <Badge kind="ok">on</Badge> : <Badge kind="danger">off</Badge>}</div>
          </button>
        ))}
      </div>
      <div className="card" style={{ marginBottom: 14 }}>
        <h3 className="section-title">Findings</h3>
        {(d.findings || []).length === 0 && <p className="muted">No issues.</p>}
        {(d.findings || []).map((f, i) => (
          <p key={i}><Badge kind={f.severity === 'high' ? 'danger' : 'warn'}>{f.severity}</Badge> {f.code}: {f.detail}</p>
        ))}
      </div>
      <div className="card">
        <h3 className="section-title">Duplicate SPNs</h3>
        {(d.duplicates || []).length === 0 && <p className="muted">No duplicate SPNs.</p>}
        {(d.duplicates || []).map((dup) => (
          <div key={dup.spn} style={{ marginBottom: 10 }}>
            <strong>{dup.spn}</strong>
            <div className="pill-row" style={{ marginTop: 6 }}>
              {dup.principals.map((p) => <Badge key={p.principalId} kind="danger">{p.principal} ({p.kind})</Badge>)}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
