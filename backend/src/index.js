import express from 'express'
import cors from 'cors'
import { loadStore, saveStore, audit, now, id } from './store.js'
import { testDomainController, testAzureAd, syncFromDirectory } from './ldap.js'

const app = express()
const PORT = Number(process.env.PORT) || 3001

app.use(cors())
app.use(express.json({ limit: '2mb' }))

function getStore() {
  return loadStore()
}

function persist(store) {
  saveStore(store)
}

function findById(list, itemId) {
  return list.find((x) => x.id === itemId)
}

function paginate(list, req) {
  const q = String(req.query.q || '').toLowerCase()
  const type = String(req.query.type || '')
  let items = list
  if (q) {
    items = items.filter((x) => JSON.stringify(x).toLowerCase().includes(q))
  }
  if (type) {
    items = items.filter((x) => x.type === type || x.status === type)
  }
  const page = Math.max(1, Number(req.query.page) || 1)
  const pageSize = Math.min(200, Math.max(1, Number(req.query.pageSize) || 50))
  const start = (page - 1) * pageSize
  return { items: items.slice(start, start + pageSize), total: items.length, page, pageSize }
}

function requireFields(body, fields) {
  const missing = fields.filter((f) => body[f] == null || body[f] === '')
  if (missing.length) {
    const err = new Error(`Missing required fields: ${missing.join(', ')}`)
    err.status = 400
    throw err
  }
}

app.get('/api/health', (_req, res) => {
  res.json({ ok: true, name: 'Admplus', time: now() })
})

app.get('/api/dashboard', (_req, res) => {
  const s = getStore()
  const enabledUsers = s.users.filter((u) => u.enabled).length
  const disabledUsers = s.users.length - enabledUsers
  const locked = s.users.filter((u) => u.locked).length
  const computersOnline = s.computers.filter((c) => c.enabled).length
  const gpoEnabled = s.gpos.filter((g) => g.status === 'enabled').length
  const lapsExpiring = s.laps.filter((l) => new Date(l.expiration) - Date.now() < 14 * 86400000).length
  res.json({
    counts: {
      users: s.users.length,
      enabledUsers,
      disabledUsers,
      lockedUsers: locked,
      computers: s.computers.length,
      computersOnline,
      groups: s.groups.length,
      gpos: s.gpos.length,
      gpoEnabled,
      shares: s.shares.length,
      laps: s.laps.length,
      lapsExpiring,
      audit: s.audit.length
    },
    connections: {
      domainController: {
        connected: Boolean(s.settings.domainController.connected),
        host: s.settings.domainController.host || null,
        domain: s.settings.domainController.domain || null,
        lastTest: s.settings.domainController.lastTest
      },
      azureAd: {
        connected: Boolean(s.settings.azureAd.connected),
        tenantId: s.settings.azureAd.tenantId || null,
        lastTest: s.settings.azureAd.lastTest
      }
    },
    recentAudit: s.audit.slice(0, 8),
    userTypes: s.users.reduce((acc, u) => {
      acc[u.type] = (acc[u.type] || 0) + 1
      return acc
    }, {}),
    groupTypes: s.groups.reduce((acc, g) => {
      acc[g.type] = (acc[g.type] || 0) + 1
      return acc
    }, {})
  })
})

app.get('/api/settings', (_req, res) => {
  const s = getStore()
  const dc = { ...s.settings.domainController, password: s.settings.domainController.password ? '********' : '' }
  const az = { ...s.settings.azureAd, clientSecret: s.settings.azureAd.clientSecret ? '********' : '' }
  res.json({ domainController: dc, azureAd: az })
})

app.put('/api/settings/domain-controller', (req, res) => {
  const s = getStore()
  const body = req.body || {}
  const current = s.settings.domainController
  s.settings.domainController = {
    ...current,
    host: body.host ?? current.host,
    port: body.port != null ? Number(body.port) : current.port,
    useSsl: body.useSsl != null ? Boolean(body.useSsl) : current.useSsl,
    bindDn: body.bindDn ?? current.bindDn,
    password: body.password && body.password !== '********' ? body.password : current.password,
    baseDn: body.baseDn ?? current.baseDn,
    domain: body.domain ?? current.domain
  }
  audit(s, { actor: 'operator', action: 'update', targetType: 'settings', target: 'domain-controller', detail: 'Updated domain controller connection settings' })
  persist(s)
  const dc = { ...s.settings.domainController, password: s.settings.domainController.password ? '********' : '' }
  res.json(dc)
})

