# Osprey Relay

**Osprey Relay** is a lightweight Windows relay that accepts email and file uploads from devices on your network — printers, copiers, scanners, line-of-business apps, monitoring systems — and delivers them through cloud services, without requiring legacy SMTP AUTH or per-device licences.

Two products are available, each targeting a different cloud platform:

| Product | Cloud platform | Executable |
|---|---|---|
| **Osprey Relay for M365** | Microsoft 365 / Exchange Online / OneDrive / SharePoint | `OspreyRelay365.exe` |
| **Osprey Relay for Workspace** | Google Workspace / Gmail / Google Drive | `OspreyRelayWorkspace.exe` |

Both ship from this repository. Each installs as its own Windows Service with its own config path and can run side-by-side on the same server.

---

## Osprey Relay for M365

### Why

Microsoft 365 deprecated basic SMTP AUTH for many tenants. Devices that relied on it — MFPs, legacy apps, monitoring tools — stopped being able to send email. The typical workarounds (direct send, shared mailboxes, third-party relays) all have drawbacks.

Osprey Relay for M365 runs locally, presents a plain SMTP and FTP listener to your devices, and uses a registered Azure AD application to deliver mail and files through the Microsoft Graph API — no per-seat licences, no open relay, no cloud subscription.

### Features

- **SMTP listener** on a configurable port (default 2525); optional SMTP AUTH
- **Microsoft 365 delivery** via Graph — small messages use `sendMail`; messages over 3.5 MB switch to a draft-and-send path with chunked attachment upload
- **OneDrive / SharePoint file storage** — route attachments directly to a drive path using `%variable%` filename and folder templates
- **FTP bridge** — accepts FTP uploads from copiers, scanners, and legacy devices; routes directly to OneDrive or SharePoint via the same rules engine
- **Flexible routing rules** — match by recipient domain suffix, exact address, or regex on To/From/Subject; five match modes
- **Smarthost routing** — route specific senders or domains to a direct SMTP smarthost (intentional, not just failover); per-rule or global config
- **Smarthost failover** — if Graph is temporarily unreachable, automatically fail over to a configured smarthost
- **External sender fallback** — re-sends via a fallback mailbox when the envelope-from has no M365 account
- **Setup Wizard** — guided Azure AD app registration with admin consent flow, or manual credential entry

### Requirements

| | |
|---|---|
| OS | Windows 10 / 11 or Windows Server 2019+ |
| Runtime | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) x64 |
| Microsoft 365 | Any plan that includes Exchange Online |
| Azure AD | Permission to register an app and grant admin consent |

### Getting Started

#### 1. Install

