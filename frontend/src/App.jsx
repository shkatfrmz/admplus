import { NavLink, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { api } from './api.js'
import { Icon } from './icons.jsx'
import Dashboard from './pages/Dashboard.jsx'
import Users from './pages/Users.jsx'
import Computers from './pages/Computers.jsx'
import Groups from './pages/Groups.jsx'
import Gpos from './pages/Gpos.jsx'
import Shares from './pages/Shares.jsx'
import Laps from './pages/Laps.jsx'
import Audit from './pages/Audit.jsx'
import Reports from './pages/Reports.jsx'
import Settings from './pages/Settings.jsx'
import Logs from './pages/Logs.jsx'
import OuBrowser from './pages/OuBrowser.jsx'
import Hybrid from './pages/Hybrid.jsx'
import JitAdmin from './pages/JitAdmin.jsx'
import PasswordPolicy from './pages/PasswordPolicy.jsx'
import BitLocker from './pages/BitLocker.jsx'
import StaleCleanup from './pages/StaleCleanup.jsx'
import BulkExport from './pages/BulkExport.jsx'
import SecurityHealth from './pages/SecurityHealth.jsx'
import Operators from './pages/Operators.jsx'

const coreLinks = [
  { to: '/', label: 'Dashboard', ico: 'dashboard' },
  { to: '/users', label: 'Users', ico: 'users' },
  { to: '/computers', label: 'Computers', ico: 'computers' },
  { to: '/groups', label: 'Groups', ico: 'groups' },
  { to: '/ous', label: 'OU browser', ico: 'tree' },
  { to: '/gpos', label: 'Group Policy', ico: 'gpo' },
  { to: '/shares', label: 'Share Folders', ico: 'shares' },
  { to: '/laps', label: 'LAPS', ico: 'laps' }
]

const featureLinks = [
  { to: '/hybrid', label: 'Hybrid identity', ico: 'cloud' },
  { to: '/jit', label: 'JIT admin', ico: 'clock' },
  { to: '/password-policy', label: 'Password policy', ico: 'key' },
  { to: '/bitlocker', label: 'BitLocker', ico: 'lock' },
  { to: '/stale', label: 'Stale cleanup', ico: 'trash' },
  { to: '/export', label: 'Bulk export', ico: 'download' },
  { to: '/security', label: 'Security health', ico: 'shield' },
  { to: '/operators', label: 'Operators', ico: 'users' },
  { to: '/audit', label: 'Auditing', ico: 'audit' },
  { to: '/logs', label: 'Logs', ico: 'audit' },
  { to: '/reports', label: 'Reports', ico: 'reports' }
]

export default function App() {
  const [conn, setConn] = useState({ dc: false, azure: false, host: '' })
  const [q, setQ] = useState('')
  const loc = useLocation()
  const nav = useNavigate()

  useEffect(() => {
    api.dashboard().then((d) => {
      setConn({
        dc: Boolean(d.connections?.domainController?.connected),
        azure: Boolean(d.connections?.azureAd?.connected),
        host: d.connections?.domainController?.host || d.connections?.domainController?.domain || ''
      })
    }).catch(() => {})
  }, [loc.pathname])

  function goSearch(e) {
    e.preventDefault()
    const term = q.trim()
    if (!term) return
    nav(`/users?q=${encodeURIComponent(term)}`)
  }

  const today = new Date().toLocaleDateString(undefined, { month: 'short', day: '2-digit' })

  return (
    <div className="app">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark"><span>A</span></div>
          <div>
            <h1>Admplus</h1>
            <p>Directory console</p>
          </div>
        </div>
        <nav className="nav-section">
          {coreLinks.map((l) => (
            <NavLink key={l.to} to={l.to} end={l.to === '/'} className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}>
              <span className="nav-ico"><Icon name={l.ico} /></span>
              {l.label}
            </NavLink>
          ))}
        </nav>
        <div className="nav-label">Advanced</div>
        <nav className="nav-section">
          {featureLinks.map((l) => (
            <NavLink key={l.to} to={l.to} className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}>
              <span className="nav-ico"><Icon name={l.ico} /></span>
              {l.label}
            </NavLink>
          ))}
        </nav>
        <div className="sidebar-spacer" />
        <nav className="nav-section">
          <NavLink to="/audit" className="nav-link">
            <span className="nav-ico"><Icon name="bell" /></span>
            Notifications
          </NavLink>
          <NavLink to="/settings" className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}>
            <span className="nav-ico"><Icon name="settings" /></span>
            Settings
          </NavLink>
        </nav>
        <div className="promo">
          <h4>Connect your forest</h4>
          <p>{conn.dc ? (conn.host || 'Domain controller online') : 'Bind a DC to manage live AD objects.'}</p>
          <button className="btn" onClick={() => nav('/settings')}>{conn.dc ? 'Manage connection' : 'Open settings'}</button>
        </div>
        <div style={{ marginTop: 10 }}>
          <div className="conn-row"><span className={`dot ${conn.dc ? 'on' : 'off'}`} /> Domain controller</div>
          <div className="conn-row"><span className={`dot ${conn.azure ? 'on' : 'off'}`} /> Azure AD / Entra ID</div>
        </div>
      </aside>
      <div className="shell">
        <header className="topbar">
          <form className="top-search" onSubmit={goSearch}>
            <Icon name="search" size={16} />
            <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search users, computers, groups..." />
          </form>
          <div className="top-tools">
            <span className="chip">{today}</span>
            <span className="chip">24h</span>
            <button className="icon-btn" type="button" onClick={() => window.location.reload()} title="Refresh">
              <Icon name="refresh" size={16} />
            </button>
            <button className="icon-btn" type="button" title="Alerts" onClick={() => nav('/audit')}>
              <Icon name="bell" size={16} />
            </button>
            <div className="avatar">AD</div>
          </div>
        </header>
        <main className="main">
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/users" element={<Users />} />
            <Route path="/computers" element={<Computers />} />
            <Route path="/groups" element={<Groups />} />
            <Route path="/ous" element={<OuBrowser />} />
            <Route path="/gpos" element={<Gpos />} />
            <Route path="/shares" element={<Shares />} />
            <Route path="/laps" element={<Laps />} />
            <Route path="/hybrid" element={<Hybrid />} />
            <Route path="/jit" element={<JitAdmin />} />
            <Route path="/password-policy" element={<PasswordPolicy />} />
            <Route path="/bitlocker" element={<BitLocker />} />
            <Route path="/stale" element={<StaleCleanup />} />
            <Route path="/export" element={<BulkExport />} />
            <Route path="/security" element={<SecurityHealth />} />
            <Route path="/operators" element={<Operators />} />
            <Route path="/audit" element={<Audit />} />
            <Route path="/logs" element={<Logs />} />
            <Route path="/reports" element={<Reports />} />
            <Route path="/settings" element={<Settings />} />
          </Routes>
        </main>
      </div>
    </div>
  )
}
