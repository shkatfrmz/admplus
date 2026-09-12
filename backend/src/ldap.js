import { Client } from 'ldapts'

function dcConfig(store) {
  const dc = store.settings.domainController
  const protocol = dc.useSsl ? 'ldaps' : 'ldap'
  const port = dc.port || (dc.useSsl ? 636 : 389)
  return { ...dc, url: `${protocol}://${dc.host}:${port}` }
}

export async function testDomainController(store) {
  const dc = store.settings.domainController
  if (!dc.host || !dc.bindDn) {
    throw new Error('Host and bind DN are required')
  }
  const { url } = dcConfig(store)
  const client = new Client({ url, timeout: 8000, connectTimeout: 8000, tlsOptions: { rejectUnauthorized: false } })
  try {
    await client.bind(dc.bindDn, dc.password || '')
    const base = dc.baseDn || ''
    if (base) {
      await client.search(base, { scope: 'base', sizeLimit: 1 })
    }
    return { ok: true, message: `Bound to ${url}` }
  } finally {
    try { await client.unbind() } catch { /* ignore */ }
  }
}

export async function testAzureAd(store) {
  const az = store.settings.azureAd
  if (!az.tenantId || !az.clientId || !az.clientSecret) {
    throw new Error('Tenant ID, client ID, and client secret are required')
  }
  const tokenUrl = `https://login.microsoftonline.com/${az.tenantId}/oauth2/v2.0/token`
  const body = new URLSearchParams({
    client_id: az.clientId,
    client_secret: az.clientSecret,
    scope: 'https://graph.microsoft.com/.default',
    grant_type: 'client_credentials'
  })
  const res = await fetch(tokenUrl, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body
  })
  const data = await res.json().catch(() => ({}))
  if (!res.ok) {
    throw new Error(data.error_description || data.error || `Azure AD token request failed (${res.status})`)
  }
  return { ok: true, message: 'Azure AD application credentials accepted' }
}

async function withLdap(store, fn) {
  const dc = store.settings.domainController
  if (!dc.connected || !dc.host) return null
  const { url } = dcConfig(store)
  const client = new Client({ url, timeout: 12000, connectTimeout: 8000, tlsOptions: { rejectUnauthorized: false } })
  try {
    await client.bind(dc.bindDn, dc.password || '')
    return await fn(client, dc)
  } finally {
    try { await client.unbind() } catch { /* ignore */ }
  }
}

function first(attr) {
  if (attr == null) return ''
  if (Array.isArray(attr)) return String(attr[0] ?? '')
  return String(attr)
}

function boolFlag(uac, bit) {
  const n = Number(uac) || 0
  return (n & bit) !== 0
}

export async function syncFromDirectory(store) {
  return withLdap(store, async (client, dc) => {
    const base = dc.baseDn || dc.bindDn.replace(/^[^,]+,/, '')
    const { searchEntries: users } = await client.search(base, {
      scope: 'sub',
      filter: '(|(objectClass=user)(objectClass=inetOrgPerson))',
      attributes: [
        'sAMAccountName', 'displayName', 'givenName', 'sn', 'userPrincipalName',
        'mail', 'department', 'title', 'physicalDeliveryOfficeName', 'telephoneNumber',
        'userAccountControl', 'distinguishedName', 'objectClass'
      ],
      sizeLimit: 200
    })
    const mapped = []
    for (const e of users) {
      const classes = [].concat(e.objectClass || []).map(String)
      if (classes.includes('computer')) continue
      const uac = first(e.userAccountControl)
      let type = 'user'
      if (classes.includes('inetOrgPerson') && !classes.includes('user')) type = 'inetOrgPerson'
      const sam = first(e.sAMAccountName)
      if (!sam) continue
      mapped.push({
        samAccountName: sam,
        displayName: first(e.displayName) || sam,
        givenName: first(e.givenName),
        surname: first(e.sn),
        userPrincipalName: first(e.userPrincipalName),
        email: first(e.mail),
        type,
        enabled: !boolFlag(uac, 2),
        locked: boolFlag(uac, 16),
        passwordNeverExpires: boolFlag(uac, 65536),
        cannotChangePassword: false,
        mustChangePassword: false,
        department: first(e.department),
        title: first(e.title),
        office: first(e.physicalDeliveryOfficeName),
        phone: first(e.telephoneNumber),
        ou: first(e.distinguishedName),
        source: 'ad-live'
      })
    }
    return { users: mapped, count: mapped.length }
  })
}