Download `OspreyRelay365.exe` from the [Releases](https://github.com/thezigpc/OspreyRelay/releases) page. Run it directly as a desktop app, or install it as a Windows Service from within the app.

> **Upgrading from v0.1.8 or earlier:** rename `%ProgramData%\365Relay` → `%ProgramData%\OspreyRelay365` before launching.

#### 2. App Registration

On first run, click **Configure App**. The wizard walks you through creating (or entering) an Azure AD app registration.

Required Graph **application permissions** (not Delegated):

| Permission | Required when |
|---|---|
| `Mail.Send` | Always — email relay |
| `Mail.ReadWrite` | Large file relay (> 3.5 MB) |
| `Files.ReadWrite.All` | Rules or FTP targeting **OneDrive** |
| `Sites.ReadWrite.All` | Rules or FTP targeting **SharePoint** |

All permissions require admin consent. If you already have a registration from an earlier release, click **Update Permissions** in the wizard to add any new permissions without re-creating the app.

> **Scoping SharePoint access:** `Sites.ReadWrite.All` grants access to every site collection. To restrict to specific sites, use `Sites.Selected` and grant per-site access via the SharePoint Admin Centre or `Grant-PnPAzureADAppSitePermission`.

#### 3. Settings

Click **Settings** to configure:

- SMTP port, bind address, max message size
- Fallback sender address
- Optional SMTP AUTH
- Smarthost failover host and credentials
- **Services tab** — enable/disable Exchange Online relay, OneDrive, and/or SharePoint independently (e.g. OneDrive-only for Apps for Business tenants with no SharePoint)

#### 4. Routing Rules

Click **Rules** to define what happens to each message. Rules are evaluated in order — first match wins.

#### 5. FTP Bridge

Click **FTP Bridge** to configure the FTP listener for devices that speak FTP but not SMTP.

| Setting | Default | Notes |
|---|---|---|
| Port | 2121 | Port 21 requires elevation; 2121 avoids that |
| Accept any login | Off | For trusted LAN-only deployments |
| Passive ports | 50000–50100 | Must be open in your firewall |

FTP rules match on virtual path prefix and optional username. Destination is OneDrive or SharePoint. Folder path supports `%username%`, `%date%`, `%datetime%`, `%ftppath%`.

---

## Osprey Relay for Workspace

### Why

Printers, scanners, copiers, and legacy applications speak SMTP and FTP — not the Gmail API or the Drive API. Osprey Relay for Workspace bridges that gap: it accepts standard SMTP and FTP connections from your devices and delivers email through Gmail and files to Google Drive, using a **service account with Domain-Wide Delegation** to act on behalf of your users without storing per-user credentials anywhere on the relay.

### Features

- **Gmail relay** — delivers email via the Gmail API, sending as the original From: address; falls back to a configured sender address if the From address isn't a mailbox in your domain
- **Google Drive file storage** — routes attachments to My Drive or any Shared Drive, into folder paths built from `%variable%` templates resolved at delivery time
- **FTP bridge** — accepts FTP uploads from copiers and legacy devices and routes files directly to Google Drive via the same rules engine used for email
- **Service account auth** — a single JSON key file covers all users; no per-user sign-in or OAuth consent required
- **Flexible routing rules** — match by recipient domain suffix, exact address, or regex on To/From/Subject; five match modes
- **Smarthost routing** — route specific senders or domains to a direct SMTP smarthost instead of Gmail; always available regardless of which Google services are enabled
- **Windows Service** — runs as `OspreyRelayWorkspace`; config stored at `%ProgramData%\OspreyRelayWorkspace\`

### Requirements

| | |
|---|---|
| OS | Windows 10 / 11 or Windows Server 2019+ |
| Runtime | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) x64 |
| Google Workspace | Any plan |
| Google Cloud | Service account with Domain-Wide Delegation enabled |

### Getting Started

#### 1. Install

Download `OspreyRelayWorkspace.exe` from the [Releases](https://github.com/thezigpc/OspreyRelay/releases) page. Run it directly as a desktop app, or install it as a Windows Service from within the app.

#### 2. Service Account Setup

In the **Google Cloud Console**:

1. Create a service account
2. Enable **Domain-Wide Delegation** on the account
3. Download the **JSON key file** and place it on the relay server

In the **Google Workspace Admin Console** (Security → API Controls → Domain-wide Delegation), add the service account and grant it whichever OAuth scopes match your enabled services:

| Scope | Required when |
|---|---|
| `https://www.googleapis.com/auth/gmail.send` | Gmail relay enabled |
| `https://www.googleapis.com/auth/drive` | Google Drive storage enabled |

#### 3. Workspace Setup

Click **Workspace Setup**, browse to the JSON key file, and enter an impersonation email address (any mailbox in your domain). Click **Test Connection** — it will verify credentials for whichever services are enabled in Settings → Services.

#### 4. Settings → Services

Choose which Google services this relay should use:

- **Gmail relay** — send email through the Gmail API
- **Google Drive** — store files and attachments in Drive

Disabling a service greys it out throughout the UI and removes it from routing options. If you only need file storage, disable Gmail relay and configure a Smarthost for any email delivery.

#### 5. Routing Rules

Click **Rules** to define what happens to each message. Rules are evaluated in order — first match wins. File destination options are **Google Drive – My Drive** and **Google Drive – Shared Drive**.

For Shared Drive routing, enter the Shared Drive ID (visible in the URL when browsing the drive in Google Drive) in the rule editor.

#### 6. FTP Bridge

Click **FTP Bridge** to configure the FTP listener for devices that upload via FTP.

| Setting | Default | Notes |
|---|---|---|
| Port | 2121 | Port 21 requires elevation; 2121 avoids that |
| Accept any login | Off | For trusted LAN-only deployments |
| Passive ports | 50000–50100 | Must be open in your firewall |

FTP rules match on virtual path prefix and optional username. Files are delivered to Google Drive. Folder path supports `%username%`, `%date%`, `%datetime%`, `%ftppath%`.

---

## Shared Features

Both products share the same core routing engine, FTP bridge, and path variable system.

### Path Variables

Use these in folder path and filename templates:

| Variable | Description |
|---|---|
| `%date%` | Date in `YYYY-MM-DD` format |
| `%datetime%` | Date and time in `YYYY-MM-DD_HHmmss` format |
| `%subject%` | Full email subject |
| `%subject[n]%` | nth word of subject (0-indexed) |
| `%subject[*]%` | All subject words joined by delimiter |
| `%toupn%` | Full recipient address |
| `%suffix%` | Matched suffix segment (domain suffix rules) |
| `%from%` | Full sender address |
| `%originalbasefilename%` | Original filename without extension |
| `%originalext%` | Original file extension |
| `%match1%`, `%match2%` | Numbered regex capture groups |
| `%name%` | Named regex capture group `(?<name>...)` |
| `%username%` | FTP username (FTP rules only) |
| `%ftppath%` | FTP virtual path (FTP rules only) |

### Service Flags

Settings → Services lets you enable only the services your environment actually uses. Disabling a service:

- Greys out its destination options in the rule editor and routing forms
- Removes it from the unrouted action choices
- Restricts credential/scope requests to only what's needed

All services default to enabled so existing configurations are unaffected on upgrade.

---

## Architecture

```
Devices / Apps
    │  SMTP (port 2525)          FTP devices / MFPs
    │                                 │  FTP (port 2121)
    ▼                                 ▼
┌─────────────────────────────────────────────┐
│            OspreyRelay.Core                 │
│  SmtpRelayServer   FtpRelayServer           │
│  RoutingEngine     FtpFileRouter            │
│  PathVariableResolver                       │
│  IMailSender ◄──── IFileStorer             │
└──────┬──────────────────────┬──────────────┘
       │                      │
       ▼                      ▼
OspreyRelay.M365         OspreyRelay.Workspace
GraphMailSender          GmailMailSender
GraphFileStorer          GoogleDriveFileStorer
AppRegistrationManager   WorkspaceCredentialProvider
       │                      │
       ▼                      ▼
OspreyRelay.App          OspreyRelay.WorkspaceApp
OspreyRelay365.exe       OspreyRelayWorkspace.exe
```

---

## Build

```powershell
.\publish.ps1
```

Outputs both executables to `.\publish\`:

```
publish\OspreyRelay365.exe         (Osprey Relay for M365)
publish\OspreyRelayWorkspace.exe   (Osprey Relay for Workspace)
```

Requires .NET 10 SDK. Target machines need the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).

---

## Licence

Proprietary — all rights reserved. This software is not open-source.
