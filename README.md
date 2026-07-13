# MakePay Event Tickets for BTCPay Server

A self-hosted event storefront, ticket delivery system, POS, and QR check-in workflow that turns paid BTCPay invoices into customer tickets.

## Features

- Branded responsive event shop with editable logo, hero artwork, colors, typography, copy, and a fullscreen live editor covering every checkout state.
- Multi-ticket cart, promotional pricing, buyer and attendee details, reservation countdowns, and the official BTCPay JavaScript modal (`/modal/btcpay.js` + `showInvoice`) with invoices created server-side.
- Protected post-payment order pages with downloadable multi-page PDF tickets, one real QR code per attendee, and the same secure link delivered by email.
- Multiple ticket types, capacity controls, quantity limits, draft/published events, and an event-specific POS view.
- Atomic inventory reservations and automatic release when reservations or invoices expire or become invalid.
- Cryptographically random ticket codes stored as one-way hashes with encrypted recoverable values.
- Browser camera QR scanner using `BarcodeDetector`, manual scanner input fallback, atomic first check-in, and duplicate/revoked responses.
- Adjustable HTML email through Resend or BTCPay store SMTP, with an attached standards-compliant PDF ticket document.
- Apple Wallet `.pkpass` generation with merchant-provided Pass Type certificate and Google Wallet signed save links with a service account.
- Configurable privacy/terms links, attendee notice, optional phone/country/company fields, confirmation copy, and Resend or SMTP delivery templates.
- MakePay.io promotion area for decentralized acceptance of 90+ currencies.

## Wallet setup

Apple Wallet requires a Pass Type Identifier, Apple Team Identifier, and its `.p12` signing certificate/password. Google Wallet requires an issuer ID, an existing Generic Pass class ID, and service-account JSON authorized for that issuer. Wallet secrets are encrypted with BTCPay Server data protection.

## Build and test

```bash
git submodule update --init --recursive
dotnet test -c Release -p:RazorCompileOnBuild=true
```

Requires BTCPay Server 2.3.5 or newer and .NET 8.

Created by [MakePay.io](https://makepay.io) — accept 90+ currencies in a decentralized way in BTCPay Server.
