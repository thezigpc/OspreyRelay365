# Osprey Relay for M365

**Osprey Relay for M365** is a lightweight Windows relay that accepts email and file uploads from any device on your network — printers, copiers, scanners, line-of-business apps, monitoring systems — and delivers them through **Microsoft 365 via the Graph API**, without requiring legacy SMTP AUTH or per-device licences.

> Part of the **Osprey Relay** product family.

---

## Why Osprey Relay for M365?

Microsoft 365 deprecated basic SMTP AUTH for many tenants. Devices that relied on it (MFPs, legacy apps, monitoring tools) stopped being able to send email. The typical workarounds — direct send, shared mailboxes, third-party relays — all have drawbacks.

Osprey Relay for M365 runs locally as a Windows Service or desktop app, presents plain SMTP and FTP listeners to your devices, and uses a registered Azure AD application to deliver mail and files through Graph — no per-seat licences, no open relay, no cloud subscription.

---

## Features

- **SMTP listener** on a configurable port (default 2525); supports optional SMTP AUTH
- **Microsoft 365 delivery** via Microsoft Graph — small messages use `sendMail`; messages over 3.5 MB automatically switch to a draft-and-send path with chunked attachment upload, handling large files up to the Graph API limit
- **FTP bridge** — accepts FTP uploads from copiers, scanners, and legacy devices (default port 2121, passive mode only); routes uploaded files directly to OneDrive or SharePoint via the same path-variable rules engine; per-device credentials or accept-any-login for trusted LAN deployments
- **Flexible routing rules** — match by sender address (regex), recipient address (exact or regex), recipient domain suffix, or email subject (regex); five match modes in total
- **OneDrive / SharePoint file storage** — save attachments directly to a drive path with rich `%variable%` filename and folder templates; per-rule option to also save embedded inline images
- **Suffix domain routing** — catch all mail for `*.yourdomain.com` subdomains and route accordingly
- **Smarthost routing rule** — route specific senders or domains directly to an SMTP smarthost (intentional, not just failover); per-rule or global smarthost config
- **Suffix strip / delivery override** — strip the suffix segment from the recipient address before delivery, or redirect to a completely different address; optional To: header rewrite for smarthost routes
- **Smarthost failover** — if Graph is temporarily unreachable (503/504), automatically failover to a configured SMTP smarthost so nothing is lost
- **External sender fallback** — when the envelope-from is not a tenant mailbox, delivery automatically re-sends via the configured fallback sender address
- **Windows Service mode** — runs unattended via the Windows Service Control Manager
- **Setup Wizard** — guided Azure AD app registration with admin consent flow, or manual credential entry; existing registrations can be updated to add new permissions without re-creating the app
- **Relay Settings** — port, max message size, bind address, fallback sender, SMTP auth, and smarthost all in one place
- **Test Send** — built-in tool to fire a test message (with optional file attachment) and verify end-to-end routing; saves the last test template for quick re-use

---

## Requirements

| Requirement | Detail |
|---|---|
| OS | Windows 10 / 11 or Windows Server 2019+ |
| Runtime | None (self-contained build) — or [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) for the smaller framework-dependent build |
| Microsoft 365 | Any plan that includes Exchange Online |
| Azure AD | Permission to register an app and grant admin consent for the required Graph application permissions |

---

## Getting Started

### 1. Build / install

