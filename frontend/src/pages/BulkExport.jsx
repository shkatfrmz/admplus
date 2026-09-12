import { useState } from 'react'
import { api } from '../api.js'
import { Field, Flash, PageHead } from '../ui.jsx'

function parseCsv(text) {
  const lines = text.trim().split(/\r?\n/).filter(Boolean)
  if (!lines.length) return []
  const headers = lines[0].split(',').map((h) => h.trim())
  return lines.slice(1).map((line) => {
    const cols = line.split(',').map((c) => c.trim())
    const row = {}
    headers.forEach((h, i) => { row[h] = cols[i] || '' })
    return row
  })
}

export default function BulkExport() {
  const [csv, setCsv] = useState('samAccountName,displayName,givenName,surname,email,department,title\nnlee,Nora Lee,Nora,Lee,nora.lee@contoso.com,IT,Analyst')
  const [flash, setFlash] = useState('')
  const [script, setScript] = useState('')

  async function importCsv() {
    try {
      const rows = parseCsv(csv)
      const r = await api.importUsers(rows)
      setFlash(`Imported ${r.created}, skipped ${r.skipped}`)
    } catch (e) { setFlash(e.message) }
  }

  async function dump(kind) {
    const text = kind === 'ps' ? await api.exportPowershell() : await api.exportCsharp()
    setScript(text)
  }

  function download() {
    const blob = new Blob([script], { type: 'text/plain' })
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = script.includes('Import-Module') ? 'admplus-export.ps1' : 'AdmplusExport.cs'
    a.click()
  }

  return (
    <div>
      <PageHead title="Bulk provision & export" subtitle="CSV import for users, plus generated PowerShell and C# DirectoryServices snippets." />
      <Flash msg={flash} kind={flash && !flash.startsWith('Imported') ? 'err' : 'ok'} />
      <div className="two-col">
        <div className="card">
          <h3 className="section-title">CSV import</h3>
          <Field label="Rows (header + data)" full><textarea style={{ minHeight: 180 }} value={csv} onChange={(e) => setCsv(e.target.value)} /></Field>
          <button className="btn primary" style={{ marginTop: 10 }} onClick={importCsv}>Import users</button>
        </div>
        <div className="card">
          <h3 className="section-title">Cmdlet / code export</h3>
          <div className="actions">
            <button className="btn" onClick={() => dump('ps')}>PowerShell</button>
            <button className="btn" onClick={() => dump('cs')}>C# LDAP</button>
            {script && <button className="btn primary" onClick={download}>Download</button>}
          </div>
          {script && <pre className="password-box" style={{ marginTop: 12, maxHeight: 320, overflow: 'auto', whiteSpace: 'pre-wrap' }}>{script}</pre>}
        </div>
      </div>
    </div>
  )
}
