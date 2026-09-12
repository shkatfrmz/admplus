# Admplus

Active Directory manager built on Microsoft technologies for backward-compatible AD operations.

- Backend: ASP.NET Core 8, `System.DirectoryServices.Protocols` (LDAP/LDAPS), `Microsoft.Identity.Client` (MSAL) for Azure AD / Entra ID
- Frontend: React + Vite (proxies `/api` to the .NET API)

Manages users (all types), computers, groups, group policy, share folders, LAPS passwords, auditing, and reports. Settings connect to a domain controller and Azure AD.

## Run

`start.sh` installs missing prerequisites (Node.js 20, .NET 8 SDK, ICU when running as root) then starts both services.

```bash
chmod +x start.sh
./start.sh
```

Frontend: http://localhost:5173
API: http://localhost:3001

## Microsoft AD stack

When a domain controller is connected, writes use LDAP operations that match classic AD tools (ADUC / ADSI):

- Users: `objectClass=user`, `unicodePwd`, `userAccountControl` (enable/disable, password never expires)
- Computers: `objectClass=computer`, `sAMAccountName` with `$`, workstation trust account
- Groups: `groupType` bits for security vs distribution and global / universal / domain local
- LAPS: reads `msLAPS-Password` (Windows LAPS) and legacy `ms-Mcs-AdmPwd`
- Azure AD: confidential client credentials against Microsoft Graph

Until a DC is connected, Admplus uses an on-disk directory store so you can evaluate the console offline.

## Modules

- Users — user, service, guest, inetOrgPerson, contact, managed service accounts
- Computers — workstations, laptops, servers, domain controllers
- Groups — security / distribution, global / universal / domain local
- Group Policy — GPO create, link, enforce, settings JSON
- Share folders — SMB paths, hidden shares, ACLs
- LAPS — enroll, reveal (audited), rotate
- Auditing — change log
- Reports — inactive users, privileged groups, LAPS gaps
- Settings — DC bind + Azure AD app credentials, test and sync
