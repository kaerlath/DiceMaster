# Relay operator setup

Normal players use the preconfigured relay and do not need these steps.

To host a separate service, install Node.js and pnpm, then run `pnpm install` from `relay`. Review `wrangler.jsonc`, choose a unique Worker name, authorize with `pnpm exec wrangler login`, and deploy with `pnpm exec wrangler deploy`. The SQLite Durable Object migration initializes the authoritative state store.

Generate a random 32-byte operator key encoded as 64 lowercase hexadecimal characters. Store it in a password manager and use `pnpm exec wrangler secret put ADMIN_KEY` to supply it through Wrangler's secret prompt. Never commit the operator key, GM credentials, OAuth tokens or local state.

On your operator machine, set `DM_RELAY_URL` to the HTTPS relay origin and `DM_ADMIN_KEY` to the operator key in the process environment. Issue individual credentials using `node tools/admin.mjs issue "GM name"`. The new credential is printed once; deliver it privately and retain its ID for revocation. `node tools/admin.mjs revoke <id>` invalidates that credential and its sessions. `node tools/admin.mjs audit` reads the private audit ring.

Use the hosted origin in the plugin's Connection tab. Saved credentials are origin-bound and will not be sent to a different relay. Updating the Worker preserves its existing secrets and Durable Object data unless you explicitly remove them. GM assignments are scoped to participant sessions and room lifetimes.

Before broadly advertising a public deployment, review the single-authority scaling limits and configure account-level abuse controls appropriate to your audience. This repository does not provision billing plans, secrets or Cloudflare accounts automatically.