Download the latest release from the [Releases](https://github.com/thezigpc/OspreyRelay/releases) page.

The release executable is `OspreyRelay365.exe`. Run it directly as a desktop app, or install it as a Windows Service from within the app.

To build from source:

```powershell
.\publish.ps1
```

The output lands in `.\publish\OspreyRelay365.exe`.

> **Upgrading from v0.1.8 or earlier:** The configuration folder was renamed from `%ProgramData%\365Relay` to `%ProgramData%\OspreyRelay365`. Rename the folder before launching the new version so your existing settings are preserved.

### 2. Configure — App Registration

On first run, click **Configure App**. The wizard will either:

- **Walk you through** signing in as a Global Admin and automatically creating an Azure AD app registration with the correct permissions, or
- Accept **manual credentials** (Tenant ID, Client ID, Client Secret) if you've already registered the app yourself.

The required Graph **application permissions** (not Delegated) depend on which features you use:

| Permission | Required when |
|---|---|
| `Mail.Send` | Always — email relay via Exchange Online |
| `Mail.ReadWrite` | Large file relay — messages over 3.5 MB (creates and sends drafts) |
| `Files.ReadWrite.All` | File routing rules or FTP bridge targeting **OneDrive** |
| `Sites.ReadWrite.All` | File routing rules or FTP bridge targeting **SharePoint** |

All permissions require **admin consent** in your Azure AD tenant.

> **Upgrading from an earlier version:** If you already have an app registration from a previous release, open the Setup Wizard and click **Update Permissions**. The wizard will add any missing permissions to your existing registration without requiring you to re-create it.

> **Scoping SharePoint access:** `Sites.ReadWrite.All` grants the registered app read/write access to every site collection in your tenant. To restrict delivery to specific sites only, use the `Sites.Selected` permission instead and grant per-site access via the SharePoint Admin Centre or PowerShell (`Grant-PnPAzureADAppSitePermission`).

### 3. Configure — Relay Settings

Click **Settings** to set:

- SMTP listener port and optional bind address
- Maximum message size
- Fallback sender address (used when the original envelope-from has no M365 mailbox)
- Optional SMTP AUTH (username / password) for devices that must authenticate
- Smarthost failover host and credentials

### 4. Add routing rules

Use **Rules** to define what happens to each message. Rules are evaluated in order — the first match wins. If no rule matches, the message is delivered using the configured fallback sender.

### 5. Configure — FTP Bridge (v0.1.8+)

Click **FTP Bridge** to configure the FTP listener for scanners, copiers, and legacy devices that speak FTP but not SMTP.

#### General settings

| Setting | Default | Notes |
|---|---|---|
| Enable FTP bridge | Off | Starts the FTP listener alongside SMTP when the relay is running |
| Accept any login | Off | When on, any username/password is accepted — no user list required; suitable for trusted LAN-only deployments |
| Port | 2121 | Port 21 requires elevated privileges on Windows; 2121 avoids that |
| Bind address | 0.0.0.0 | Restrict to a specific NIC if needed |
| Passive ports | 50000–50100 | Range must be open in your firewall for data connections |

#### Users

Add one entry per device. Each entry has a username, a password (stored DPAPI-encrypted), and an optional **"Accept any password"** toggle per user — useful for devices that send fixed but non-configurable credentials.

If **Accept any login** is on at the global level, the Users list is bypassed entirely.

#### Rules

FTP routing rules work similarly to email routing rules:

| Field | Purpose |
|---|---|
| Virtual path prefix | The FTP directory the device uploads into (e.g. `/Invoices`). Use `/` to match all paths. Longest prefix wins. |
| Username | Restrict this rule to a specific FTP user. Leave blank to match any authenticated user. |
| Destination | OneDrive or SharePoint |
| OneDrive user UPN | The mailbox whose OneDrive receives the file. Leave blank to resolve automatically from the FTP login username — the username must then be in UPN form (`user@domain.com`). |
| Folder path | Destination folder. Supports `%username%`, `%date%`, `%datetime%`, `%ftppath%`. |
| Filename template | Optional rename template. Supports `%filename%`, `%date%`, `%username%`. Blank = keep original filename. |

**Match priority:** user-specific rules beat wildcard rules; among rules of the same type, the longest virtual path prefix wins.

**Typical minimal setup:**
1. Add one FTP user (the credentials configured on the device), or enable **Accept any login**.
2. Add one rule: virtual path `/`, username blank, OneDrive destination, folder path `/Scans/%date%`.
3. Point the device's FTP settings to the relay server IP, port 2121.

---

## Architecture

```
Device / App
    │  SMTP (port 2525)          FTP device / MFP
    │                                 │  FTP (port 2121)
    ▼                                 ▼
Osprey Relay for M365
    ├── SmtpRelayServer      — accepts SMTP connections
    ├── FtpRelayServer       — accepts FTP connections (passive mode)
    ├── RoutingEngine        — evaluates SMTP routing rules (5 match modes)
    ├── FtpFileRouter        — evaluates FTP routing rules (path + username)
    ├── GraphMailSender      — delivers via Microsoft Graph (sendMail / draft+send)
    ├── GraphFileStorer      — saves files to OneDrive / SharePoint
    └── SmtpSmarthostSender  — direct smarthost delivery or Graph failover
```

---

## Osprey Relay Product Family

| Product | Target | Status |
|---|---|---|
| **Osprey Relay for M365** | Microsoft 365 / Exchange Online | This repo |
| **Osprey Relay for Workspace** | Google Workspace / Gmail | Planned |
| **Osprey Relay Edge** | SMB / FTP to Cloud storage | Planned |

---

## Licence

Proprietary — all rights reserved. This software is not open-source.