app.put('/api/settings/azure-ad', (req, res) => {
  const s = getStore()
  const body = req.body || {}
  const current = s.settings.azureAd
  s.settings.azureAd = {
    ...current,
    tenantId: body.tenantId ?? current.tenantId,
    clientId: body.clientId ?? current.clientId,
    clientSecret: body.clientSecret && body.clientSecret !== '********' ? body.clientSecret : current.clientSecret
  }
  audit(s, { actor: 'operator', action: 'update', targetType: 'settings', target: 'azure-ad', detail: 'Updated Azure AD connection settings' })
  persist(s)
  const az = { ...s.settings.azureAd, clientSecret: s.settings.azureAd.clientSecret ? '********' : '' }
  res.json(az)
})

app.post('/api/settings/domain-controller/test', async (_req, res) => {
  const s = getStore()
  try {
    const result = await testDomainController(s)
    s.settings.domainController.connected = true
    s.settings.domainController.lastTest = now()
    s.settings.domainController.lastError = null
    audit(s, { actor: 'operator', action: 'connect', targetType: 'settings', target: 'domain-controller', detail: result.message })
    persist(s)
    res.json({ ok: true, ...result, lastTest: s.settings.domainController.lastTest })
  } catch (err) {
    s.settings.domainController.connected = false
    s.settings.domainController.lastTest = now()
    s.settings.domainController.lastError = err.message
    audit(s, { actor: 'operator', action: 'connect', targetType: 'settings', target: 'domain-controller', detail: err.message, result: 'failure' })
    persist(s)
    res.status(400).json({ ok: false, message: err.message, lastTest: s.settings.domainController.lastTest })
  }
})

app.post('/api/settings/azure-ad/test', async (_req, res) => {
  const s = getStore()
  try {
    const result = await testAzureAd(s)
    s.settings.azureAd.connected = true
    s.settings.azureAd.lastTest = now()
    s.settings.azureAd.lastError = null
    audit(s, { actor: 'operator', action: 'connect', targetType: 'settings', target: 'azure-ad', detail: result.message })
    persist(s)
    res.json({ ok: true, ...result, lastTest: s.settings.azureAd.lastTest })
  } catch (err) {
    s.settings.azureAd.connected = false
    s.settings.azureAd.lastTest = now()
    s.settings.azureAd.lastError = err.message
    audit(s, { actor: 'operator', action: 'connect', targetType: 'settings', target: 'azure-ad', detail: err.message, result: 'failure' })
    persist(s)
    res.status(400).json({ ok: false, message: err.message, lastTest: s.settings.azureAd.lastTest })
  }
})

app.post('/api/settings/sync', async (_req, res) => {
  const s = getStore()
  try {
    const live = await syncFromDirectory(s)
    if (!live) {
      return res.status(400).json({ ok: false, message: 'Domain controller is not connected' })
    }
    let created = 0
    let updated = 0
    for (const u of live.users) {
      const existing = s.users.find((x) => x.samAccountName.toLowerCase() === u.samAccountName.toLowerCase())
      if (existing) {
        Object.assign(existing, u)
        updated += 1
      } else {
        s.users.push({
          id: id('u'),
          manager: '',
          groups: [],
          lastLogon: now(),
          created: now(),
          ...u
        })
        created += 1
      }
    }
    audit(s, { actor: 'operator', action: 'sync', targetType: 'directory', target: 'users', detail: `Synced ${live.count} users (${created} created, ${updated} updated)` })
    persist(s)
    res.json({ ok: true, count: live.count, created, updated })
  } catch (err) {
    res.status(400).json({ ok: false, message: err.message })
  }
})

app.get('/api/users', (req, res) => {
  const s = getStore()
  res.json(paginate(s.users, req))
})

app.get('/api/users/:id', (req, res) => {
  const s = getStore()
  const item = findById(s.users, req.params.id)
  if (!item) return res.status(404).json({ message: 'User not found' })
  const groups = s.groups.filter((g) => item.groups.includes(g.id))
  res.json({ ...item, groupDetails: groups })
})

