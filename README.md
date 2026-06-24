# Osprey Relay for M365

**Osprey Relay for M365** is a lightweight Windows SMTP relay that accepts email from any device on your network — printers, copiers, line-of-business apps, monitoring systems — and delivers it through **Microsoft 365 via the Graph API**, without requiring legacy SMTP AUTH or per-device licences.

> Part of the **Osprey Relay** product family.

---

## Why Osprey Relay for M365?

Microsoft 365 deprecated basic SMTP AUTH for many tenants. Devices that relied on it (MFPs, legacy apps, monitoring tools) stopped being able to send email. The typical workarounds — direct send, shared mailboxes, third-party relays — all have drawbacks.

Osprey Relay for M365 runs locally as a Windows Service or desktop app, presents a plain SMTP listener to your devices, and uses a registered Azure AD application to deliver mail through Graph — no per-seat licences, no open relay, no cloud subscription.

---

## Features

- **SMTP listener** on a configurable port (default 2525); supports optional SMTP AUTH
- **Microsoft 365 delivery** via Microsoft Graph — small messages use `sendMail`; messages over 3.5 MB automatically switch to a draft-and-send path with chunked attachment upload, handling large files up to the Graph API limit
- **Flexible routing rules** — match by sender address (regex), recipient address (exact or regex), recipient domain suffix, or email subject (regex); five match modes in total
- **OneDrive / SharePoint file storage** — save attachments directly to a drive path with rich `%variable%` filename templates; per-rule option to also save embedded inline images
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

Download the latest release from the [Releases](https://github.com/thezigpc/OspreyRelay365/releases) page. Two builds are available:

| Build | File | Requirement |
|---|---|---|
| Self-contained | `Relay365.exe` | No .NET install needed — runs anywhere |
| Framework-dependent | `Relay365-fdd.exe` | Requires [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64); smaller download |

To build from source:

```powershell
dotnet publish src/Relay365/Relay365.csproj -p:PublishProfile=win-x64
```

The output lands in `src\Relay365\publish\`. Run `Relay365.exe` directly, or install as a Windows Service.

### 2. Configure — App Registration

On first run, click **Configure App**. The wizard will either:

- **Walk you through** signing in as a Global Admin and automatically creating an Azure AD app registration with the correct permissions, or
- Accept **manual credentials** (Tenant ID, Client ID, Client Secret) if you've already registered the app yourself.

The required Graph **application permissions** (not Delegated) depend on which features you use:

| Permission | Required when |
|---|---|
| `Mail.Send` | Always — email relay via Exchange Online |
| `Mail.ReadWrite` | Large file relay — messages over 3.5 MB (creates and sends drafts) |
| `Files.ReadWrite.All` | File routing rules targeting **OneDrive** |
| `Sites.ReadWrite.All` | File routing rules targeting **SharePoint** |

All permissions require **admin consent** in your Azure AD tenant.

> **Upgrading from an earlier version:** If you already have an app registration from a previous release, open the Setup Wizard and click **Update Permissions**. The wizard will add any missing permissions (such as `Mail.ReadWrite` added in v0.1.7) to your existing registration without requiring you to re-create it.

> **Scoping SharePoint access:** `Sites.ReadWrite.All` grants the registered app read/write access to every site collection in your tenant. To restrict delivery to specific sites only, use the `Sites.Selected` permission instead and grant per-site access via the SharePoint Admin Centre or PowerShell (`Grant-PnPAzureADAppSitePermission`). Support for managing site access through **security groups** directly within Osprey Relay is planned for a future release.

### 3. Configure — Relay Settings

Click **Settings** to set:

- SMTP listener port and optional bind address
- Maximum message size
- Fallback sender address (used when the original envelope-from has no M365 mailbox)
- Optional SMTP AUTH (username / password) for devices that must authenticate
- Smarthost failover host and credentials

### 4. Add routing rules

Use **Rules** to define what happens to each message. Rules are evaluated in order — the first match wins. If no rule matches, the message is delivered using the configured fallback sender.

---

## Architecture

```
Device / App
    │  SMTP (port 2525)
    ▼
Osprey Relay for M365
    ├── SmtpRelayServer      — accepts SMTP connections
    ├── RoutingEngine        — evaluates routing rules (5 match modes)
    ├── GraphMailSender      — delivers via Microsoft Graph (sendMail / draft+send)
    ├── GraphFileStorer      — saves attachments to OneDrive / SharePoint
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
