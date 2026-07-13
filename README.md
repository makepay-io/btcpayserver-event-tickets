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
- Mutually exclusive Google Tag Manager or direct Google Analytics 4 integration with consent controls, Do Not Track support, CSP allowlisting, and normalized ecommerce events throughout the ticket funnel.
- MakePay.io promotion area for decentralized acceptance of 90+ currencies.

## Analytics setup

Open **Event Tickets → Settings → Analytics & ecommerce events**, then choose either Google Tag Manager or Google Analytics 4 and enter the matching `GTM-…` container ID or `G-…` measurement ID. Selecting one provider avoids duplicate GA reporting. Consent is required by default, Google scripts are not loaded before approval, revocation reloads into a Google-script-free state, and browser Do Not Track is respected by default.

Every public page initializes `window.dataLayer` and `window.makePayAnalytics` even when Google is disabled, so a self-hosted integration can consume the same stable contract. Events clear stale ecommerce state before pushing and include a `makepay` context with `plugin: "event_tickets"`, the store ID, and schema version `1`.

Standard events include:

- Sanitized `page_context` and `page_view`, plus `view_item_list`, `select_item`, and `view_item` while browsing events and ticket types.
- `add_to_cart`, `view_cart`, and best-effort browser-deduplicated `begin_checkout` as a reservation moves through the cart and attendee form.
- Best-effort browser-deduplicated `add_payment_info` when the BTCPay modal opens or the protected invoice link is used.
- `purchase` only after the order is paid, with best-effort browser deduplication plus a stable one-way analytics transaction ID for GA4 deduplication, ISO currency, value, coupon, and ticket-type line items.

The integration never includes buyer or attendee names, email addresses, phone numbers, companies, countries, ticket codes, invoice IDs, access tokens, payment addresses, raw order IDs, or checkout query parameters. Direct GA4 page locations mask dynamic checkout/order capability segments, and referrers receive the same treatment. Do Not Track disables both Google and local data-layer collection when enabled.

### GTM page-view safety

The configured GTM container is merchant-controlled JavaScript and can technically read the browser URL. Checkout and order URLs contain protected capability parameters, so do not use an **All Pages** automatic pageview trigger or GTM's browser **Page URL** variable on those routes. Instead, trigger page tracking from MakePay's `page_view` data-layer event and map its top-level `page_location`, `page_path`, and `page_referrer` values. These remove query strings/fragments and mask checkout/order identifiers. Every ecommerce event carries the same safe page fields. Direct GA4 disables automatic pageviews and sends the sanitized values itself.

## Wallet setup

Apple Wallet requires a Pass Type Identifier, Apple Team Identifier, and its `.p12` signing certificate/password. Google Wallet requires an issuer ID, an existing Generic Pass class ID, and service-account JSON authorized for that issuer. Wallet secrets are encrypted with BTCPay Server data protection.

## Build and test

```bash
git submodule update --init --recursive
dotnet test -c Release -p:RazorCompileOnBuild=true
```

Requires BTCPay Server 2.3.5 or newer and .NET 8.

Created by [MakePay.io](https://makepay.io) — accept 90+ currencies in a decentralized way in BTCPay Server.