app.post('/api/users', (req, res) => {
  const s = getStore()
  const b = req.body || {}
  requireFields(b, ['samAccountName', 'displayName'])
  if (s.users.some((u) => u.samAccountName.toLowerCase() === String(b.samAccountName).toLowerCase())) {
    return res.status(409).json({ message: 'sAMAccountName already exists' })
  }
  const domain = s.settings.domainController.domain || 'contoso.local'
  const item = {
    id: id('u'),
    samAccountName: b.samAccountName,
    displayName: b.displayName,
    givenName: b.givenName || '',
    surname: b.surname || '',
    userPrincipalName: b.userPrincipalName || `${b.samAccountName}@${domain}`,
    email: b.email || '',
    type: b.type || 'user',
    enabled: b.enabled !== false,
    locked: false,
    passwordNeverExpires: Boolean(b.passwordNeverExpires),
    cannotChangePassword: Boolean(b.cannotChangePassword),
    mustChangePassword: b.mustChangePassword !== false,
    department: b.department || '',
    title: b.title || '',
    office: b.office || '',
    phone: b.phone || '',
    manager: b.manager || '',
    ou: b.ou || 'CN=Users,DC=contoso,DC=local',
    groups: Array.isArray(b.groups) ? b.groups : ['g-domain-users'],
    lastLogon: null,
    created: now(),
    source: 'ad'
  }
  s.users.push(item)
  for (const gid of item.groups) {
    const g = findById(s.groups, gid)
    if (g && !g.members.includes(item.id)) g.members.push(item.id)
  }
  audit(s, { actor: 'operator', action: 'create', targetType: 'user', target: item.samAccountName, detail: `Created ${item.type} ${item.displayName}` })
  persist(s)
  res.status(201).json(item)
})

app.put('/api/users/:id', (req, res) => {
  const s = getStore()
  const item = findById(s.users, req.params.id)
  if (!item) return res.status(404).json({ message: 'User not found' })
  const b = req.body || {}
  const fields = [
    'displayName', 'givenName', 'surname', 'userPrincipalName', 'email', 'type',
    'enabled', 'locked', 'passwordNeverExpires', 'cannotChangePassword', 'mustChangePassword',
    'department', 'title', 'office', 'phone', 'manager', 'ou'
  ]
  for (const f of fields) {
    if (b[f] !== undefined) item[f] = b[f]
  }
  if (Array.isArray(b.groups)) {
    for (const g of s.groups) {
      g.members = g.members.filter((m) => m !== item.id)
    }
    item.groups = b.groups
    for (const gid of item.groups) {
      const g = findById(s.groups, gid)
      if (g && !g.members.includes(item.id)) g.members.push(item.id)
    }
  }
  audit(s, { actor: 'operator', action: 'update', targetType: 'user', target: item.samAccountName, detail: `Updated user ${item.displayName}` })
  persist(s)
  res.json(item)
})

app.post('/api/users/:id/enable', (req, res) => {
  const s = getStore()
  const item = findById(s.users, req.params.id)
  if (!item) return res.status(404).json({ message: 'User not found' })
  item.enabled = true
  audit(s, { actor: 'operator', action: 'enable', targetType: 'user', target: item.samAccountName, detail: 'Account enabled' })
  persist(s)
  res.json(item)
})

app.post('/api/users/:id/disable', (req, res) => {
  const s = getStore()
  const item = findById(s.users, req.params.id)
  if (!item) return res.status(404).json({ message: 'User not found' })
  item.enabled = false
  audit(s, { actor: 'operator', action: 'disable', targetType: 'user', target: item.samAccountName, detail: 'Account disabled' })
  persist(s)
  res.json(item)
})

app.post('/api/users/:id/unlock', (req, res) => {
  const s = getStore()
  const item = findById(s.users, req.params.id)
  if (!item) return res.status(404).json({ message: 'User not found' })
  item.locked = false
  audit(s, { actor: 'operator', action: 'unlock', targetType: 'user', target: item.samAccountName, detail: 'Account unlocked' })
  persist(s)
  res.json(item)
})

app.post('/api/users/:id/reset-password', (req, res) => {
  const s = getStore()
  const item = findById(s.users, req.params.id)
  if (!item) return res.status(404).json({ message: 'User not found' })
  item.mustChangePassword = true
  audit(s, { actor: 'operator', action: 'reset-password', targetType: 'user', target: item.samAccountName, detail: 'Password reset; must change at next logon' })
  persist(s)
  res.json({ ok: true, mustChangePassword: true })
})

