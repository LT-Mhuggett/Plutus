# Entra External ID — Plutus IdP (Phase 9, WP9.3)

The second switchable identity provider behind the Phase-9 seam (the other is Keycloak — see
`ops/keycloak/`). Selected by `IdP:Provider=entra`. The backend code path is already live from
WP9.1 (a metadata-driven `AddJwtBearer` + the shared `RbacClaimsTransformation`); this document
is the setup to make it go live once the Azure tenant exists. **No live proof is possible here
without Matt's Azure tenant** — everything below is config-ready.

Entra External ID is the successor to Azure AD B2C (the codebase's original `AzureAdB2C`
registration remains as the `b2c` provider for backward compatibility). Both are standard OIDC,
so the same JwtBearer path serves both — only the authority shape and claim names differ, and the
seam already normalises those.

## Azure setup (one-time)

1. **Create an External ID tenant** (Microsoft Entra admin center → *External Identities*).
2. **App registration — API** (`plutus-api`): expose an API / set the Application ID URI; this
   is the token **audience**. Note the client id.
3. **App registrations — SPAs**: `plutus-portal` and `plutus-webpos`, platform *Single-page
   application*, redirect URIs:
   - portal: `https://admin.plutus.huggett.dscloud.me/` (+ `http://localhost:5274/` for dev)
   - web POS: `https://plutus.huggett.dscloud.me/` (+ `http://localhost:5173/` for dev)
   Grant each delegated access to the `plutus-api` scope. PKCE is automatic for the SPA platform.
4. **User flow**: create a Sign-up/Sign-in user flow and associate the apps. Ensure the **email**
   claim is emitted in the token (the seam maps identity→Plutus user by verified email).

## Backend config to switch to Entra

In `~/PLUTUS/plutus-ecosystem.config.js` (or appsettings):

```
IdP__Provider = entra
IdP__Entra__Authority = https://<subdomain>.ciamlogin.com/<tenantId>/v2.0
IdP__Entra__Audience  = <plutus-api client id, or api://<clientId>>
```

Restart `plutus-backend`. The `scope`/`perm:*` authorization is unchanged — the transformation
injects the same RBAC scopes it does under Keycloak, so **zero API contract change**.

## Claim mapping notes (already handled by the seam)

- `MapInboundClaims=false` on the JwtBearer options stops Entra's `sub`/`oid` from masquerading as
  the Plutus `NameIdentifier`; the transformation stamps the real EmployeeId after email match.
- Email is read from `email` → `preferred_username` → `upn` (first present).
- The legacy `[RequiredScope]` `scp` API scopes are injected by the transformation, so the generic
  CRUD controllers keep working exactly as under the test/Keycloak providers.

## Frontend config

Both React apps read `OIDC:Authority` / `OIDC:ClientId` at build/deploy (see each app's
`.env`). For Entra:
- portal: authority = `https://<subdomain>.ciamlogin.com/<tenantId>/v2.0`, clientId = `plutus-portal` app id.
- web POS: same authority, clientId = `plutus-webpos` app id.

The apps use auth-code + PKCE with the access token held in memory and refresh via cookie —
identical flow to Keycloak, only the authority/clientId change.
