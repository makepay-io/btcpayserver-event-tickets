# Design QA — public ticket experience v1.1

## Evidence

- Desktop reference/implementation comparison: `.design-qa/reference-vs-implementation-3072x800.png`
- Official BTCPay JavaScript modal: `.design-qa/payment-js-modal-1536x800.png`
- Checkout states: `.design-qa/cart-1536x800.png`, `.design-qa/details-full.png`, `.design-qa/success-v110-1536x800.png`
- Fullscreen editor and responsive preview: `.design-qa/settings-editor-v110.png`, `.design-qa/settings-editor-mobile-cart-v110.png`
- Branded PDF rendered to PNG and inspected before temporary files were removed.

## Fidelity and behavior

- The desktop split, 50/50 proportions, bottom-aligned brand block, high-impact sans display type, blue/white palette, flat ticket rows, quantity controls, dividers, and disabled CTA match the supplied Coinfest reference closely while keeping merchant copy and inventory dynamic.
- The public flow uses real controls and realistic data across selection, cart, attendee details, payment, and success states. Quantity, totals, validation, countdown, invoice creation, fulfillment, QR, PDF, and protected links were exercised.
- Payment uses BTCPay's `/modal/btcpay.js` and `showInvoice(invoiceId)`; the legacy checkout iframe is absent. The only persistent iframe in the plugin is the sandboxed settings-editor preview.
- Merchant design tokens cover logo/hero imagery, five professional font stacks, panel width, colors, copy, policies, timer, confirmation, email, PDF, and attribution. Live editor state changes do not save until the merchant confirms.
- Responsive rules collapse the split layout, ticket columns, summary, buyer/attendee grids, and action rows at tablet/mobile breakpoints. The editor's mobile checkout preview was inspected for wrapping, spacing, and usable controls.
- Real BTCPay assets and BTCPay's existing checkmark asset are used; there is no decorative CSS art, emoji, placeholder illustration, or hand-drawn SVG substitution.

## Accessibility and quality

- Semantic headings, navigation labels, button labels, status regions, form labels, image alt text, disabled states, keyboard-native controls, visible focus styles, practical tap targets, and reduced-motion handling are present.
- Protected order, status, PDF, and wallet routes reject invalid access tokens with HTTP 404.
- No application-origin console errors were observed during checkout; logged errors came from a third-party Chrome wallet extension attempting duplicate provider injection.
- Release tests: 9 passed, 0 failed. The only build advisory is the upstream BTCPay Server MailKit vulnerability warning.

passed

---

# Design QA — hero containment and label cleanup v1.1.3

## Evidence

- Source visual truth: `/var/folders/70/d8rtf1ds5f14stgc4yv_4c600000gn/T/TemporaryItems/NSIRD_screencaptureui_hrDxDO/Screenshot 2026-07-13 at 4.15.49 PM.png`
- Implementation capture: `/tmp/event-tickets-headline-after-1728.png`
- Side-by-side comparison: `/tmp/event-tickets-headline-comparison.png`
- Viewport: 1728 × 891 CSS pixels, matching the supplied 3456 × 1782 Retina screenshot at 50%.
- State: public ticket selector with zero tickets selected.

## Findings

- [P1 resolved] The left-panel display headline previously scaled from the full viewport and extended past the brand column. It now scales from the panel container, stays inside the content inset, and has safe wrapping as a final guard.
- [P2 resolved] The redundant “MakePay Events” label below “MakePay Demo Events” was removed from the public page and from both settings editing surfaces.
- Typography: the existing family, weight, capitalization, line height, and three-line composition are preserved. At 1728 pixels the headline is 84.672 pixels and has zero right overflow.
- Layout: the 50/50 split, lower-left brand alignment, existing spacing system, ticket table, and controls are unchanged.
- Responsive QA: zero headline overflow and zero page-level horizontal overflow at 390 × 844; the duplicate label is absent at desktop and mobile sizes.
- Assets and colors: unchanged.

## Comparison history

1. Source: “UNFORGETTABLE” reached beyond the intended brand-column content boundary, and the brand block repeated “MakePay Events”.
2. Iteration: changed the headline from viewport-relative sizing to panel-relative container sizing while retaining the original responsive composition.
3. Follow-up: removed the duplicate label from the Razor storefront and the live-editor preview/configuration.
4. Final verification: desktop and mobile DOM measurements report zero overflow; the combined reference/implementation image was visually inspected.

## Validation

- Razor compilation and unit tests: 13 passed, 0 failed.
- Live BTCPay plugin startup: `BTCPayServer.Plugins.MakePay.EventTickets - 1.1.3.0`.
- Browser console and rendered public state: no application-visible regression observed.

final result: passed

---

# Design QA — Digital Products parity and public experience v1.4.0

## Evidence

- Supplied dashboard defect: `.design-qa/before-admin-dashboard.png`
- Fixed dashboard: `.design-qa/after-admin-dashboard-v140.png`
- Direct reference/fixed comparison: `.design-qa/reference-vs-admin-dashboard-v140.png`
- Settings before/fixed: `.design-qa/before-settings.png`, `.design-qa/after-settings-v140.png`
- Public event before/fixed: `.design-qa/before-public-event.png`, `.design-qa/after-public-event-window.png`

## Admin and settings verification

- The Event Tickets dashboard now uses BTCPay's dark surfaces, borders, typography, green actions, summary cards, searchable/filterable event management, recent orders, and native sidebar icon treatment used by Digital Products.
- The dashboard clipping defect was traced to a local `overflow-x:hidden` formatting context that blocked BTCPay's sticky-header negative-margin layout. Removing that rule eliminated the exact 80-pixel overlap: the header bottom and summary-grid top both measure 201.992 pixels at 1440 × 900.
- Desktop and 390 × 844 mobile checks report zero document-level horizontal overflow and zero header/card overlap. The four summary cards, primary actions, two management panels, table scrolling, and event actions remain usable.
- Settings expose five keyboard-accessible tabs, preserved active state, error-aware tab activation, root-path-safe BTCPay icons/assets, two save points, a responsive fullscreen editor, and visible editor controls at 800 × 768.
- Payment preview uses the server-resolved BTCPay logo path. The sandboxed preview preserves its scroll position across edits and supports Tickets, Cart, Checkout, Payment, Success, Desktop, and Mobile states.

## Public experience verification

- The storefront now presents a professional event directory with accurate live availability and lowest purchasable tier pricing, sold-out handling, branded navigation, clear event cards, and a shared enforced BTCPay/MakePay footer.
- Event selection, cart, attendee checkout, payment, completion, expired-order recovery, PDF/wallet delivery, favicon configuration, custom-domain guidance, and consent-aware GTM/GA4 ecommerce events were checked for shared theme and interaction parity.
- Invoice-backed or partially paid orders are protected from reservation expiry redirects; expired unpaid reservations offer a direct rebuy path and retain buyer/attendee form data locally.
- The storefront and event page have zero document-level overflow at 390 × 844. The public event headline has zero measured text overflow, both ticket rows render, and the enforced BTCPay and MakePay backlinks are present.

## Validation

- Release tests: 85 passed, 0 failed.
- Razor publish: passed with `RazorCompileOnBuild=true`.
- Live plugin startup: `BTCPayServer.Plugins.MakePay.EventTickets - 1.4.0.0`.
- Live HTTP checks: authenticated admin redirects correctly; public storefront returns HTTP 200.
- Browser diagnostics: no application errors; only normal BTCPay Blazor connection information was recorded.
- The only build advisory is the upstream BTCPay Server MailKit 4.8.0 NU1902 warning.

final result: passed