app.delete('/api/users/:id', (req, res) => {
  const s = getStore()
  const idx = s.users.findIndex((u) => u.id === req.params.id)
  if (idx < 0) return res.status(404).json({ message: 'User not found' })
  const item = s.users[idx]
  s.users.splice(idx, 1)
  for (const g of s.groups) g.members = g.members.filter((m) => m !== item.id)
  audit(s, { actor: 'operator', action: 'delete', targetType: 'user', target: item.samAccountName, detail: `Deleted user ${item.displayName}` })
  persist(s)
  res.json({ ok: true })
})

app.get('/api/computers', (req, res) => {
  res.json(paginate(getStore().computers, req))
})

app.get('/api/computers/:id', (req, res) => {
  const s = getStore()
  const item = findById(s.computers, req.params.id)
  if (!item) return res.status(404).json({ message: 'Computer not found' })
  const laps = s.laps.find((l) => l.computerId === item.id) || null
  res.json({ ...item, laps: laps ? { id: laps.id, expiration: laps.expiration, lastRotated: laps.lastRotated } : null })
})

app.post('/api/computers', (req, res) => {
  const s = getStore()
  const b = req.body || {}
  requireFields(b, ['name'])
  const domain = s.settings.domainController.domain || 'contoso.local'
  const item = {
    id: id('c'),
    name: b.name,
    dnsHostName: b.dnsHostName || `${String(b.name).toLowerCase()}.${domain}`,
    os: b.os || 'Windows 11 Enterprise',
    osVersion: b.osVersion || '',
    type: b.type || 'workstation',
    enabled: b.enabled !== false,
    ou: b.ou || 'OU=Computers,DC=contoso,DC=local',
    description: b.description || '',
    ipAddress: b.ipAddress || '',
    lastLogon: null,
    created: now(),
    managedBy: b.managedBy || '',
    source: 'ad'
  }
  s.computers.push(item)
  audit(s, { actor: 'operator', action: 'create', targetType: 'computer', target: item.name, detail: `Created computer ${item.name}` })
  persist(s)
  res.status(201).json(item)
})

app.put('/api/computers/:id', (req, res) => {
  const s = getStore()
  const item = findById(s.computers, req.params.id)
  if (!item) return res.status(404).json({ message: 'Computer not found' })
  const b = req.body || {}
  for (const f of ['name', 'dnsHostName', 'os', 'osVersion', 'type', 'enabled', 'ou', 'description', 'ipAddress', 'managedBy']) {
    if (b[f] !== undefined) item[f] = b[f]
  }
  audit(s, { actor: 'operator', action: 'update', targetType: 'computer', target: item.name, detail: `Updated computer ${item.name}` })
  persist(s)
  res.json(item)
})

app.post('/api/computers/:id/enable', (req, res) => {
  const s = getStore()
  const item = findById(s.computers, req.params.id)
  if (!item) return res.status(404).json({ message: 'Computer not found' })
  item.enabled = true
  audit(s, { actor: 'operator', action: 'enable', targetType: 'computer', target: item.name, detail: 'Computer enabled' })
  persist(s)
  res.json(item)
})

app.post('/api/computers/:id/disable', (req, res) => {
  const s = getStore()
  const item = findById(s.computers, req.params.id)
  if (!item) return res.status(404).json({ message: 'Computer not found' })
  item.enabled = false
  audit(s, { actor: 'operator', action: 'disable', targetType: 'computer', target: item.name, detail: 'Computer disabled' })
  persist(s)
  res.json(item)
})

app.delete('/api/computers/:id', (req, res) => {
  const s = getStore()
  const idx = s.computers.findIndex((c) => c.id === req.params.id)
  if (idx < 0) return res.status(404).json({ message: 'Computer not found' })
  const item = s.computers[idx]
  s.computers.splice(idx, 1)
  s.laps = s.laps.filter((l) => l.computerId !== item.id)
  audit(s, { actor: 'operator', action: 'delete', targetType: 'computer', target: item.name, detail: `Deleted computer ${item.name}` })
  persist(s)
  res.json({ ok: true })
})

