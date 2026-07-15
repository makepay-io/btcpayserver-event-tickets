# MakePay Event Tickets for BTCPay Server

A self-hosted event storefront, ticket delivery system, POS, and QR check-in workflow that turns paid BTCPay invoices into customer tickets.

Version 1.4.0 brings the Event Tickets dashboard, settings workspace, live editor, and public event directory in line with the Digital Products experience. It also hardens invoice-backed reservation expiry, ended-event and sold-out handling, pending-order refresh, non-cacheable customer pages, consent-aware analytics replay, saved attendee details, BTCPay modal recovery, responsive behavior, and enforced MakePay attribution.

Version 1.3.1 adds a native QR-code sidebar icon that follows BTCPay's light, dark, hover, and active navigation states.

Version 1.3.0 adds native BTCPay App domain mapping, clean branded ticket URLs, and an in-product setup guide with safe DNS/TLS instructions for BTCPay Docker operators.

## Features

- Branded responsive event shop with editable logo, favicon, hero artwork, colors, typography, copy, and a fullscreen live editor covering every checkout state.
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
- Enforced BTCPay + MakePay attribution for decentralized acceptance of 90+ currencies.

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

## Storefront favicon

Set **Browser favicon URL** under **Event Tickets → Experience settings → Brand & storefront**. Use an absolute HTTP(S) URL to a square ICO, PNG, or SVG asset. The configured icon is emitted consistently on the event directory, ticket selection, cart, attendee checkout, BTCPay payment, and protected order pages; leaving the field empty emits no favicon link.

## Custom domains

The **Use your own domain** guide in Event Tickets settings shows the current canonical ticket URL, the native App identity used for mapping, a Docker command template, and links to BTCPay's official domain documentation.

DNS and TLS cannot be provisioned by the plugin: the domain owner or BTCPay server operator must point the hostname to the server, keep every existing entry when adding it to `BTCPAY_ADDITIONAL_HOSTS`, and rerun `btcpay-setup.sh`. Verify DNS before requesting the certificate, because an unresolved additional hostname can interfere with Let's Encrypt renewal for the other configured names.

`BTCPAY_ADDITIONAL_HOSTS` aliases the entire BTCPay Server, not only this store. Other BTCPay pages and store-scoped public routes remain reachable through that hostname, so it must not be treated as domain-to-store isolation.

To enable native clean routing:

1. In **Event Tickets → Experience settings**, choose **Create Event Tickets App**. This opens BTCPay's standard create-App screen; loading or saving plugin settings never creates an App as a hidden side effect.
2. Give the App a clear name. If old duplicate Event Tickets Apps already exist, keep one active identity and archive the others.
3. As a BTCPay server administrator, open **Server Settings → Policies → Domain mapping** and map the exact canonical ASCII hostname to that App. Do not include whitespace, a trailing dot, scheme, port, or path; use punycode for an internationalized hostname.
4. Verify the directory, event, cart, attendee details, embedded BTCPay payment, protected confirmation, status polling, rebuy, PDF, and Apple Wallet routes on `https://tickets.example.com/events/…`.

BTCPay supplies the mapped App ID to the plugin's clean controller through `DomainMappingConstraint`. The plugin then derives the store only from that `AppData`; a route, query, or form value named `storeId` cannot select another store. If multiple App records exist, canonical URL generation follows the App actually selected in Policies rather than assuming the oldest one.

The mapped hostname root redirects to `/events`. BTCPay deployments configured below a root path preserve it consistently, for example `https://tickets.example.com/btcpay/events`. Every generated public link uses the mapped host and clean path, including invoice return metadata, email delivery, payment polling, protected order pages, rebuy, PDFs, and wallet passes. BTCPay resolves the first exact hostname row, so duplicate rows must be removed; a later conflicting row is inactive. The authenticated QR scanner remains a store-admin route and is not exposed on the public hostname.

Existing `/stores/{storeId}/events/…` routes remain available for compatibility. Once a mapping exists, safe GET and HEAD requests redirect permanently to the canonical hostname while POST requests remain on their submitted origin, avoiding an unsafe cross-host form replay.

Onion requests never canonicalize to the clearnet mapping. Checkout records their route/origin intent, so BTCPay invoice returns and fulfillment email links remain on the onion origin even when the store also has a mapped clearnet hostname.

A dedicated ticket subdomain is recommended. If an apex domain already serves another website, that site's reverse proxy or CDN must deliberately forward `/events` and all nested public routes to BTCPay. Separate hostnames are required when Event Tickets and Digital Products should each use native clean URLs because BTCPay maps one hostname to one App.

Official references: [Docker domain settings](https://docs.btcpayserver.org/Docker/#environment-variables), [BTCPay App domain mapping](https://docs.btcpayserver.org/FAQ/Apps/#how-to-map-a-domain-name-to-an-app), and [external reverse-proxy requirements](https://docs.btcpayserver.org/FAQ/Deployment/#can-i-use-an-existing-nginx-server-as-a-reverse-proxy-with-ssl-termination).

## Build and test

```bash
git submodule update --init --recursive
dotnet test -c Release -p:RazorCompileOnBuild=true
```

Requires BTCPay Server 2.3.5 or newer and .NET 8.

Created by [MakePay.io](https://makepay.io) — accept 90+ currencies in a decentralized way in BTCPay Server.
