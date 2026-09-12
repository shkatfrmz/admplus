import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const DATA_FILE = path.join(__dirname, '..', 'data', 'store.json')

function now() {
  return new Date().toISOString()
}

function id(prefix) {
  return `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`
}

function seed() {
  const created = now()
  return {
    settings: {
      domainController: {
        host: '',
        port: 389,
        useSsl: false,
        bindDn: '',
        password: '',
        baseDn: '',
        domain: '',
        connected: false,
        lastTest: null,
        lastError: null
      },
      azureAd: {
        tenantId: '',
        clientId: '',
        clientSecret: '',
        connected: false,
        lastTest: null,
        lastError: null
      }
    },
    users: [
      {
        id: 'u-admin',
        samAccountName: 'administrator',
        displayName: 'Domain Administrator',
        givenName: 'Domain',
        surname: 'Admin',
        userPrincipalName: 'administrator@contoso.local',
        email: 'admin@contoso.local',
        type: 'user',
        enabled: true,
        locked: false,
        passwordNeverExpires: true,
        cannotChangePassword: false,
        mustChangePassword: false,
        department: 'IT',
        title: 'Enterprise Admin',
        office: 'HQ',
        phone: '+1-555-0100',
        manager: '',
        ou: 'CN=Users,DC=contoso,DC=local',
        groups: ['g-ea', 'g-da', 'g-domain-users'],
        lastLogon: created,
        created,
        source: 'ad'
      },
      {
        id: 'u-jsmith',
        samAccountName: 'jsmith',
        displayName: 'Jane Smith',
        givenName: 'Jane',
        surname: 'Smith',
        userPrincipalName: 'jsmith@contoso.local',
        email: 'jane.smith@contoso.com',
        type: 'user',
        enabled: true,
        locked: false,
        passwordNeverExpires: false,
        cannotChangePassword: false,
        mustChangePassword: false,
        department: 'Finance',
        title: 'Controller',
        office: 'Floor 3',
        phone: '+1-555-0142',
        manager: 'u-admin',
        ou: 'OU=Finance,DC=contoso,DC=local',
        groups: ['g-finance', 'g-domain-users'],
        lastLogon: created,
        created,
        source: 'ad'
      },
      {
        id: 'u-mjones',
        samAccountName: 'mjones',
        displayName: 'Marcus Jones',
        givenName: 'Marcus',
        surname: 'Jones',
        userPrincipalName: 'mjones@contoso.local',
        email: 'marcus.jones@contoso.com',
        type: 'user',
        enabled: true,
        locked: false,
        passwordNeverExpires: false,
        cannotChangePassword: false,
        mustChangePassword: true,
        department: 'Engineering',
        title: 'Systems Engineer',
        office: 'Lab A',
        phone: '+1-555-0188',
        manager: 'u-admin',
        ou: 'OU=Engineering,DC=contoso,DC=local',
        groups: ['g-eng', 'g-helpdesk', 'g-domain-users'],
        lastLogon: created,
        created,
        source: 'ad'
      },
      {
        id: 'u-svc-sql',
        samAccountName: 'svc-sql',
        displayName: 'SQL Service Account',
        givenName: 'SQL',
        surname: 'Service',
        userPrincipalName: 'svc-sql@contoso.local',
        email: '',
        type: 'service',
        enabled: true,
        locked: false,
        passwordNeverExpires: true,
        cannotChangePassword: true,
        mustChangePassword: false,
        department: 'IT',
        title: 'Service Account',
        office: '',
        phone: '',
        manager: '',
        ou: 'OU=Service Accounts,DC=contoso,DC=local',
        groups: ['g-domain-users'],
        lastLogon: created,
        created,
        source: 'ad'
      },
      {
        id: 'u-guest',
        samAccountName: 'guest',
        displayName: 'Guest',
        givenName: '',
        surname: 'Guest',
        userPrincipalName: 'guest@contoso.local',
        email: '',
        type: 'guest',
        enabled: false,
        locked: false,
        passwordNeverExpires: true,
        cannotChangePassword: true,
        mustChangePassword: false,
        department: '',
        title: '',
        office: '',
        phone: '',
        manager: '',
        ou: 'CN=Users,DC=contoso,DC=local',
        groups: ['g-domain-guests'],
        lastLogon: null,
        created,
        source: 'ad'
      },
      {
        id: 'u-inet',
        samAccountName: 'inet-klee',
        displayName: 'Kai Lee (InetOrg)',
        givenName: 'Kai',
        surname: 'Lee',
        userPrincipalName: 'klee@contoso.local',
        email: 'kai.lee@contoso.com',
        type: 'inetOrgPerson',
        enabled: true,
        locked: false,
        passwordNeverExpires: false,
        cannotChangePassword: false,
        mustChangePassword: false,
        department: 'Marketing',
        title: 'Campaign Lead',
        office: 'Remote',
        phone: '+1-555-0199',
        manager: 'u-jsmith',
        ou: 'OU=Marketing,DC=contoso,DC=local',
        groups: ['g-mkt', 'g-domain-users'],
        lastLogon: created,
        created,
        source: 'ad'
      }
    ],
    computers: [
      {
        id: 'c-dc01',
        name: 'DC01',
        dnsHostName: 'dc01.contoso.local',
        os: 'Windows Server 2022 Datacenter',
        osVersion: '10.0.20348',
        type: 'domainController',
        enabled: true,
        ou: 'OU=Domain Controllers,DC=contoso,DC=local',
        description: 'Primary domain controller',
        ipAddress: '10.0.0.10',
        lastLogon: created,
        created,
        managedBy: 'u-admin',
        source: 'ad'
      },
      {
        id: 'c-fs01',
        name: 'FS01',
        dnsHostName: 'fs01.contoso.local',
        os: 'Windows Server 2022 Standard',
        osVersion: '10.0.20348',
        type: 'server',
        enabled: true,
        ou: 'OU=Servers,DC=contoso,DC=local',
        description: 'File server',
        ipAddress: '10.0.0.21',
        lastLogon: created,
        created,
        managedBy: 'u-mjones',
        source: 'ad'
      },
      {
        id: 'c-wks-042',
        name: 'WKS-042',
        dnsHostName: 'wks-042.contoso.local',
        os: 'Windows 11 Enterprise',
        osVersion: '10.0.22631',
        type: 'workstation',
        enabled: true,
        ou: 'OU=Workstations,DC=contoso,DC=local',
        description: 'Finance workstation',
        ipAddress: '10.0.10.42',
        lastLogon: created,
        created,
        managedBy: 'u-jsmith',
        source: 'ad'
      },
      {
        id: 'c-lap-018',
        name: 'LAP-018',
        dnsHostName: 'lap-018.contoso.local',
        os: 'Windows 11 Pro',
        osVersion: '10.0.22631',
        type: 'laptop',
        enabled: true,
        ou: 'OU=Laptops,DC=contoso,DC=local',
        description: 'Engineering laptop',
        ipAddress: '10.0.20.18',
        lastLogon: created,
        created,
        managedBy: 'u-mjones',
        source: 'ad'
      }
    ],
    groups: [
      {
        id: 'g-ea',
        name: 'Enterprise Admins',
        samAccountName: 'Enterprise Admins',
        type: 'security',
        scope: 'universal',
        description: 'Members can make forest-wide changes',
        ou: 'CN=Users,DC=contoso,DC=local',
        members: ['u-admin'],
        memberOf: [],
        mail: '',
        created,
        source: 'ad'
      },
      {
        id: 'g-da',
        name: 'Domain Admins',
        samAccountName: 'Domain Admins',
        type: 'security',
        scope: 'global',
        description: 'Designated administrators of the domain',
        ou: 'CN=Users,DC=contoso,DC=local',
        members: ['u-admin'],
        memberOf: ['g-ea'],
        mail: '',
        created,
        source: 'ad'
      },
      {
        id: 'g-domain-users',
        name: 'Domain Users',
        samAccountName: 'Domain Users',
        type: 'security',
        scope: 'global',
        description: 'All domain users',
        ou: 'CN=Users,DC=contoso,DC=local',
        members: ['u-admin', 'u-jsmith', 'u-mjones', 'u-svc-sql', 'u-inet'],
        memberOf: [],
        mail: '',
        created,
        source: 'ad'
      },
      {
        id: 'g-domain-guests',
        name: 'Domain Guests',
        samAccountName: 'Domain Guests',
        type: 'security',
        scope: 'global',
        description: 'All domain guests',
        ou: 'CN=Users,DC=contoso,DC=local',
        members: ['u-guest'],
        memberOf: [],
        mail: '',
        created,
        source: 'ad'
      },
      {
        id: 'g-finance',
        name: 'Finance',
        samAccountName: 'Finance',
        type: 'security',
        scope: 'global',
        description: 'Finance department',
        ou: 'OU=Groups,DC=contoso,DC=local',
        members: ['u-jsmith'],
        memberOf: [],
        mail: 'finance@contoso.com',
        created,
        source: 'ad'
      },
      {
        id: 'g-eng',
        name: 'Engineering',
        samAccountName: 'Engineering',
        type: 'security',
        scope: 'global',
        description: 'Engineering department',
        ou: 'OU=Groups,DC=contoso,DC=local',
        members: ['u-mjones'],
        memberOf: [],
        mail: 'engineering@contoso.com',
        created,
        source: 'ad'
      },
      {
        id: 'g-mkt',
        name: 'Marketing DL',
        samAccountName: 'Marketing-DL',
        type: 'distribution',
        scope: 'universal',
        description: 'Marketing distribution list',
        ou: 'OU=Groups,DC=contoso,DC=local',
        members: ['u-inet'],
        memberOf: [],
        mail: 'marketing@contoso.com',
        created,
        source: 'ad'
      },
      {
        id: 'g-helpdesk',
        name: 'Helpdesk',
        samAccountName: 'Helpdesk',
        type: 'security',
        scope: 'domainLocal',
        description: 'Helpdesk operators',
        ou: 'OU=Groups,DC=contoso,DC=local',
        members: ['u-mjones'],
        memberOf: [],
        mail: '',
        created,
        source: 'ad'
      }
    ],
    gpos: [
      {
        id: 'gpo-default-domain',
        name: 'Default Domain Policy',
        status: 'enabled',
        linkedOus: ['DC=contoso,DC=local'],
        enforced: true,
        description: 'Password, lockout, and Kerberos settings',
        created,
        modified: created,
        settings: {
          passwordMinLength: 12,
          passwordComplexity: true,
          lockoutThreshold: 5,
          lockoutDurationMinutes: 30,
          auditLogonEvents: true
        }
      },
      {
        id: 'gpo-default-dc',
        name: 'Default Domain Controllers Policy',
        status: 'enabled',
        linkedOus: ['OU=Domain Controllers,DC=contoso,DC=local'],
        enforced: true,
        description: 'DC audit and user rights',
        created,
        modified: created,
        settings: {
          auditAccountLogon: true,
          auditDirectoryService: true,
          restrictAnonymous: true
        }
      },
      {
        id: 'gpo-workstation',
        name: 'Workstation Hardening',
        status: 'enabled',
        linkedOus: ['OU=Workstations,DC=contoso,DC=local', 'OU=Laptops,DC=contoso,DC=local'],
        enforced: false,
        description: 'Firewall, BitLocker, and Windows Update',
        created,
        modified: created,
        settings: {
          bitlockerRequired: true,
          firewallEnabled: true,
          windowsUpdate: 'WSUS'
        }
      },
      {
        id: 'gpo-laps',
        name: 'LAPS Policy',
        status: 'enabled',
        linkedOus: ['OU=Workstations,DC=contoso,DC=local', 'OU=Laptops,DC=contoso,DC=local', 'OU=Servers,DC=contoso,DC=local'],
        enforced: true,
        description: 'Local Administrator Password Solution',
        created,
        modified: created,
        settings: {
          passwordLength: 16,
          passwordAgeDays: 30,
          complexity: true
        }
      }
    ],
    shares: [
      {
        id: 'sh-dept',
        name: 'Departments',
        path: '\\\\FS01\\Departments',
        server: 'FS01',
        description: 'Department file shares',
        permissions: [
          { principal: 'g-finance', access: 'modify' },
          { principal: 'g-eng', access: 'modify' },
          { principal: 'g-da', access: 'full' }
        ],
        hidden: false,
        created
      },
      {
        id: 'sh-it',
        name: 'IT$',
        path: '\\\\FS01\\IT$',
        server: 'FS01',
        description: 'Hidden IT share',
        permissions: [{ principal: 'g-da', access: 'full' }],
        hidden: true,
        created
      },
      {
        id: 'sh-public',
        name: 'Public',
        path: '\\\\FS01\\Public',
        server: 'FS01',
        description: 'Company public files',
        permissions: [
          { principal: 'g-domain-users', access: 'read' },
          { principal: 'g-da', access: 'full' }
        ],
        hidden: false,
        created
      }
    ],
    laps: [
      {
        id: 'laps-wks-042',
        computerId: 'c-wks-042',
        computerName: 'WKS-042',
        account: 'Administrator',
        password: 'Kx7!mP2qR9sT4vW1',
        expiration: new Date(Date.now() + 14 * 86400000).toISOString(),
        lastRotated: created
      },
      {
        id: 'laps-lap-018',
        computerId: 'c-lap-018',
        computerName: 'LAP-018',
        account: 'Administrator',
        password: 'Nz3#bL8cY1dH6jK2',
        expiration: new Date(Date.now() + 21 * 86400000).toISOString(),
        lastRotated: created
      },
      {
        id: 'laps-fs01',
        computerId: 'c-fs01',
        computerName: 'FS01',
        account: 'Administrator',
        password: 'Qv5$tF0wE4xU9iA7',
        expiration: new Date(Date.now() + 7 * 86400000).toISOString(),
        lastRotated: created
      }
    ],
    audit: [
      {
        id: 'a-1',
        time: created,
        actor: 'system',
        action: 'seed',
        targetType: 'store',
        target: 'initial',
        detail: 'Directory store initialized with sample objects',
        result: 'success'
      }
    ]
  }
}

function ensureDir() {
  const dir = path.dirname(DATA_FILE)
  if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true })
}

export function loadStore() {
  ensureDir()
  if (!fs.existsSync(DATA_FILE)) {
    const data = seed()
    fs.writeFileSync(DATA_FILE, JSON.stringify(data, null, 2))
    return data
  }
  return JSON.parse(fs.readFileSync(DATA_FILE, 'utf8'))
}

export function saveStore(data) {
  ensureDir()
  fs.writeFileSync(DATA_FILE, JSON.stringify(data, null, 2))
}

export function audit(store, entry) {
  store.audit.unshift({
    id: id('a'),
    time: now(),
    result: 'success',
    ...entry
  })
  if (store.audit.length > 500) store.audit.length = 500
}

export { now, id }