app.get('/api/groups', (req, res) => {
  res.json(paginate(getStore().groups, req))
})

app.get('/api/groups/:id', (req, res) => {
  const s = getStore()
  const item = findById(s.groups, req.params.id)
  if (!item) return res.status(404).json({ message: 'Group not found' })
  const members = item.members.map((mid) => {
    const u = findById(s.users, mid)
    const c = findById(s.computers, mid)
    const g = findById(s.groups, mid)
    if (u) return { id: u.id, name: u.displayName, kind: 'user', extra: u.samAccountName }
    if (c) return { id: c.id, name: c.name, kind: 'computer', extra: c.dnsHostName }
    if (g) return { id: g.id, name: g.name, kind: 'group', extra: g.samAccountName }
    return { id: mid, name: mid, kind: 'unknown', extra: '' }
  })
  res.json({ ...item, memberDetails: members })
})

app.post('/api/groups', (req, res) => {
  const s = getStore()
  const b = req.body || {}
  requireFields(b, ['name'])
  const item = {
    id: id('g'),
    name: b.name,
    samAccountName: b.samAccountName || b.name,
    type: b.type || 'security',
    scope: b.scope || 'global',
    description: b.description || '',
    ou: b.ou || 'OU=Groups,DC=contoso,DC=local',
    members: Array.isArray(b.members) ? b.members : [],
    memberOf: Array.isArray(b.memberOf) ? b.memberOf : [],
    mail: b.mail || '',
    created: now(),
    source: 'ad'
  }
  s.groups.push(item)
  audit(s, { actor: 'operator', action: 'create', targetType: 'group', target: item.name, detail: `Created ${item.scope} ${item.type} group` })
  persist(s)
  res.status(201).json(item)
})

app.put('/api/groups/:id', (req, res) => {
  const s = getStore()
  const item = findById(s.groups, req.params.id)
  if (!item) return res.status(404).json({ message: 'Group not found' })
  const b = req.body || {}
  for (const f of ['name', 'samAccountName', 'type', 'scope', 'description', 'ou', 'mail', 'members', 'memberOf']) {
    if (b[f] !== undefined) item[f] = b[f]
  }
  audit(s, { actor: 'operator', action: 'update', targetType: 'group', target: item.name, detail: `Updated group ${item.name}` })
  persist(s)
  res.json(item)
})

app.post('/api/groups/:id/members', (req, res) => {
  const s = getStore()
  const item = findById(s.groups, req.params.id)
  if (!item) return res.status(404).json({ message: 'Group not found' })
  const memberId = req.body?.memberId
  if (!memberId) return res.status(400).json({ message: 'memberId is required' })
  if (!item.members.includes(memberId)) item.members.push(memberId)
  const user = findById(s.users, memberId)
  if (user && !user.groups.includes(item.id)) user.groups.push(item.id)
  audit(s, { actor: 'operator', action: 'add-member', targetType: 'group', target: item.name, detail: `Added member ${memberId}` })
  persist(s)
  res.json(item)
})

app.delete('/api/groups/:id/members/:memberId', (req, res) => {
  const s = getStore()
  const item = findById(s.groups, req.params.id)
  if (!item) return res.status(404).json({ message: 'Group not found' })
  item.members = item.members.filter((m) => m !== req.params.memberId)
  const user = findById(s.users, req.params.memberId)
  if (user) user.groups = user.groups.filter((g) => g !== item.id)
  audit(s, { actor: 'operator', action: 'remove-member', targetType: 'group', target: item.name, detail: `Removed member ${req.params.memberId}` })
  persist(s)
  res.json(item)
})

app.delete('/api/groups/:id', (req, res) => {
  const s = getStore()
  const idx = s.groups.findIndex((g) => g.id === req.params.id)
  if (idx < 0) return res.status(404).json({ message: 'Group not found' })
  const item = s.groups[idx]
  s.groups.splice(idx, 1)
  for (const u of s.users) u.groups = u.groups.filter((g) => g !== item.id)
  audit(s, { actor: 'operator', action: 'delete', targetType: 'group', target: item.name, detail: `Deleted group ${item.name}` })
  persist(s)
  res.json({ ok: true })
})

app.get('/api/gpos', (req, res) => {
  res.json(paginate(getStore().gpos, req))
})

