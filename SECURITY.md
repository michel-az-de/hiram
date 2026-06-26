# Security Policy

## Reporting a vulnerability

Please do not open a public issue for a security vulnerability.

Report it privately through GitHub Security Advisories on this repository, or by email to felipe.azevedoit@gmail.com. Include enough detail to reproduce the issue: affected endpoint or component, steps, and impact.

You can expect an acknowledgement within a few days. There is no bug bounty; this is a best effort, good faith process.

## Supported versions

Security fixes target the `main` branch. There are no long term support branches yet.

## Scope notes

- Tenant secrets and webhook signing keys are encrypted at rest with ASP.NET Data Protection. Provider and VAPID private keys live in configuration, never in the database or logs.
- Webhook callbacks are signed with HMAC-SHA256 in the `X-Hiram-Signature` header so receivers can verify authenticity.
