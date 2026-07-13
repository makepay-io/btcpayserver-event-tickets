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