app.get('/api/gpos/:id', (req, res) => {
  const s = getStore()
  const item = findById(s.gpos, req.params.id)
  if (!item) return res.status(404).json({ message: 'GPO not found' })
  res.json(item)
})

app.post('/api/gpos', (req, res) => {
  const s = getStore()
  const b = req.body || {}
  requireFields(b, ['name'])
  const item = {
    id: id('gpo'),
    name: b.name,
    status: b.status || 'enabled',
    linkedOus: Array.isArray(b.linkedOus) ? b.linkedOus : [],
    enforced: Boolean(b.enforced),
    description: b.description || '',
    created: now(),
    modified: now(),
    settings: b.settings && typeof b.settings === 'object' ? b.settings : {}
  }
  s.gpos.push(item)
  audit(s, { actor: 'operator', action: 'create', targetType: 'gpo', target: item.name, detail: 'Created group policy object' })
  persist(s)
  res.status(201).json(item)
})

app.put('/api/gpos/:id', (req, res) => {
  const s = getStore()
  const item = findById(s.gpos, req.params.id)
  if (!item) return res.status(404).json({ message: 'GPO not found' })
  const b = req.body || {}
  for (const f of ['name', 'status', 'linkedOus', 'enforced', 'description', 'settings']) {
    if (b[f] !== undefined) item[f] = b[f]
  }
  item.modified = now()
  audit(s, { actor: 'operator', action: 'update', targetType: 'gpo', target: item.name, detail: 'Updated group policy object' })
  persist(s)
  res.json(item)
})

app.delete('/api/gpos/:id', (req, res) => {
  const s = getStore()
  const idx = s.gpos.findIndex((g) => g.id === req.params.id)
  if (idx < 0) return res.status(404).json({ message: 'GPO not found' })
  const item = s.gpos[idx]
  s.gpos.splice(idx, 1)
  audit(s, { actor: 'operator', action: 'delete', targetType: 'gpo', target: item.name, detail: 'Deleted group policy object' })
  persist(s)
  res.json({ ok: true })
})

app.get('/api/shares', (req, res) => {
  res.json(paginate(getStore().shares, req))
})

app.get('/api/shares/:id', (req, res) => {
  const s = getStore()
  const item = findById(s.shares, req.params.id)
  if (!item) return res.status(404).json({ message: 'Share not found' })
  res.json(item)
})

app.post('/api/shares', (req, res) => {
  const s = getStore()
  const b = req.body || {}
  requireFields(b, ['name', 'path'])
  const item = {
    id: id('sh'),
    name: b.name,
    path: b.path,
    server: b.server || '',
    description: b.description || '',
    permissions: Array.isArray(b.permissions) ? b.permissions : [],
    hidden: Boolean(b.hidden),
    created: now()
  }
  s.shares.push(item)
  audit(s, { actor: 'operator', action: 'create', targetType: 'share', target: item.name, detail: `Created share ${item.path}` })
  persist(s)
  res.status(201).json(item)
})

app.put('/api/shares/:id', (req, res) => {
  const s = getStore()
  const item = findById(s.shares, req.params.id)
  if (!item) return res.status(404).json({ message: 'Share not found' })
  const b = req.body || {}
  for (const f of ['name', 'path', 'server', 'description', 'permissions', 'hidden']) {
    if (b[f] !== undefined) item[f] = b[f]
  }
  audit(s, { actor: 'operator', action: 'update', targetType: 'share', target: item.name, detail: `Updated share ${item.name}` })
  persist(s)
  res.json(item)
})

app.delete('/api/shares/:id', (req, res) => {
  const s = getStore()
  const idx = s.shares.findIndex((x) => x.id === req.params.id)
  if (idx < 0) return res.status(404).json({ message: 'Share not found' })
  const item = s.shares[idx]
  s.shares.splice(idx, 1)
  audit(s, { actor: 'operator', action: 'delete', targetType: 'share', target: item.name, detail: `Deleted share ${item.name}` })
  persist(s)
  res.json({ ok: true })
})

function randomPassword(len = 16) {
  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*'
  let out = ''
  for (let i = 0; i < len; i++) out += chars[Math.floor(Math.random() * chars.length)]
  return out
}

app.get('/api/laps', (req, res) => {
  const s = getStore()
  const list = s.laps.map((l) => ({
    ...l,
    password: undefined,
    daysRemaining: Math.ceil((new Date(l.expiration) - Date.now()) / 86400000)
  }))
  res.json(paginate(list, req))
})

app.get('/api/laps/:id', (req, res) => {
  const s = getStore()
  const item = findById(s.laps, req.params.id)
  if (!item) return res.status(404).json({ message: 'LAPS record not found' })
  audit(s, { actor: 'operator', action: 'view-password', targetType: 'laps', target: item.computerName, detail: 'Retrieved LAPS password' })
  persist(s)
  res.json(item)
})

app.post('/api/laps', (req, res) => {
  const s = getStore()
  const b = req.body || {}
  requireFields(b, ['computerId'])
  const computer = findById(s.computers, b.computerId)
  if (!computer) return res.status(404).json({ message: 'Computer not found' })
  if (s.laps.some((l) => l.computerId === computer.id)) {
    return res.status(409).json({ message: 'LAPS already configured for this computer' })
  }
  const item = {
    id: id('laps'),
    computerId: computer.id,
    computerName: computer.name,
    account: b.account || 'Administrator',
    password: randomPassword(),
    expiration: new Date(Date.now() + (Number(b.ageDays) || 30) * 86400000).toISOString(),
    lastRotated: now()
  }
  s.laps.push(item)
  audit(s, { actor: 'operator', action: 'create', targetType: 'laps', target: item.computerName, detail: 'Enabled LAPS for computer' })
  persist(s)
  res.status(201).json(item)
})

app.post('/api/laps/:id/rotate', (req, res) => {
  const s = getStore()
  const item = findById(s.laps, req.params.id)
  if (!item) return res.status(404).json({ message: 'LAPS record not found' })
  item.password = randomPassword()
  item.lastRotated = now()
  item.expiration = new Date(Date.now() + 30 * 86400000).toISOString()
  audit(s, { actor: 'operator', action: 'rotate', targetType: 'laps', target: item.computerName, detail: 'Rotated LAPS password' })
  persist(s)
  res.json(item)
})

app.get('/api/audit', (req, res) => {
  res.json(paginate(getStore().audit, req))
})

app.get('/api/reports', (_req, res) => {
  const s = getStore()
  const staleMs = 90 * 86400000
  const nowMs = Date.now()
  const inactiveUsers = s.users.filter((u) => !u.lastLogon || nowMs - new Date(u.lastLogon).getTime() > staleMs)
  const disabledUsers = s.users.filter((u) => !u.enabled)
  const lockedUsers = s.users.filter((u) => u.locked)
  const passwordNeverExpires = s.users.filter((u) => u.passwordNeverExpires)
  const privileged = s.groups.filter((g) => /admin/i.test(g.name)).map((g) => ({
    group: g.name,
    members: g.members.map((mid) => {
      const u = findById(s.users, mid)
      return u ? { id: u.id, name: u.displayName, sam: u.samAccountName } : { id: mid, name: mid, sam: '' }
    })
  }))
  const emptyGroups = s.groups.filter((g) => g.members.length === 0)
  const unlinkedGpos = s.gpos.filter((g) => !g.linkedOus.length)
  const lapsSoon = s.laps.filter((l) => new Date(l.expiration) - nowMs < 14 * 86400000)
  const computersNoLaps = s.computers.filter((c) => c.type !== 'domainController' && !s.laps.some((l) => l.computerId === c.id))
  res.json({
    generatedAt: now(),
    summaries: {
      inactiveUsers: inactiveUsers.length,
      disabledUsers: disabledUsers.length,
      lockedUsers: lockedUsers.length,
      passwordNeverExpires: passwordNeverExpires.length,
      emptyGroups: emptyGroups.length,
      unlinkedGpos: unlinkedGpos.length,
      lapsExpiringSoon: lapsSoon.length,
      computersNoLaps: computersNoLaps.length
    },
    inactiveUsers,
    disabledUsers,
    lockedUsers,
    passwordNeverExpires,
    privileged,
    emptyGroups,
    unlinkedGpos,
    lapsSoon,
    computersNoLaps
  })
})

app.use((err, _req, res, _next) => {
  const status = err.status || 500
  res.status(status).json({ message: err.message || 'Internal error' })
})

app.listen(PORT, '0.0.0.0', () => {
  console.log(`Admplus API listening on ${PORT}`)
})
